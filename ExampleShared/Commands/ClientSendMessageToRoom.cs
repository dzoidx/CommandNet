using System;
using System.Collections.Generic;
using System.Text;
using CommandNet;

namespace ChatShared.Commands
{
    public class ClientSendMessageToRoom : Command
    {
        public string Room;
        public string Message;
    }
}
