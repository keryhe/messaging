using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace Keryhe.Messaging.IO.Serialization
{
    public class XmlFileSerializer<T> : IFileSerializer<T>
    {
        public T Deserialize(TextReader inputStream)
        {
            XmlSerializer xs = new XmlSerializer(typeof(T));
            T result = (T)xs.Deserialize(inputStream);

            return result;
        }

        public void Serialize(T src, TextWriter outputStream)
        {
            XmlSerializer xs = new XmlSerializer(typeof(T));
            xs.Serialize(outputStream, src);
        }
    }
}
