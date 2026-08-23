using Keryhe.Messaging.Polling;
using Keryhe.Messaging.Polling.Delay;
using Microsoft.Extensions.Options;

namespace MessagingTester
{
    public class FileSystemPoller : Poller<List<string>>
    {
        private readonly FileSystemPollerOptions _options;

        public FileSystemPoller(FileSystemPollerOptions options, IDelay delay, ILogger<Poller<List<string>>> logger)
            : base(delay, logger)
        {
            _options = options;

            if (!Directory.Exists(_options.Folder))
            {
                Directory.CreateDirectory(_options.Folder);
            }
        }

        public FileSystemPoller(IOptions<FileSystemPollerOptions> options, IDelay delay, ILogger<Poller<List<string>>> logger)
            : this(options.Value, delay, logger)
        {
        }

        protected override Task<List<string>> Poll()
        {
            string[] files = Directory.GetFiles(_options.Folder, "*." + _options.FileType);

            // An empty list is fine to return: Poller<T>.CheckNullOrEmpty treats an empty
            // collection as "nothing to do" and waits on the delay.
            return Task.FromResult(files.ToList());
        }
    }

    public class FileSystemPollerOptions
    {
        public string Folder { get; set; }
        public string FileType { get; set; }
    }
}
