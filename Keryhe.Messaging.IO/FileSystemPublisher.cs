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
using System.Threading;
using System.Threading.Tasks;

namespace Keryhe.Messaging.IO
{
    public class FileSystemPublisher<T> : IMessagePublisher<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.IO");
        private readonly IOptionsMonitor<FileSystemPublisherOptions> _optionsMonitor;
        private readonly IDisposable _changeToken;

        private FileSystemPublisherOptions _options;
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

        public FileSystemPublisher(IOptionsMonitor<FileSystemPublisherOptions> options, IServiceProvider serviceProvider)
            : this(options.CurrentValue, serviceProvider)
        {
            _optionsMonitor = options;

            _changeToken = _optionsMonitor.OnChange(updated =>
            {
                Interlocked.Exchange(ref _options, updated);
                _ensured.Clear();
            });
        }

        public async Task SendAsync(T message, string destination)
        {
            FileSystemDestinationOptions destinationOptions = Resolve(destination);

            // Checked every send rather than once: the folder can be removed underneath a
            // long-lived publisher, and marking it ensured before the create succeeds means a
            // failure is never retried.
            if (!_ensured.ContainsKey(destination) || !Directory.Exists(destinationOptions.Folder))
            {
                Directory.CreateDirectory(destinationOptions.Folder);
                _ensured[destination] = true;
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

            // The listener globs for "*.{FileType}", so the payload is written under a .tmp name
            // first and stays invisible until complete. A rename is atomic within a volume, so the
            // listener never sees a half-written file.
            // The extension is replaced rather than appended: on Windows a three-character search
            // pattern matches longer extensions too (*.xls matches book.xlsx), so "name.xml.tmp"
            // could still be picked up by a "*.xml" glob.
            string tempPath = Path.Combine(destinationOptions.Folder, filename + ".tmp");

            await serializer.SerializeAsync(message, tempPath);

            activity?.SetTag("messaging.message.body.size", new FileInfo(tempPath).Length);

            // Move the payload in before writing the sidecar, not after: the sidecar is keyed off
            // the payload's final name, so writing it first means a crash between the two steps
            // leaves a sidecar with no payload that will ever arrive — a permanent orphan, since
            // only a processed payload triggers sidecar cleanup. Moving first instead risks the
            // listener picking the file up before the sidecar lands, which only costs a trace link
            // (already an accepted failure elsewhere in this class), not an orphaned file.
            File.Move(tempPath, path);

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

        public ValueTask DisposeAsync()
        {
            _changeToken?.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    internal class FileSystemTraceSidecar
    {
        public string TraceParent { get; set; }
        public string TraceState { get; set; }
    }
}
