using System;
using System.Collections.Generic;
using System.Text;

namespace CommandNet
{
    public class MemoryBlock
    {
        public byte[] Data;
        public int DataSize;
        public int BlockSize;
    }

    public struct Command
    {
        public int ModuleId;
        public int CommandId;
        public MemoryBlock Data;

        public Command(int moduleId, int commandId, int dataSize)
        {
            ModuleId = moduleId;
            CommandId = commandId;
            Data = BufferManager.Get(dataSize);
        }
    }

    public class ChatModule
    {
        public static readonly string Name = nameof(ChatModule);
        
        public static class Proto
        {
            public static readonly int ChatMessage = 1;
        }
    }

    public class ChatMessage
    {
        public string ChannelId;
        public string Message;
        public byte[] CustomBytes;
    }

    public class ChatClient
    {
        public void SendMessage(string message, string channel)
        {
            CommandPool.Get(moduleId_, ChatModule.Proto.ChatMessage, )
        }

        static ChatClient()
        {
            moduleId_ = BitHelper.SimpleHash(ChatModule.Name);
        }

        private static  readonly int moduleId_;
    }

    public class CommandPool
    {
        public static Command Get(int moduleId, int commandId, int dataSize)
        {
        }
    }

    public static class BufferManager
    {
        public static MemoryBlock Get(int size)
        {
            System.Diagnostics.Debug.Assert(size > 0);
            var key = AlignSize(size);
            lock (_pool)
            {
                if (_pool.TryGetValue(key, out var queue) && queue.TryDequeue(out var arr))
                {
                    arr.DataSize = size;
                    return arr;
                }
            }

            return new MemoryBlock() {BlockSize = key, Data = new byte[key], DataSize = size};
        }

        public static void Put(MemoryBlock memoryBlock)
        {
            var key = memoryBlock.BlockSize;
            lock (_pool)
            {
                if (_pool.TryGetValue(key, out var queue))
                {
                    queue.Enqueue(memoryBlock);
                }
                else
                {
                    _pool[key] = queue = new Queue<MemoryBlock>();
                    queue.Enqueue(memoryBlock);
                }
            }
        }

        private static int AlignSize(int size)
        {
            var orig = size;
            var result = 1;
            while (size > 0)
            {
                if (orig == result)
                    return result;
                size >>= 1;
                result <<= 1;
            }
            return result;
        }

        private static Dictionary<int, Queue<MemoryBlock>> _pool = new Dictionary<int, Queue<MemoryBlock>>();
    }
}
