using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Keryhe.Messaging.IO.Serialization
{
    public class JsonFileSerializer<T> : IFileSerializer<T>
    {
        public T Deserialize(TextReader inputStream)
        {
            JsonSerializer js = new JsonSerializer();
            T result = (T)js.Deserialize(inputStream, typeof(T));

            return result;
        }

        public void Serialize(T src, TextWriter outputStream)
        {
            JsonSerializer js = new JsonSerializer();
            js.Serialize(outputStream, src);
        }
    }
}
