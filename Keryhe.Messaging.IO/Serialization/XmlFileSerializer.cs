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
        public Task<T> DeserializeAsync(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            {
                XmlSerializer xs = new XmlSerializer(typeof(T));
                T result = (T)xs.Deserialize(fs);

                return Task.FromResult(result);
            }
        }

        public Task SerializeAsync(T src, string path)
        {
            using (FileStream fs = File.Create(path))
            {
                XmlSerializer xs = new XmlSerializer(typeof(T));
                xs.Serialize(fs, src);

                return Task.CompletedTask;
            }
        }
    }
}
