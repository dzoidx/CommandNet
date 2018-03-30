using System;
using System.IO;
using System.Threading;
using CommandNet.Serializer;
using CaptainLib.Collections;

namespace CommandNet
{
    class CommandStream
    {
        private int _commandId = 0;
        private Stream _stream;
        private ICommandSerializer _serializer;
        private int _streamId;
        private ObjectPool<byte> _bytesPool;

        public int Id { get { return _streamId; } }

        public CommandStream(int streamId, Stream stream, ICommandSerializer serializer)
        {
            _bytesPool = new ObjectPool<byte>(100, () => new byte());
            _stream = stream;
            _streamId = streamId;
            _serializer = serializer;
        }

        private byte[] _header = new byte[16];
        private void ReadBytes(byte[] buffer, int offset, int count)
        {
            var read = 0;
            do
            {
                read += _stream.Read(buffer, offset + read, count - read);
            } while (read != count);
        }

        public Command ReadCommand(out int commandId, out int tag)
        {
            ReadBytes(_header, 0, _header.Length);
            var packet = Create(_header);
            ReadBytes(packet.Payload, 0, packet.PayloadSize);
            var command = _serializer.Deserialize(packet.Payload);
            commandId = packet.CommandId;
            tag = packet.CommandTag;
            return command;
        }

        public long WriteCommand(Command command, int tag = 0)
        {
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
                _stream.Write(data, 0, sz);
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
