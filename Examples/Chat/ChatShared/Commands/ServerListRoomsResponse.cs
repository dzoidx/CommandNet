using System;
using System.Collections.Generic;
using System.Text;
using CommandNet;

namespace ChatShared.Commands
{
    public class ServerListRoomsResponse : ServerResponseBase
    {
        public string[] RoomNames;
    }
}
