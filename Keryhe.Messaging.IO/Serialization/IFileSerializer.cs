using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Keryhe.Messaging.IO.Serialization
{
    public interface IFileSerializer<T>
    {
        Task SerializeAsync(T src, string path);

        Task<T> DeserializeAsync(string path);
    }
}
