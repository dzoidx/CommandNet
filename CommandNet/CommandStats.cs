using System;
using System.Threading;
using System.Collections.Generic;
using System.Text;

namespace CommandNet
{
    public class CommandStats
    {
        private long _commandsSend;
        private long _commandsReceived;
        private long _bytesSend;
        private long _bytesReceived;

        public long CommandsSend => _commandsSend;
        public long CommandsReceived => _commandsReceived;
        public long BytesSend => _bytesSend;
        public long BytesReceived => _bytesReceived;

        public void StatSend(long bytes)
        {
            Interlocked.Exchange(ref _commandsSend, _commandsSend + 1);
            Interlocked.Exchange(ref _bytesSend, _bytesSend + bytes);
        }

        public void StatReceive(long bytes)
        {
            Interlocked.Exchange(ref _commandsReceived, _commandsReceived + 1);
            Interlocked.Exchange(ref _bytesReceived, _bytesReceived + bytes);
        }
    }
}
