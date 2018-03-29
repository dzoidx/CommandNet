using System;
using System.Collections.Generic;
using System.Text;

namespace CommandNet.Serializer
{
    public interface ICommandSerializer
    {
        byte[] Serialize(Command o);
        Command Deserialize(byte[] data);
    }
}
