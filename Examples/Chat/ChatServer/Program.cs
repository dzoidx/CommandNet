using System;
using ChatShared;

namespace ChatServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Serving at 8500");
            var server = new ChatServer(8500, new JsonSerializer());
            Console.WriteLine("Press any key");
            Console.ReadKey();
        }
    }
}
