using System;
using System.Collections.Generic;
using System.Text;
using CommandNet;

namespace ChatShared.Commands
{
    public class ServerServiceMessage : Command
    {
        public string[] RoomNames;
        public string Message;
    }
}
