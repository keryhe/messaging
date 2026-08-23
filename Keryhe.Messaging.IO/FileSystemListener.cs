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
        private readonly IOptionsMonitor<FileSystemListenerOptions> _optionsMonitor;
        private readonly IDisposable _changeToken;

        private FileSystemListenerOptions _options;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FileSystemListener<T>> _logger;
        private readonly SemaphoreSlim _optionsLock = new(1, 1);
        private readonly ConcurrentDictionary<string, bool> _ensured = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();
        private readonly ConcurrentDictionary<string, (Func<T, Task<bool>> Handler, CancellationToken CancellationToken)> _handlers = new();
        private int _disposed;

        // Serializes subscribe against unsubscribe. Stopping the previous loop first only avoids a
        // leak if no second subscribe can interleave between the stop and the dictionary write.
        private readonly object _subscribeGate = new();

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

        public FileSystemListener(IOptionsMonitor<FileSystemListenerOptions> options, IServiceProvider serviceProvider, ILogger<FileSystemListener<T>> logger)
            : this(options.CurrentValue, serviceProvider, logger)
        {
            _optionsMonitor = options;

            _changeToken = _optionsMonitor.OnChange(updated =>
            {
                _ = ResetAsync(updated);
            });
        }

        public async Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            await _optionsLock.WaitAsync();
            FileSystemListenerSourceOptions sourceOptions;
            try
            {
                sourceOptions = Resolve(source);

                if (!_ensured.ContainsKey(source))
                {
                    EnsureFolder(sourceOptions.Folder);
                    EnsureFolder(sourceOptions.CompletedFolder);
                    EnsureFolder(sourceOptions.ErrorFolder);

                    // Mark only once the folders exist. Marking first means a failed creation is
                    // never retried, and every later subscribe skips it and fails further on.
                    _ensured[source] = true;
                }
            }
            finally
            {
                _optionsLock.Release();
            }

            lock (_subscribeGate)
            {
                // Subscribing a source twice would otherwise leave the first loop running and
                // unreachable — both scanning the same folder, so the same file is handled twice.
                StopSource(source);

                _handlers[source] = (messageHandler, cancellationToken);

                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _cancellations[source] = linkedCts;

                _ = Task.Run(() => Run(source, sourceOptions, messageHandler, linkedCts.Token), linkedCts.Token)
                    .ContinueWith(
                        t => _logger.LogError(t.Exception, "FileSystemListener poll loop for source {Source} faulted and has stopped", source),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
            }

            _logger.LogDebug("FileSystemListener started for source {Source}", source);
        }

        public Task UnsubscribeAsync(string source, CancellationToken cancellationToken)
        {
            lock (_subscribeGate)
            {
                StopSource(source);
            }

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

            _handlers.TryRemove(source, out _);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return ValueTask.CompletedTask;
            }

            _changeToken?.Dispose();

            lock (_subscribeGate)
            {
                foreach (string source in _cancellations.Keys.ToList())
                {
                    StopSource(source);
                }
            }

            _optionsLock.Dispose();

            return ValueTask.CompletedTask;
        }

        private async Task ResetAsync(FileSystemListenerOptions updated)
        {
            // OnChange fires the callback fire-and-forget; a change racing DisposeAsync would
            // otherwise operate on a lock disposal is concurrently tearing down.
            if (Volatile.Read(ref _disposed) == 1)
            {
                return;
            }

            try
            {
                List<(string Source, Func<T, Task<bool>> MessageHandler, CancellationToken CancellationToken)> existingSources;

                await _optionsLock.WaitAsync();
                try
                {
                    Interlocked.Exchange(ref _options, updated);
                    _ensured.Clear();

                    existingSources = _handlers
                        .Select(kvp => (kvp.Key, kvp.Value.Handler, kvp.Value.CancellationToken))
                        .ToList();
                }
                finally
                {
                    _optionsLock.Release();
                }

                lock (_subscribeGate)
                {
                    foreach (var entry in existingSources)
                    {
                        StopSource(entry.Source);
                    }
                }

                foreach (var entry in existingSources)
                {
                    try
                    {
                        // Carry the caller's original token across, or cancelling it would stop
                        // having any effect after the first options change.
                        await SubscribeAsync(entry.Source, entry.MessageHandler, entry.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to resubscribe source {Source} after options change", entry.Source);
                    }
                }

                _logger.LogInformation("FileSystemListener reset options due to options change");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset FileSystemListener after options change");
            }
        }

        private async Task Run(string source, FileSystemListenerSourceOptions sourceOptions, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            IFileSerializer<T> serializer = _serviceProvider.GetRequiredKeyedService<IFileSerializer<T>>(sourceOptions.FileType);

            while (!cancellationToken.IsCancellationRequested)
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(sourceOptions.Folder, "*." + sourceOptions.FileType);
                }
                catch (Exception ex)
                {
                    // A folder that is deleted, unmounted or briefly unreadable would otherwise end
                    // the loop for the life of the process. Report it and try again next interval.
                    _logger.LogError(ex, "FileSystemListener could not read folder {Folder} for source {Source}; retrying", sourceOptions.Folder, source);
                    files = Array.Empty<string>();
                }

                foreach (string file in files)
                {
                    bool result;
                    try
                    {
                        result = await ProcessFileAsync(sourceOptions, messageHandler, serializer, file);
                    }
                    catch (Exception ex)
                    {
                        // A malformed file would otherwise kill the loop and do so again on every
                        // restart, since the file stays in the folder. Move it aside and carry on.
                        _logger.LogError(ex, "FileSystemListener failed to process file {File} for source {Source}", file, source);
                        result = false;
                    }

                    try
                    {
                        MoveFile(file, result ? sourceOptions.CompletedFolder : sourceOptions.ErrorFolder);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "FileSystemListener failed to move file {File} for source {Source}", file, source);
                    }
                }

                try
                {
                    // Floor at one second: an explicit "Interval": 0 in configuration would
                    // otherwise still make this a no-op and turn the scan loop into a spin.
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, sourceOptions.Interval)), cancellationToken);
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
            ActivityContext parentContext = default;
            if (File.Exists(sidecarPath))
            {
                try
                {
                    parentContext = await ReadTraceSidecarAsync(sidecarPath);
                }
                catch (Exception ex)
                {
                    // A corrupt or half-written sidecar is a telemetry problem, not a reason to
                    // send a perfectly good payload to the error folder.
                    _logger.LogWarning(ex, "FileSystemListener could not read the trace sidecar {Sidecar}; processing the file without a parent trace context", sidecarPath);
                }
            }

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

            try
            {
                T message = await serializer.DeserializeAsync(file);

                // An empty or "null" file is not a message. Handing null to the handler pushes the
                // failure into caller code; treat it as a deserialization failure instead, so the
                // file lands in ErrorFolder like any other bad file.
                if (message == null)
                {
                    throw new InvalidOperationException($"File '{file}' deserialized to null.");
                }

                return await messageHandler(message);
            }
            finally
            {
                // The payload file is moved out of the folder either way, so the sidecar has to go
                // with it — otherwise a failed file leaves an orphan behind on every pass.
                if (File.Exists(sidecarPath))
                {
                    File.Delete(sidecarPath);
                }
            }
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

            try
            {
                var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
                var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
                var traceFlags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

                return new ActivityContext(traceId, spanId, traceFlags, sidecar.TraceState);
            }
            catch (ArgumentOutOfRangeException)
            {
                return default;
            }
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
                return;
            }

            string subFolder = DateTime.Now.ToString("yyyy-MM-dd");
            string path = Path.Combine(destination, subFolder);
            string fileName = Path.GetFileName(file);
            Directory.CreateDirectory(path);

            string target = Path.Combine(path, fileName);

            // A name already taken in the destination would make File.Move throw on every pass,
            // leaving the file in the source folder to be handled again and again — the listener
            // would livelock, re-invoking the handler forever. Producers that reuse file names
            // (anything other than this package's publisher) hit this routinely.
            if (File.Exists(target))
            {
                string unique = $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():n}{Path.GetExtension(fileName)}";
                target = Path.Combine(path, unique);

                _logger.LogWarning("FileSystemListener found {FileName} already present in {Folder}; moving it as {Unique} instead", fileName, path, unique);
            }

            File.Move(file, target);
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
