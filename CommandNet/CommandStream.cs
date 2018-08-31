using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
using CommandNet.Serializer;
using CaptainLib.Collections;
using log4net;

namespace CommandNet
{
    class ReadContext
    {
        public byte[] Array;
        public int Offset;
        public int Len;
        public int Read;
        public EndPoint Remote;
        public Exception Exception;
    }

    class CommandStream : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(CommandStream));
        private static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(5);
        private int _commandId = 0;
        private readonly Stream _stream;
        private readonly ICommandSerializer _serializer;
        private readonly Socket _socket;
        private readonly ObjectPool<byte> _bytesPool;
        private readonly AutoResetEvent _resetEvent = new AutoResetEvent(true);

        public CommandStats Stats { get; } = new CommandStats();
        public int Id { get; }

        public CommandStream(int streamId, TcpClient tcpClient, ICommandSerializer serializer)
        {
            _bytesPool = new ObjectPool<byte>(100, () => new byte());
            _stream = tcpClient.GetStream();
            _socket = tcpClient.Client;
            Id = streamId;
            _serializer = serializer;
        }

        public void Close()
        {
            Log.Debug($"Closing stream {Id}");
            Dispose();
        }

        private async Task<bool> WrappedWriteOperation(byte[] buffer, int offset, int size)
        {
            var r = await WrappedOperation(async () => await _stream.WriteAsync(buffer, offset, size), null).ConfigureAwait(false);
            if(r.exception != null)
                Log.Error(r.exception);
            return r.result;
        }

        private async Task<bool> WrappedReadOperation(ReadContext ctx, CancellationToken cancellation)
        {
            var before = ctx.Read;
            var r = await WrappedOperation(async () => ctx.Read += await _stream.ReadAsync(ctx.Array, ctx.Offset + ctx.Read, ctx.Len - ctx.Read, cancellation).ConfigureAwait(false), ctx.Remote).ConfigureAwait(false);
            ctx.Exception = r.exception;
            if (!r.result)
                return false;
            if (ctx.Read - before < 1)
            {
                Log.Debug($"0 bytes read from {ctx.Remote}. Closing stream");
                return false;
            }
            return true;
        }

        private async Task<(bool result, Exception exception)> WrappedOperation(Func<Task> action, EndPoint remote)
        {
            Exception exOut = null;
            try
            {
                await action();
                return (true, null);
            }
            catch (ObjectDisposedException e)
            {
                Log.Info("Connection closed by us");
            }
            catch (IOException e)
            {
                if (e.InnerException is SocketException socketException)
                {
                    if (socketException.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        Log.Info($"Connection {remote} reset by remote");
                    }
                    else
                    {
                        Log.Error($"Socket error: {socketException.SocketErrorCode}");
                    }
                }

                if (e.InnerException is ObjectDisposedException disposedException)
                {
                    Log.Info($"Connection {remote} closed by us");
                }
                else
                    exOut = e;
            }
            catch (Exception e)
            {
                exOut = e;
            }

            return (false, exOut);
        }

        private byte[] _header = new byte[16];
        private async Task<bool> ReadBytes(byte[] buffer, int offset, int count)
        {
            EndPoint remote;
            try
            {
                remote = _socket.RemoteEndPoint;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            var ctx = new ReadContext()
            {
                Array = buffer,
                Offset = offset,
                Read = 0,
                Len = count,
                Remote = remote
            };
            var cancelation = new CancellationTokenSource();
            while (!_resetEvent.WaitOne(1000))
            {
                if (cancelation.IsCancellationRequested)
                    return false;
            }
            try
            {
                do
                {
                    cancelation.CancelAfter(InactivityTimeout);
                    if (cancelation.IsCancellationRequested)
                        continue;
                    var r = await WrappedReadOperation(ctx, cancelation.Token).ConfigureAwait(false);
                    if (cancelation.IsCancellationRequested)
                    {
                        Log.Info($"{remote} is inactive for {InactivityTimeout} secs. Connection closed.");
                        return false;
                    }

                    if (!r)
                        return false;
                    if (ctx.Exception != null)
                        throw ctx.Exception;
                    if (ctx.Read == ctx.Len)
                        return true;
                    if (!_stream.CanRead || !_socket.Connected)
                        return false;
                } while (ctx.Read != ctx.Len);
            }
            finally
            {
                _resetEvent.Set();
            }

            return true;
        }

        public async Task<(Command command, int commandId, int tag, int size)> ReadCommand()
        {
            var commandId = 0;
            var tag = 0;
            var size = 0;
            var r = await ReadBytes(_header, 0, _header.Length).ConfigureAwait(false);
            if (!r)
                return (null, commandId, tag, size);
            var packet = Create(_header);
            r = await ReadBytes(packet.Payload, 0, packet.PayloadSize).ConfigureAwait(false);
            if (!r)
                return (null, commandId, tag, size);
            var command = _serializer.Deserialize(packet.Payload);
            commandId = packet.CommandId;
            tag = packet.CommandTag;
            size = packet.PayloadSize + 16;
            Stats.StatReceive(size);
            return (command, commandId, tag, size);
        }

        public async Task<(long commandId, int size)> WriteCommand(Command command, int tag = 0)
        {
            var size = 0;
            int commandId = Interlocked.Increment(ref _commandId);
            var payload = _serializer.Serialize(command);
            var sz = 16 + payload.Length;
            using (var buff = _bytesPool.AllocArray(sz))
            {
                byte[] data = buff;
                BitHelper.WriteIntToArray(data, 0, Id);
                BitHelper.WriteIntToArray(data, 4, payload.Length);
                BitHelper.WriteIntToArray(data, 8, commandId);
                BitHelper.WriteIntToArray(data, 12, tag);
                payload.CopyTo(data, 16);
                var r = await WrappedWriteOperation(data, 0, sz).ConfigureAwait(false);
                if (!r)
                    return (0, 0);
                size = sz;
                Stats.StatSend(sz);
            }
            return (GetCommandId(Id, commandId), size);
        }

        public static long GetCommandId(int streamId, int commandId)
        {
            return ((long)streamId << 32) + commandId;
        }

        private static CommandPacket Create(byte[] head)
        {
            var streamId = BitHelper.ReadIntFromArray(head, 0);
            var payloadSize = BitHelper.ReadIntFromArray(head, 4);
            var commandId = BitHelper.ReadIntFromArray(head, 8);
            var tag = BitHelper.ReadIntFromArray(head, 12);
            return new CommandPacket
            {
                StreamId = streamId,
                PayloadSize = payloadSize,
                CommandId = commandId,
                CommandTag = tag,
                Payload = new byte[payloadSize]
            };
        }

        public void Dispose()
        {
            try
            {
                _socket.Close();
            }
            catch {}
            try
            {
                _stream?.Close();
            }
            catch {}
        }
    }
}
