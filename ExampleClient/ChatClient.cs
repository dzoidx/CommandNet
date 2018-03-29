﻿using System;
using System.Collections.Generic;
using System.Text;
using CommandNet;
using CommandNet.Serializer;
using ChatShared.Commands;
using System.Net.Sockets;

namespace ChatClient
{
    public class ChatClient
    {
        private CommandHandler _commandHandler;
        private TcpClient _client;
        private string _host;
        private int _port;

        public bool IsConnected { get { return _client.Connected; } }

        internal ChatClient(string host, int port, ICommandSerializer serializer)
        {
            _client = new TcpClient();
            _commandHandler = new CommandHandler(serializer);
            _commandHandler.RegisterHandler<ServerRoomMessage>(OnRoomMessage);
            _commandHandler.RegisterHandler<ServerServiceMessage>(OnServiceMessage);
            _commandHandler.OnClose += OnClose;
        }

        public RequestContext<ServerLoginResponse> Login(string userName)
        {
            return _commandHandler.Request<ClientLoginRequest, ServerLoginResponse>(new ClientLoginRequest { UserName = userName }, 1);
        }

        private void OnConnect(IAsyncResult ar)
        {
            var c = (TcpClient)ar.AsyncState;
            if (!ReferenceEquals(c, _client))
                return;
            c.EndConnect(ar);
        }

        private void ValidateConnection()
        {
            if (_client.Connected)
                return;
            _client.BeginConnect(_host, _port, OnConnect, _client);
        }

        private void OnClose(int stream)
        {
            _client.Client.Shutdown(SocketShutdown.Both);
            _client.Client.Close();
        }

        private void OnRoomMessage(ServerRoomMessage command, int streamId, CommandAnswerContext answerContext)
        {
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
    }
}
