using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Keryhe.Messaging.IO.Serialization
{
    public class JsonFileSerializer<T> : IFileSerializer<T>
    {
        public async Task<T> DeserializeAsync(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            {
                T result = await JsonSerializer.DeserializeAsync<T>(fs);
                return result;
            }
        }

        public async Task SerializeAsync(T src, string path)
        {
            using (FileStream fs = File.Create(path))
            {
                await JsonSerializer.SerializeAsync(fs, src);
            }
        }
    }
}
