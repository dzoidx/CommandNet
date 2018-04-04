using System;
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
            var server = new ChatServer(8500, new JsonSerializer());
            Console.WriteLine("Press any key");
            Console.ReadKey();
        }
    }
}
