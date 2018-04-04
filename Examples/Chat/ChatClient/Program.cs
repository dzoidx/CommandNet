using System;
using System.IO;
using ChatShared;
using ChatShared.Commands;
using System.Linq;
using System.Reflection;
using log4net;
using log4net.Config;

namespace ChatClient
{
    class Program
    {
        private static ILog Logger = LogManager.GetLogger(typeof(Program));
        static ChatClient client;
        static void ProcessCommand(string command, string[] args)
        {
            switch (command)
            {
                case "join_room":
                    var room = args[0];
                    client.JoinRoom(room).OnResult += ProcessResponse;
                    break;
                case "login":
                    var name = args[0];
                    client.Login(name).OnResult += ProcessResponse;
                    break;
                case "send":
                    client.SendMessage(args[0], args[1]).OnResult += ProcessResponse;
                    break;
            }
        }

        static void ProcessResponse(ServerResponseBase resp)
        {
            if (resp.Status != ServerResponseStatus.Error)
                return;
            Console.WriteLine($"Error: " + resp.Description);
        }

        static void Main(string[] args)
        {
            var fi = new FileInfo("log4net.config");
            if (fi.Exists)
            {
                var logRep = LogManager.GetRepository(Assembly.GetCallingAssembly());
                XmlConfigurator.Configure(logRep, fi);
                Logger.InfoFormat("Log config: {0}", fi.FullName);
            }

            Console.WriteLine("Client started");
            client = new ChatClient("localhost", 8500, new JsonSerializer());
            string command;
            do
            {
                command = Console.ReadLine();
                if (command.StartsWith(":"))
                {
                    var parts = command.Substring(1).Split(" ", StringSplitOptions.RemoveEmptyEntries);
                    ProcessCommand(parts[0], parts.Skip(1).ToArray());
                }
            } while (command != ":exit");
            Console.ReadKey();
        }
    }
}
