using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using CommandNet;
using CommandNet.Serializer;
using ChatShared.Commands;

namespace ChatServer
{
    class UserContext
    {
        public string Name;
        public List<string> Rooms;
        public int Stream;
    }

    class ChatServer
    {
        private CommandHandler _commandHandler;
        private TcpListener _server;
        private List<UserContext> _users = new List<UserContext>();

        public CommandStats Stat { get { return _commandHandler.Stats; } }
        public ChatServer(int port, ICommandSerializer serializer)
        {
            _server = new TcpListener(IPAddress.Any, port);
            _commandHandler = new CommandHandler(serializer);
            _commandHandler.RegisterHandler<ClientLoginRequest>(LoginHandler);
            _commandHandler.RegisterHandler<ClientJoinRoomRequest>(JoinRoomHandler);
            _commandHandler.RegisterHandler<ClientSendMessageToRoom>(RoomMessageHandler);
            _commandHandler.OnClose += OnCloseStream;
            _server.Start();
            FetchConnection();
        }

        private void OnCloseStream(int streamId)
        {
            lock (_users)
            {
                var u = _users.FirstOrDefault(usr => usr.Stream == streamId);
                if (u == null)
                    return;
                Console.WriteLine($"{u.Name} disconnected");
                _users.Remove(u);
            }
        }

        private void FetchConnection()
        {
            _server.AcceptTcpClientAsync()
                .ContinueWith(ProcessConnection);
        }

        private void ProcessConnection(Task<TcpClient> t)
        {
            try
            {
                var client = t.Result;
                var streamId = _commandHandler.AddSource(client.GetStream());
                var remote = client.Client.RemoteEndPoint.ToString();
                Console.WriteLine($"New client {remote}");
            }
            catch (Exception e)
            {
                // TODO: log
                Console.WriteLine(e);
            }
            finally
            {
                FetchConnection();
            }
        }

        private void RoomMessageHandler(ClientSendMessageToRoom command, int streamId, CommandAnswerContext answerContext)
        {
            var result = new ServerResponseBase();
            var message = command.Message;
            var room = command.Room;
            lock (_users)
            {
                var ctx = _users.FirstOrDefault(u => u.Stream == streamId);
                if (ctx == null)
                {
                    result.Status = ServerResponseStatus.Error;
                    result.Description = "User not logged in";
                    answerContext.TryAnswer(result);
                    return;
                }
                if (!ctx.Rooms.Contains(room))
                {
                    result.Status = ServerResponseStatus.Error;
                    result.Description = $"You not in the room '{room}'!";
                    answerContext.TryAnswer(result);
                    return;
                }
                var dests = _users.Where(u => u.Rooms.Contains(room)).Select(u => u.Stream).ToArray();
                var messageCmd = new ServerRoomMessage
                {
                    FromUser = ctx.Name,
                    Message = message,
                    Room = room
                };
                _commandHandler.Notify(messageCmd, dests);
            }
        }

        private void JoinRoomHandler(ClientJoinRoomRequest command, int streamId, CommandAnswerContext answerContext)
        {
            var result = new ServerResponseBase();
            UserContext ctx = null;
            var room = command.RoomName;
            lock (_users)
            {
                ctx = _users.FirstOrDefault(u => u.Stream == streamId);
                if (ctx == null)
                {
                    result.Status = ServerResponseStatus.Error;
                    result.Description = "User not logged in";
                    answerContext.TryAnswer(result);
                    return;
                }
                if (ctx.Rooms.Contains(room))
                {
                    result.Status = ServerResponseStatus.Error;
                    result.Description = $"Already in room {room}";
                    answerContext.TryAnswer(result);
                    return;
                }
                ctx.Rooms.Add(room);
            }
            result.Status = ServerResponseStatus.Success;
            answerContext.TryAnswer(result);
            _commandHandler.Notify(new ServerServiceMessage() { Message = $"{ctx.Name}, welcome to '{room}'!" }, streamId);
        }

        private void LoginHandler(ClientLoginRequest command, int streamId, CommandAnswerContext answerContext)
        {
            var result = new ServerLoginResponse();
            if (string.IsNullOrEmpty(command.UserName))
            {
                result.Status = ServerResponseStatus.Error;
                result.Description = "Invalid name";
                answerContext.TryAnswer(result);
                return;
            }
            var name = command.UserName;
            lock (_users)
            {
                if (_users.Any(u => u.Name == name))
                {
                    result.Status = ServerResponseStatus.Error;
                    result.Description = $"User with name '{name}' already logged in";
                    answerContext.TryAnswer(result);
                    return;
                }
                var ctx = new UserContext
                {
                    Name = command.UserName,
                    Rooms = new List<string>(),
                    Stream = streamId
                };
                _users.Add(ctx);
            }
            result.Status = ServerResponseStatus.Success;
            answerContext.TryAnswer(result);
            _commandHandler.Notify(new ServerServiceMessage() { Message = $"Hello, {name}!" }, streamId);
        }
    }
}
