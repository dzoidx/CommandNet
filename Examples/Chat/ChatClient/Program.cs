using System;
using ChatShared;

namespace ChatClient
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Client started");
            var client = new ChatClient("localhost", 8500, new JsonSerializer());
            Console.WriteLine("Press any key");
            Console.ReadKey();
        }
    }
}
