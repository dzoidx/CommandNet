using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using System.Threading;
using System.Net.Sockets;
using CommandNet.Serializer;
using System.Reflection;
using System.Threading.Tasks;
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
        private CommandStats _stats;
        private CommandStream _stream;
        private int _tag;

        internal CommandAnswerContext(CommandStats stats, CommandStream stream, int tag)
        {
            _stats = stats;
            _stream = stream;
            _tag = tag;
        }

        public bool Answered { get; private set; }

        public async Task<bool> TryAnswer(Command ans)
        {
            if (Answered)
                return false;
            try
            {
                var r = await _stream.WriteCommand(ans, _tag);
                if(r.commandId > 0)
                    _stats.StatSend(r.size);
            }
            catch (Exception)
            {
                return false;
            }
            Answered = true;
            return true;
        }
    }

    abstract class RequestBase
    {
        public DateTime StartTime { get; }
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
        }
    }

    class RequestsContext<T> : RequestBase where T : Command
    {
        private readonly Dictionary<int, T> _results = new Dictionary<int, T>();
        private readonly List<int> _pendingStreams = new List<int>();
        private readonly TaskCompletionSource<T[]> _tcs = new TaskCompletionSource<T[]>();
        public Task<T[]> Awaiter => _tcs.Task; 

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
                throw new ArgumentException($"Unexpected result type '{result.GetType().Name}'");
            SetResult(c, streamId);
        }

        private void SetResult(T result, int streamId)
        {
            lock (_results)
            {
                if (_results[streamId] != null)
                    throw new ArgumentException($"Result for stream {streamId} already set");
                _results[streamId] = result;
                _pendingStreams.Remove(streamId);
                _tcs.SetResult(_results.Values.ToArray());
            }
            if (_pendingStreams.Count < 1)
            {
                IsComplete = true;
            }
        }
    }

    class RequestContext<T> : RequestBase where T: Command
    {
        private readonly TaskCompletionSource<T> _tcs = new TaskCompletionSource<T>();
        public int StreamId { get; }
        public Task<T> Awaiter => _tcs.Task;

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
            _tcs.SetResult(result);
            OnResult(result);
            IsComplete = true;
        }
    }

    public class CommandHandler : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(CommandHandler));
        private int _streamCount;
        private readonly ICommandSerializer _serializer;
        private volatile bool _disposed;
        private readonly List<Task> _tasks = new List<Task>();
        private readonly List<Thread> _threads = new List<Thread>();
        private readonly Dictionary<Type, List<object>> _handlers = new Dictionary<Type, List<object>>();
        private readonly Dictionary<Type, MethodInfo> _handlersMap = new Dictionary<Type, MethodInfo>();
        private readonly Dictionary<int, CommandHanderStreamContext> _streams = new Dictionary<int, CommandHanderStreamContext>();
        private readonly ConcurrentDictionary<long, RequestBase> _unansweredRequests = new ConcurrentDictionary<long, RequestBase>();
        private readonly CommandStats _stats = new CommandStats();

        public delegate void CommandHandlerDelegate<T>(T command, int streamId, CommandAnswerContext answerContext);

        public CommandStats Stats { get { return _stats; } }
        public event Action<int> OnClose = (s) => { };
        public TimeSpan RequestTimeout { get; set; }
        public bool IsConnected => _streams.Count > 0;

        public CommandHandler(ICommandSerializer serializer)
        {
            RequestTimeout = TimeSpan.FromSeconds(30);
            _serializer = serializer;
        }

        public int AddSource(TcpClient client)
        {
            var streamId = Interlocked.Increment(ref _streamCount);
            var s = new CommandStream(streamId, client, _serializer);
            var ctx = new CommandHanderStreamContext
            {
                StreamId = streamId,
                Stream = s,
                InitEvent = new ManualResetEventSlim(false)
            };
            var t = CommandStreamLoop(ctx);
            _tasks.Add(t);
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

        public async Task<ResultT[]> Request<RequestT, ResultT>(RequestT request) where RequestT : Command where ResultT : Command
        {
            CommandHanderStreamContext[] contexts;
            lock (_streams)
                contexts = _streams.Values.ToArray();
            var requestContext = new RequestsContext<ResultT>(contexts.Select(_ => _.StreamId).ToArray());
            for (var i = 0; i < contexts.Length; ++i)
            {
                var c = contexts[i];
                var r = await c.Stream.WriteCommand(request).ConfigureAwait(false);
                if (r.commandId < 1)
                {
                    return null;
                }
                _stats.StatSend(r.size);
                _unansweredRequests.TryAdd(r.commandId, requestContext);
            }
            return await requestContext.Awaiter;
        }

        public async Task<ResultT> Request<RequestT, ResultT>(RequestT request, int streamId) where RequestT : Command where ResultT : Command
        {
            CommandHanderStreamContext context;
            lock (_streams)
                context = _streams.Values.First(_ => _.StreamId == streamId);
            var requestContext = new RequestContext<ResultT>(streamId);
            var r = await context.Stream.WriteCommand(request).ConfigureAwait(false);
            if (r.commandId < 1)
            {
                return null;
            }
            _stats.StatSend(r.size);
            _unansweredRequests.TryAdd(r.commandId, requestContext);
            return await requestContext.Awaiter;
        }

        public async Task Notify(Command command)
        {
            int[] streamIds;
            lock (_streams)
                streamIds = _streams.Values.Select(_ => _.StreamId).ToArray();
            await Notify(command, streamIds);
        }

        public async Task Notify(Command command, params int[] streamIds)
        {
            CommandHanderStreamContext[] contexts;
            lock (_streams)
                contexts = _streams.Where(_ => streamIds.Contains(_.Key)).Select(_ => _.Value).ToArray();
            for (var i = 0; i < contexts.Length; ++i)
            {
                var s = contexts[i];
                var r = await s.Stream.WriteCommand(command);
                if (r.commandId > 0)
                {
                    _stats.StatSend(r.size);
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

        private async Task CommandStreamLoop(CommandHanderStreamContext ctx)
        {
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
                    Command command;
                    (command, commandId, tag, size) = await ctx.Stream.ReadCommand().ConfigureAwait(false);
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
                                _unansweredRequests.TryRemove(uId, out var t);
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
