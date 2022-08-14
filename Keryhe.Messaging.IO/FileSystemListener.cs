using Keryhe.Messaging.IO.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Keryhe.Messaging.IO
{
    public class FileSystemListener<T> : IMessageListener<T>
    {
        private static ManualResetEvent _resetEvent = new ManualResetEvent(false);
        private readonly FileSystemListenerOptions _options;
        private readonly IFileSerializer<T> _serializer;
        private readonly ILogger<FileSystemListener<T>> _logger;
        private Func<T, Task<bool>> _messageHandler;

        private bool _status;

        public FileSystemListener(FileSystemListenerOptions options, IFileSerializer<T> serializer, ILogger<FileSystemListener<T>> logger)
        {
            _options = options;
            _logger = logger;
            _serializer = serializer;
            _status = false;

            if (!Directory.Exists(_options.Folder))
            {
                Directory.CreateDirectory(_options.Folder);
            }
            if (!Directory.Exists(_options.CompletedFolder))
            {
                Directory.CreateDirectory(_options.CompletedFolder);
            }
            if (!Directory.Exists(_options.ErrorFolder))
            {
                Directory.CreateDirectory(_options.ErrorFolder);
            }
        }


        public FileSystemListener(IOptions<FileSystemListenerOptions> options, IFileSerializer<T> serializer, ILogger<FileSystemListener<T>> logger)
            : this(options.Value, serializer, logger)
        {
        }

        public Task SubscribeAsync(Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            _messageHandler = messageHandler;
            _status = true;

            Task.Run(() => Run(), cancellationToken);

            _logger.LogDebug("FileSystemListener Started");
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken)
        {
            _status = false;
            _resetEvent.Set();

            _logger.LogDebug("FileSystemListener Stopped");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _resetEvent.Close();
            return ValueTask.CompletedTask;
        }

        private async Task Run()
        {
            while (_status)
            {
                string[] files = Directory.GetFiles(_options.Folder, "*." + _options.FileType);

                foreach (string file in files)
                {
                    T message = await _serializer.DeserializeAsync(file);
                    bool result = await _messageHandler(message);

                    if (!result)
                    {
                        MoveFile(file, _options.ErrorFolder);
                    }
                    MoveFile(file, _options.CompletedFolder);
                }

                _resetEvent.WaitOne(TimeSpan.FromSeconds(_options.Interval));
            }
        }

        private void MoveFile(string file, string destination)
        {
            if (string.IsNullOrEmpty(_options.CompletedFolder))
            {
                File.Delete(file);
            }
            else
            {
                string subFolder = DateTime.Now.ToString("yyyy-MM-dd");
                string path = Path.Combine(_options.CompletedFolder, subFolder);
                string fileName = Path.GetFileName(file);
                Directory.CreateDirectory(path);
                File.Move(file, Path.Combine(path, fileName));
            }
        }
    }
}
