using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Keryhe.Messaging.IO.Serialization
{
    public interface IFileSerializer<T>
    {
        void Serialize(T src, TextWriter outputStream);

        T Deserialize(TextReader inputStream);
    }
}
