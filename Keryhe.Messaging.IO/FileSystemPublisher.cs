using Keryhe.Messaging.IO.Serialization;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Keryhe.Messaging.IO
{
    public class FileSystemPublisher<T> : IMessagePublisher<T>
    {
        private readonly FileSystemListenerOptions _options;
        private readonly IFileSerializer<T> _serializer;

        public FileSystemPublisher(FileSystemListenerOptions options, IFileSerializer<T> serializer)
        {
            _options = options;
            _serializer = serializer;
        }

        public FileSystemPublisher(IOptions<FileSystemListenerOptions> options, IFileSerializer<T> serializer)
            : this(options.Value, serializer)
        {
        }

        public async Task SendAsync(T message)
        {
            string dir = _options.Folder;
            string filename = Guid.NewGuid().ToString();
            string ext = "." + _options.FileType;
            string path = Path.Combine(dir, filename, ext);

            await _serializer.SerializeAsync(message, path);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
