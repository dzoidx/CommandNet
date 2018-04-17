using CommandNet;

namespace ChatShared.Commands
{
    public class ServerRoomMessage : Command
    {
        public string Room;
        public string FromUser;
        public string Message;
    }
}
