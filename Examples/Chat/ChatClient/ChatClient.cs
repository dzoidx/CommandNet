using System;
using System.Collections.Generic;
using System.Text;
using CommandNet;
using CommandNet.Serializer;
using ChatShared.Commands;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ChatClient
{
    public class ChatClient : IDisposable
    {
        private CommandHandler _commandHandler;
        private TcpClient _client;
        private string _host;
        private int _port;

        public bool IsConnected { get { return _client.Connected && _commandHandler.IsConnected; } }
        public CommandStats Stat => _commandHandler.Stats;

        internal ChatClient(string host, int port, ICommandSerializer serializer)
        {
            _host = host;
            _port = port;
            _client = new TcpClient();
            _commandHandler = new CommandHandler(serializer);
            _commandHandler.RegisterHandler<ServerRoomMessage>(OnRoomMessage);
            _commandHandler.RegisterHandler<ServerServiceMessage>(OnServiceMessage);
            _commandHandler.OnClose += OnClose;
            ValidateConnection().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        Console.WriteLine("Connection failed");
                }
            );
        }

        public Task<ServerLoginResponse> Login(string userName)
        {
            return _commandHandler.Request<ClientLoginRequest, ServerLoginResponse>(new ClientLoginRequest { UserName = userName }, 1);
        }

        public Task<ServerResponseBase> JoinRoom(string roomName)
        {
            return _commandHandler.Request<ClientJoinRoomRequest, ServerResponseBase>(new ClientJoinRoomRequest { RoomName = roomName }, 1);
        }

        public Task<ServerResponseBase> SendMessage(string room, string message)
        {
            return _commandHandler.Request<ClientSendMessageToRoom, ServerResponseBase>(new ClientSendMessageToRoom { Room = room, Message = message }, 1);
        }

        public Task<ServerListRoomsResponse> ListRooms()
        {
            return _commandHandler.Request<ClientListRoomsRequest, ServerListRoomsResponse>(new ClientListRoomsRequest(), 1);
        }

        public Task<ServerListRoomUsersResponse> ListRoomUsers(string roomName)
        {
            return _commandHandler.Request<ClientListRoomUsersRequest, ServerListRoomUsersResponse>(new ClientListRoomUsersRequest { RoomName = roomName }, 1);
        }

        private async Task ValidateConnection()
        {
            if (_client.Connected)
                return;
            try
            {
                await _client.ConnectAsync(_host, _port);
                _commandHandler.AddSource(_client);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private void OnClose(int stream)
        {
            _client.Client.Shutdown(SocketShutdown.Both);
            _client.Client.Close();
        }

        private void OnRoomMessage(ServerRoomMessage command, int streamId, CommandAnswerContext answerContext)
        {
            Console.WriteLine($"{command.Room}:{command.FromUser}: {command.Message}");
        }

        private void OnServiceMessage(ServerServiceMessage command, int streamId, CommandAnswerContext answerContext)
        {
            if (command.RoomNames == null || command.RoomNames.Length < 1)
            {
                Console.WriteLine($"Service message: {command.Message}");
            }
            else
            {
                for (var i = 0; i < command.RoomNames.Length; ++i)
                {
                    var r = command.RoomNames[i];
                    Console.WriteLine($"{r}: Service message: {command.Message}");
                }
            }
        }

        public void Dispose()
        {
            _commandHandler?.Dispose();
            _client?.Dispose();
        }
    }
}
