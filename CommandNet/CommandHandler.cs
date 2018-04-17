using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using System.Threading;
using System.Net.Sockets;
using CommandNet.Serializer;
using System.Reflection;
using log4net;

namespace CommandNet
{
    class CommandHanderStreamContext
    {
        public int StreamId;
        public CommandStream Stream;
        public ManualResetEventSlim InitEvent;
    }

    public class CommandAnswerContext
    {
        private bool _answered;
        private CommandStats _stats;
        private CommandStream _stream;
        private int _tag;

        internal CommandAnswerContext(CommandStats stats, CommandStream stream, int tag)
        {
            _stats = stats;
            _stream = stream;
            _tag = tag;
        }

        public bool Answered { get { return _answered; } }

        public bool TryAnswer(Command ans)
        {
            if (_answered)
                return false;
            try
            {
                int size;
                var r = _stream.WriteCommand(ans, out size, _tag);
                if(r > 0)
                    _stats.StatSend(size);
            }
            catch (Exception)
            {
                return false;
            }
            _answered = true;
            return true;
        }
    }

    public abstract class RequestBase
    {
        protected ManualResetEvent _event = new ManualResetEvent(false);
        public DateTime StartTime { get; private set; }
        public bool IsComplete { get; protected set; }
        public bool IsClosed { get; private set; }
        internal abstract bool HasStream(int streamId);
        internal abstract void SetResult(Command result, int streamId);

        protected RequestBase()
        {
            StartTime = DateTime.UtcNow;
        }

        internal void Close()
        {
            IsClosed = true;
            _event.Set();
        }
    }

    public class RequestsContext<T> : RequestBase where T : Command
    {
        private T[] _result;
        private Dictionary<int, T> _results = new Dictionary<int, T>();
        private List<int> _pendingStreams = new List<int>();

        public T[] Result { get {
                _event.WaitOne();
                return _result;
            } }

        internal override bool HasStream(int streamId)
        {
            return _pendingStreams.Contains(streamId);
        }

        internal RequestsContext(params int[] streamIds)
        {
            _pendingStreams.AddRange(streamIds);
            for (var i = 0; i < streamIds.Length; ++i)
            {
                _results[i] = null;
            }
        }

        internal override void SetResult(Command result, int streamId)
        {
            var c = result as T;
            if (c == null)
                throw new ArgumentException(string.Format("Unexpected result type '{0}'", result.GetType().Name));
            SetResult(c, streamId);
        }

        private void SetResult(T result, int streamId)
        {
            lock (_results)
            {
                if (_results[streamId] != null)
                    throw new ArgumentException(string.Format("Result for stream {0} already set", streamId));
                _results[streamId] = result;
                _pendingStreams.Remove(streamId);
                _result = _results.Values.ToArray();
            }
            if (_pendingStreams.Count < 1)
            {
                IsComplete = true;
                _event.Set();
            }
        }
    }

    public class RequestContext<T> : RequestBase where T: Command
    {
        private ManualResetEvent _event = new ManualResetEvent(false);
        private T _result;
        public int StreamId { get; private set; }
        public T Result { get
            {
                _event.WaitOne();
                return _result;
            }
        }

        public event Action<T> OnResult = (r) => { };

        internal override bool HasStream(int streamId)
        {
            return StreamId == streamId;
        }

        internal RequestContext(int streamId)
        {
            StreamId = streamId;
        }

        internal override void SetResult(Command result, int streamId)
        {
            var c = result as T;
            if (c == null)
                throw new ArgumentException(string.Format("Unexpected result type '{0}'", result.GetType().Name));
            SetResult(c, streamId);
        }

        private void SetResult(T result, int streamId)
        {
            _result = result;
            OnResult(_result);
            _event.Set();
            IsComplete = true;
        }
    }

    public class CommandHandler : IDisposable
    {
        private readonly static ILog Log = LogManager.GetLogger(typeof(CommandHandler));
        private int _streamCount;
        private ICommandSerializer _serializer;
        private volatile bool _disposed;
        private List<Thread> _threads = new List<Thread>();
        private Dictionary<Type, List<object>> _handlers = new Dictionary<Type, List<object>>();
        private Dictionary<Type, MethodInfo> _handlersMap = new Dictionary<Type, MethodInfo>();
        private Dictionary<int, CommandHanderStreamContext> _streams = new Dictionary<int, CommandHanderStreamContext>();
        private Dictionary<long, RequestBase> _unansweredRequests = new Dictionary<long, RequestBase>();
        private CommandStats _stats = new CommandStats();

        public delegate void CommandHandlerDelegate<T>(T command, int streamId, CommandAnswerContext answerContext);

        public CommandStats Stats { get { return _stats; } }
        public event Action<int> OnClose = (s) => { };
        public TimeSpan RequestTimeout { get; set; }

        public CommandHandler(ICommandSerializer serializer)
        {
            RequestTimeout = TimeSpan.FromSeconds(30);
            _serializer = serializer;
        }

        public int AddSource(Stream stream)
        {
            var streamId = Interlocked.Increment(ref _streamCount);
            var thread = new Thread(CommandStreamLoop);
            thread.Name = "CommandStream_" + streamId + "_Loop";
            thread.IsBackground = true;
            var s = new CommandStream(streamId, stream, _serializer);
            var ctx = new CommandHanderStreamContext
            {
                StreamId = streamId,
                Stream = s,
                InitEvent = new ManualResetEventSlim(false)
            };
            thread.Start(ctx);
            _threads.Add(thread);
            ctx.InitEvent.Wait();
            return streamId;
        }

        public void RegisterHandler<T>(CommandHandlerDelegate<T> handler) where T : Command
        {
            var t = typeof(T);
            List<object> list;
            if (!_handlers.TryGetValue(t, out list))
            {
                list = new List<object>();
                _handlers[t] = list;
            }
            lock(list)
                list.Add(handler);
        }

        public void UnregisterHandler<T>(CommandHandlerDelegate<T> handler) where T : Command
        {
            var t = typeof(T);
            List<object> list;
            if (!_handlers.TryGetValue(t, out list))
                return;
            lock(list)
                list.Remove(handler);
        }

        public RequestsContext<ResultT> Request<RequestT, ResultT>(RequestT request) where RequestT : Command where ResultT : Command
        {
            CommandHanderStreamContext[] contexts;
            lock (_streams)
                contexts = _streams.Values.ToArray();
            var requestContext = new RequestsContext<ResultT>(contexts.Select(_ => _.StreamId).ToArray());
            lock (_unansweredRequests)
            {
                for (var i = 0; i < contexts.Length; ++i)
                {
                    var c = contexts[i];
                    int size;
                    var uId = c.Stream.WriteCommand(request, out size);
                    if (uId < 1)
                    {
                        return null;
                    }
                    _stats.StatSend(size);
                    _unansweredRequests.Add(uId, requestContext);
                }
            }
            return requestContext;
        }

        public RequestContext<ResultT> Request<RequestT, ResultT>(RequestT request, int streamId) where RequestT : Command where ResultT : Command
        {
            CommandHanderStreamContext context;
            lock (_streams)
                context = _streams.Values.First(_ => _.StreamId == streamId);
            var requestContext = new RequestContext<ResultT>(streamId);
            lock (_unansweredRequests)
            {
                int size;
                var uId = context.Stream.WriteCommand(request, out size);
                if (uId < 1)
                {
                    return null;
                }
                _stats.StatSend(size);
                _unansweredRequests.Add(uId, requestContext);
            }
            return requestContext;
        }

        public void Notify(Command command)
        {
            int[] streamIds;
            lock (_streams)
                streamIds = _streams.Values.Select(_ => _.StreamId).ToArray();
            Notify(command, streamIds);
        }

        public void Notify(Command command, params int[] streamIds)
        {
            CommandHanderStreamContext[] contexts;
            lock (_streams)
                contexts = _streams.Where(_ => streamIds.Contains(_.Key)).Select(_ => _.Value).ToArray();
            for (var i = 0; i < contexts.Length; ++i)
            {
                var s = contexts[i];
                int size;
                var r = s.Stream.WriteCommand(command, out size);
                if (r > 0)
                {
                    _stats.StatSend(size);
                }
            }
        }

        private void HandleCommand<T>(T command, CommandStream stream, int commandId, int streamId) where T : Command
        {
            var t = typeof(T);
            List<object> list;
            if (!_handlers.TryGetValue(t, out list))
                return;

            lock (list)
            {
                for (var i = 0; i < list.Count; ++i)
                {
                    var h = (CommandHandlerDelegate<T>)list[i];
                    ThreadPool.QueueUserWorkItem((o) => h(command, streamId, new CommandAnswerContext(_stats, stream, commandId)));
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            lock (_streams)
            {
                foreach (var s in _streams)
                    s.Value.Stream.Close();
            }
            foreach (var t in _threads)
            {
                t.Join();
            }
        }

        private MethodInfo GetHandlerMethod(Type t)
        {
            MethodInfo result;
            lock (_handlersMap)
                _handlersMap.TryGetValue(t, out result);
            if (result != null)
                return result;

            var handleGeneric = GetType().GetMethod("HandleCommand", BindingFlags.NonPublic | BindingFlags.Instance);
            var handleInst = handleGeneric.MakeGenericMethod(t);
            lock (_handlersMap)
                _handlersMap[t] = handleInst;
            return handleInst;
        }

        private void CommandStreamLoop(object o)
        {
            var ctx = (CommandHanderStreamContext)o;
            lock (_streams)
                _streams.Add(ctx.StreamId, ctx);
            ctx.InitEvent.Set();
            try
            {
                while (!_disposed)
                {
                    int commandId;
                    int tag;
                    int size;
                    var command = ctx.Stream.ReadCommand(out commandId, out tag, out size);
                    if (command == null)
                    {
                        Log.InfoFormat($"End of stream {ctx.StreamId}");
                        return;
                    }
                    _stats.StatReceive(size);
                    if (tag == 0)
                    {
                        var handle = GetHandlerMethod(command.GetType());
                        ThreadPool.QueueUserWorkItem((_) => handle.Invoke(this, new object[] { command, ctx.Stream, commandId, ctx.Stream.Id }));
                    }
                    else
                    {
                        var uId = CommandStream.GetCommandId(ctx.StreamId, tag);
                        lock (_unansweredRequests)
                        {
                            RequestBase req;
                            if (!_unansweredRequests.TryGetValue(uId, out req))
                            {
                                Log.Warn($"No unanswered requests with tag {tag}. Uniq command id: {uId}");
                            }
                            else
                            {
                                req.SetResult(command, ctx.StreamId);
                                _unansweredRequests.Remove(uId);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                ctx.Stream.Close();
                lock (_streams)
                    _streams.Remove(ctx.StreamId);
                OnClose(ctx.StreamId);
            }
        }
    }
}
