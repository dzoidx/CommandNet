﻿using System;
using System.Collections.Generic;
using System.Text;
using CommandNet;

namespace ChatShared.Commands
{
    public class ServerListRoomUsersResponse : ServerResponseBase
    {
        public string[] UsersNames;
    }
}
