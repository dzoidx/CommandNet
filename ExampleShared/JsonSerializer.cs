using System;
using System.Text;
using CommandNet;
using CommandNet.Serializer;
using Newtonsoft.Json;

namespace ExampleShared
{
    public class JsonSerializer : ICommandSerializer
    {
        private JsonSerializerSettings _setttings;
        public JsonSerializer()
        {
            _setttings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
        }

        public Command Deserialize(byte[] data)
        {
            var dataStr = Encoding.UTF8.GetString(data);
            return JsonConvert.DeserializeObject<Command>(dataStr, _setttings);
        }

        public byte[] Serialize(Command o)
        {
            var json = JsonConvert.SerializeObject(o, _setttings);
            return Encoding.UTF8.GetBytes(json);
        }
    }
}
