﻿using System;
using System.Collections.Generic;
using System.Text;
using CommandNet;

namespace ChatShared.Commands
{
    public enum ServerResponseStatus
    {
        Success,
        Error
    }

    public class ServerResponseBase : Command
    {
        public ServerResponseStatus Status;
        public string Description;
    }
}
