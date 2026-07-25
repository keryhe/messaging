using Keryhe.Messaging.IO.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Keryhe.Messaging.IO
{
    public class FileSystemPublisher<T> : IMessagePublisher<T>
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.IO");
        private readonly FileSystemPublisherOptions _options;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, bool> _ensured = new();

        public FileSystemPublisher(FileSystemPublisherOptions options, IServiceProvider serviceProvider)
        {
            _options = options;
            _serviceProvider = serviceProvider;
        }

        public FileSystemPublisher(IOptions<FileSystemPublisherOptions> options, IServiceProvider serviceProvider)
            : this(options.Value, serviceProvider)
        {
        }

        public async Task SendAsync(T message, string destination)
        {
            FileSystemDestinationOptions destinationOptions = Resolve(destination);

            if (_ensured.TryAdd(destination, true) && !Directory.Exists(destinationOptions.Folder))
            {
                Directory.CreateDirectory(destinationOptions.Folder);
            }

            IFileSerializer<T> serializer = _serviceProvider.GetRequiredKeyedService<IFileSerializer<T>>(destinationOptions.FileType);

            string filename = Guid.NewGuid().ToString();
            string path = Path.Combine(destinationOptions.Folder, filename + "." + destinationOptions.FileType);

            using var activity = _activitySource.StartActivity($"send {destinationOptions.Folder}", ActivityKind.Producer);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "file");
                activity.SetTag("messaging.operation.name", "send");
                activity.SetTag("messaging.operation.type", "send");
                activity.SetTag("messaging.destination.name", destinationOptions.Folder);
                activity.SetTag("messaging.message.id", filename);
            }

            await serializer.SerializeAsync(message, path);

            activity?.SetTag("messaging.message.body.size", new FileInfo(path).Length);
            await WriteTraceSidecarAsync(path, activity);
        }

        private static async Task WriteTraceSidecarAsync(string path, Activity activity)
        {
            if (activity == null)
                return;

            var sidecar = new FileSystemTraceSidecar
            {
                TraceParent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}",
                TraceState = string.IsNullOrEmpty(activity.TraceStateString) ? null : activity.TraceStateString
            };

            using FileStream fs = File.Create(path + ".trace");
            await JsonSerializer.SerializeAsync(fs, sidecar);
        }

        private FileSystemDestinationOptions Resolve(string destination)
        {
            if (_options.Destinations == null || !_options.Destinations.TryGetValue(destination, out FileSystemDestinationOptions destinationOptions))
            {
                throw new KeyNotFoundException(
                    $"No destination named '{destination}' is configured. Configured destinations: " +
                    string.Join(", ", _options.Destinations?.Keys ?? Enumerable.Empty<string>()));
            }

            return destinationOptions;
        }
    }

    internal class FileSystemTraceSidecar
    {
        public string TraceParent { get; set; }
        public string TraceState { get; set; }
    }
}
