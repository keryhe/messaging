using Keryhe.Messaging.IO.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;

namespace Keryhe.Messaging.IO
{
    public class FileSystemListener<T> : IMessageListener<T>
    {
        private static ManualResetEvent _resetEvent = new ManualResetEvent(false);
        private readonly FileSystemListenerOptions _options;
        private readonly IFileSerializer<T> _serializer;
        private readonly ILogger<FileSystemListener<T>> _logger;
        private Action<T> _callback;


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
        }


        public FileSystemListener(IOptions<FileSystemListenerOptions> options, IFileSerializer<T> serializer, ILogger<FileSystemListener<T>> logger)
            : this(options.Value, serializer, logger)
        {
        }

        public void Start(Action<T> callback)
        {
            _logger.LogDebug("FileSystemListener Started");
            _callback = callback;
            _status = true;

            Thread t = new Thread(new ThreadStart(Run));
            t.Start();
        }

        public void Stop()
        {
            _status = false;
            _resetEvent.Set();
            _logger.LogDebug("FileSystemListener Stopped");
        }

        public void Dispose()
        {
            _resetEvent.Close();
        }

        private void Run()
        {
            while (_status)
            {
                string[] files = Directory.GetFiles(_options.Folder, "*." + _options.FileType);

                foreach (string file in files)
                {
                    T message;

                    using (StreamReader reader = new StreamReader(file))
                    {
                        message = _serializer.Deserialize(reader);
                    }

                    _callback(message);

                    CleanupFile(file);
                }

                _resetEvent.WaitOne(TimeSpan.FromSeconds(_options.Interval));
            }
        }

        private void CleanupFile(string file)
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
