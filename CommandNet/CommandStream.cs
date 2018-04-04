using System;
using System.IO;
using System.Threading;
using System.Net.Sockets;
using CommandNet.Serializer;
using CaptainLib.Collections;
using log4net;

namespace CommandNet
{
    class CommandStream
    {
        private readonly static ILog Log = LogManager.GetLogger(typeof(CommandStream));
        private int _commandId = 0;
        private Stream _stream;
        private ICommandSerializer _serializer;
        private int _streamId;
        private ObjectPool<byte> _bytesPool;
        private CommandStats _stats = new CommandStats();

        public CommandStats Stats { get { return _stats; } }

        public int Id { get { return _streamId; } }

        public CommandStream(int streamId, Stream stream, ICommandSerializer serializer)
        {
            _bytesPool = new ObjectPool<byte>(100, () => new byte());
            _stream = stream;
            _streamId = streamId;
            _serializer = serializer;
        }

        public void Close()
        {
            _stream.Close();
        }

        private bool WrappedOperation(Action action)
        {
            try
            {
                action();
                return true;
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
                        Log.Info($"Connection closed by remote");
                    }
                    else
                    {
                        Log.Error($"Socket error: {socketException.SocketErrorCode}");
                    }
                }
                if (e.InnerException is ObjectDisposedException disposedException)
                {
                    Log.Info("Connection closed by us");
                }
            }
            return false;
        }

        private byte[] _header = new byte[16];
        private bool ReadBytes(byte[] buffer, int offset, int count)
        {
            var read = 0;
            do
            {
                var r = WrappedOperation(() => read += _stream.Read(buffer, offset + read, count - read));
                if (!r)
                    return false;
            } while (read != count);
            return true;
        }

        public Command ReadCommand(out int commandId, out int tag, out int size)
        {
            commandId = 0;
            tag = 0;
            size = 0;
            var r = ReadBytes(_header, 0, _header.Length);
            if (!r)
                return null;
            var packet = Create(_header);
            r = ReadBytes(packet.Payload, 0, packet.PayloadSize);
            if (!r)
                return null;
            var command = _serializer.Deserialize(packet.Payload);
            commandId = packet.CommandId;
            tag = packet.CommandTag;
            size = packet.PayloadSize + 16;
            _stats.StatReceive(size);
            return command;
        }

        public long WriteCommand(Command command, out int size, int tag = 0)
        {
            size = 0;
            int commandId = Interlocked.Increment(ref _commandId);
            var payload = _serializer.Serialize(command);
            var sz = 16 + payload.Length;
            using (var buff = _bytesPool.AllocArray(sz))
            {
                byte[] data = buff;
                BitHelper.WriteIntToArray(data, 0, _streamId);
                BitHelper.WriteIntToArray(data, 4, payload.Length);
                BitHelper.WriteIntToArray(data, 8, commandId);
                BitHelper.WriteIntToArray(data, 12, tag);
                payload.CopyTo(data, 16);
                var r = WrappedOperation(() => _stream.Write(data, 0, sz));
                if (!r)
                    return 0;
                size = sz;
                _stats.StatSend(sz);
            }
            return GetCommandId(_streamId, commandId);
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
    }
}
