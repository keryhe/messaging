using Keryhe.Messaging.IO.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    public class FileSystemListener<T> : IMessageListener<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.IO");
        private readonly FileSystemListenerOptions _options;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FileSystemListener<T>> _logger;
        private readonly ConcurrentDictionary<string, bool> _ensured = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();

        public FileSystemListener(FileSystemListenerOptions options, IServiceProvider serviceProvider, ILogger<FileSystemListener<T>> logger)
        {
            _options = options;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public FileSystemListener(IOptions<FileSystemListenerOptions> options, IServiceProvider serviceProvider, ILogger<FileSystemListener<T>> logger)
            : this(options.Value, serviceProvider, logger)
        {
        }

        public Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            FileSystemListenerSourceOptions sourceOptions = Resolve(source);

            if (_ensured.TryAdd(source, true))
            {
                EnsureFolder(sourceOptions.Folder);
                EnsureFolder(sourceOptions.CompletedFolder);
                EnsureFolder(sourceOptions.ErrorFolder);
            }

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellations[source] = linkedCts;

            Task.Run(() => Run(source, sourceOptions, messageHandler, linkedCts.Token), linkedCts.Token);

            _logger.LogDebug("FileSystemListener started for source {Source}", source);
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(string source, CancellationToken cancellationToken)
        {
            StopSource(source);

            _logger.LogDebug("FileSystemListener stopped for source {Source}", source);
            return Task.CompletedTask;
        }

        private void StopSource(string source)
        {
            if (_cancellations.TryRemove(source, out CancellationTokenSource cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        public ValueTask DisposeAsync()
        {
            foreach (string source in _cancellations.Keys.ToList())
            {
                StopSource(source);
            }

            return ValueTask.CompletedTask;
        }

        private async Task Run(string source, FileSystemListenerSourceOptions sourceOptions, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            IFileSerializer<T> serializer = _serviceProvider.GetRequiredKeyedService<IFileSerializer<T>>(sourceOptions.FileType);

            while (!cancellationToken.IsCancellationRequested)
            {
                string[] files = Directory.GetFiles(sourceOptions.Folder, "*." + sourceOptions.FileType);

                foreach (string file in files)
                {
                    bool result = await ProcessFileAsync(sourceOptions, messageHandler, serializer, file);

                    if (!result)
                    {
                        MoveFile(file, sourceOptions.ErrorFolder);
                    }
                    MoveFile(file, sourceOptions.CompletedFolder);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(sourceOptions.Interval), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<bool> ProcessFileAsync(FileSystemListenerSourceOptions sourceOptions, Func<T, Task<bool>> messageHandler, IFileSerializer<T> serializer, string file)
        {
            string sidecarPath = file + ".trace";
            ActivityContext parentContext = File.Exists(sidecarPath)
                ? await ReadTraceSidecarAsync(sidecarPath)
                : default;

            using var activity = _activitySource.StartActivity($"process {sourceOptions.Folder}", ActivityKind.Consumer, parentContext);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "file");
                activity.SetTag("messaging.operation.name", "process");
                activity.SetTag("messaging.operation.type", "process");
                activity.SetTag("messaging.destination.name", sourceOptions.Folder);
                activity.SetTag("messaging.message.body.size", new FileInfo(file).Length);
                activity.SetTag("messaging.message.id", Path.GetFileNameWithoutExtension(file));
            }

            T message = await serializer.DeserializeAsync(file);
            bool result = await messageHandler(message);

            if (File.Exists(sidecarPath))
            {
                File.Delete(sidecarPath);
            }

            return result;
        }

        private static async Task<ActivityContext> ReadTraceSidecarAsync(string sidecarPath)
        {
            using FileStream fs = File.OpenRead(sidecarPath);
            FileSystemTraceSidecar sidecar = await JsonSerializer.DeserializeAsync<FileSystemTraceSidecar>(fs);

            if (string.IsNullOrEmpty(sidecar?.TraceParent))
                return default;

            var parts = sidecar.TraceParent.Split('-');
            if (parts.Length != 4)
                return default;

            var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
            var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
            var traceFlags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

            return new ActivityContext(traceId, spanId, traceFlags, sidecar.TraceState);
        }

        private void EnsureFolder(string folder)
        {
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        private void MoveFile(string file, string destination)
        {
            if (string.IsNullOrEmpty(destination))
            {
                File.Delete(file);
            }
            else
            {
                string subFolder = DateTime.Now.ToString("yyyy-MM-dd");
                string path = Path.Combine(destination, subFolder);
                string fileName = Path.GetFileName(file);
                Directory.CreateDirectory(path);
                File.Move(file, Path.Combine(path, fileName));
            }
        }

        private FileSystemListenerSourceOptions Resolve(string source)
        {
            if (_options.Sources == null || !_options.Sources.TryGetValue(source, out FileSystemListenerSourceOptions sourceOptions))
            {
                throw new KeyNotFoundException(
                    $"No source named '{source}' is configured. Configured sources: " +
                    string.Join(", ", _options.Sources?.Keys ?? Enumerable.Empty<string>()));
            }

            return sourceOptions;
        }
    }
}
