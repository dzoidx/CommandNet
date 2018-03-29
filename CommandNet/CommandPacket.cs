using System;
using System.Collections.Generic;
using System.Text;

namespace CommandNet
{
    class CommandPacket
    {
        public int StreamId;
        public int PayloadSize;
        public int CommandId;
        public int CommandTag;
        public byte[] Payload;
    }
}
