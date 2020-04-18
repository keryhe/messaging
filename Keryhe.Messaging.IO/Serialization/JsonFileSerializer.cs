using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keryhe.Messaging.IO.Serialization
{
    public class JsonFileSerializer<T> : IFileSerializer<T>
    {
        public T Deserialize(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            {
                T result = JsonSerializer.DeserializeAsync<T>(fs).Result;
                return result;
            }
        }

        public void Serialize(T src, string path)
        {
            using (FileStream fs = File.Create(path))
            {
                JsonSerializer.SerializeAsync(fs, src).Wait();
            }
        }
    }
}
