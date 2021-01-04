using System;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace CommandNet
{
    public static class BitHelper
    {
        public static int ReadIntFromArray(byte[] buff, int offset)
        {
            var result = (int)buff[offset];
            result += buff[offset + 1] << 8;
            result += buff[offset + 2] << 16;
            result += buff[offset + 3] << 24;
            return result;
        }

        public static long ReadLongFromArray(byte[] buff, int offset)
        {
            var lo = ReadIntFromArray(buff, offset);
            var hi = ReadIntFromArray(buff, offset + 4);
            return lo + ((long)hi << 32);
        }

        public static string ReadStringFromArray(byte[] buff, int offset)
        {
            var sz = ReadIntFromArray(buff, offset);
            var bytes = new byte[sz];
            Array.Copy(buff, offset + 4, bytes, 0, sz);
            return Encoding.UTF8.GetString(bytes);
        }

        public static void WriteIntToArray(byte[] buff, int offset, int value)
        {
            buff[offset + 0] = (byte)value;
            buff[offset + 1] = (byte)(value >> 8);
            buff[offset + 2] = (byte)(value >> 16);
            buff[offset + 3] = (byte)(value >> 24);
        }

        public static void WriteLongToArray(byte[] buff, int offset, long value)
        {
            WriteIntToArray(buff, offset, (int)value);
            WriteIntToArray(buff, offset + 4, (int)(value >> 32));
        }

        public static int WriteStringToArray(byte[] buff, int offset, string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            var sz = bytes.Length;
            var needBytes = buff.Length - offset - sz - 4;
            if (needBytes < 0)
            {
                return needBytes;
            }
            WriteIntToArray(buff, offset, sz);
            bytes.CopyTo(buff, offset + 4);
            return offset + sz + 4;
        }

        public static int SimpleHash(string value)
        {
            uint hash = 5381;

            for (var i = 0; i < value.Length; ++i)
            {
                hash = ((hash << 5) + hash) + value[i];
            }

            return (int) hash;
        }

    }
}
