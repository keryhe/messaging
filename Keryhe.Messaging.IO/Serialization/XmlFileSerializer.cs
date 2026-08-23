using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Keryhe.Messaging.IO.Serialization
{
    public class XmlFileSerializer<T> : IFileSerializer<T>
    {
        // XmlSerializer has no asynchronous API, so it runs against a MemoryStream and only the
        // file I/O — the part that actually blocks — is awaited. Both methods were previously
        // synchronous behind an async signature, blocking the caller's thread on disk.
        public async Task<T> DeserializeAsync(string path)
        {
            using MemoryStream buffer = new MemoryStream();

            using (FileStream fs = File.OpenRead(path))
            {
                await fs.CopyToAsync(buffer);
            }

            buffer.Position = 0;

            XmlSerializer xs = new XmlSerializer(typeof(T));
            return (T)xs.Deserialize(buffer);
        }

        public async Task SerializeAsync(T src, string path)
        {
            using MemoryStream buffer = new MemoryStream();

            XmlSerializer xs = new XmlSerializer(typeof(T));
            xs.Serialize(buffer, src);

            buffer.Position = 0;

            using FileStream fs = File.Create(path);
            await buffer.CopyToAsync(fs);
        }
    }
}
