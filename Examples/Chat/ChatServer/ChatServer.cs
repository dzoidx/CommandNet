using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using CommandNet;
using CommandNet.Serializer;
using ChatShared.Commands;

namespace ChatServer
{
    class ChatServer
    {
        private CommandHandler _commandHandler;
        private TcpListener _server;

        public ChatServer(int port, ICommandSerializer serializer)
        {
            _server = new TcpListener(IPAddress.Any, port);
            _commandHandler = new CommandHandler(serializer);
            _server.Start();
            FetchConnection();
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
                _commandHandler.Notify(new ServerServiceMessage() { Message = "Hello!" }, streamId);
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
    }
}
