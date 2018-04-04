using System;
using System.Linq;
using System.IO;
using ChatShared;
using System.Reflection;
using log4net;
using log4net.Config;

namespace ChatServer
{
    class Program
    {
        private static ILog Logger = LogManager.GetLogger(typeof(Program));
        private static ChatServer server;

        static void ProcessCommand(string command, string[] args)
        {
            switch (command)
            {
                case "stat":
                    var stat = server.Stat;
                    Console.WriteLine($"CommandsSend: {stat.CommandsSend}");
                    Console.WriteLine($"CommandsReceived: {stat.CommandsReceived}");
                    Console.WriteLine($"BytesSend: {stat.BytesSend}");
                    Console.WriteLine($"BytesReceived: {stat.BytesReceived}");
                    break;
            }
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

            Console.WriteLine("Serving at 8500");
            server = new ChatServer(8500, new JsonSerializer());

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
            Console.WriteLine("Press any key");
            Console.ReadKey();
        }
    }
}
