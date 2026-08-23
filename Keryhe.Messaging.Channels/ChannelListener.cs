using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Keryhe.Messaging.Channels
{
    public class ChannelListener<T> : IMessageListener<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.Channels");
        private readonly IOptionsMonitor<ChannelListenerOptions> _optionsMonitor;
        private readonly IDisposable _changeToken;

        private ChannelListenerOptions _options;
        private readonly IChannelRegistry<T> _registry;
        private readonly ILogger<ChannelListener<T>> _logger;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();

        // Serializes subscribe against unsubscribe. Stopping the previous reader first only avoids
        // a leak if no second subscribe can interleave between the stop and the dictionary write.
        private readonly object _subscribeGate = new();

        public ChannelListener(ChannelListenerOptions options, IServiceProvider serviceProvider, ILogger<ChannelListener<T>> logger)
        {
            _options = options;
            _registry = serviceProvider.GetRequiredService<IChannelRegistry<T>>();
            _logger = logger;
        }

        public ChannelListener(IOptions<ChannelListenerOptions> options, IServiceProvider serviceProvider, ILogger<ChannelListener<T>> logger)
            : this(options.Value, serviceProvider, logger)
        {
        }

        public ChannelListener(IOptionsMonitor<ChannelListenerOptions> options, IServiceProvider serviceProvider, ILogger<ChannelListener<T>> logger)
            : this(options.CurrentValue, serviceProvider, logger)
        {
            _optionsMonitor = options;

            // Channels are in-process, singleton-registry-backed resources with no "reconnect"
            // concept, so OnChange only swaps _options for sources declared after the change —
            // existing channels/consumers are intentionally left untouched (see ChannelRegistry).
            _changeToken = _optionsMonitor.OnChange(updated =>
            {
                Interlocked.Exchange(ref _options, updated);
            });
        }

        public Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            ChannelOptions shape = Resolve(source);
            Channel<ChannelEnvelope<T>> channel = _registry.GetOrCreate(source, shape);

            lock (_subscribeGate)
            {
                // Subscribing a source twice would otherwise leave the first reader running and
                // unreachable, competing with the second for the same channel.
                StopSource(source);

                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _cancellations[source] = linkedCts;

                Task.Run(() => Run(source, channel, messageHandler, linkedCts.Token), linkedCts.Token)
                    .ContinueWith(
                        t => _logger.LogError(t.Exception, "ChannelListener read loop for source {Source} faulted and has stopped", source),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
            }

            _logger.LogDebug("ChannelListener started for source {Source}", source);
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(string source, CancellationToken cancellationToken)
        {
            lock (_subscribeGate)
            {
                StopSource(source);
            }

            _logger.LogDebug("ChannelListener stopped for source {Source}", source);
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
            _changeToken?.Dispose();

            lock (_subscribeGate)
            {
                foreach (string source in _cancellations.Keys.ToList())
                {
                    StopSource(source);
                }
            }

            return ValueTask.CompletedTask;
        }

        private async Task Run(string source, Channel<ChannelEnvelope<T>> channel, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (ChannelEnvelope<T> envelope in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        await ProcessAsync(source, envelope, messageHandler);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // One throwing handler must not end consumption for the process lifetime.
                        _logger.LogError(ex, "ChannelListener handler threw for message {MessageId} on source {Source}", envelope?.MessageId, source);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ProcessAsync(string source, ChannelEnvelope<T> envelope, Func<T, Task<bool>> messageHandler)
        {
            var parentContext = ExtractTraceContext(envelope, source);
            using var activity = _activitySource.StartActivity($"process {source}", ActivityKind.Consumer, parentContext);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "in-process");
                activity.SetTag("messaging.operation.name", "process");
                activity.SetTag("messaging.operation.type", "process");
                activity.SetTag("messaging.destination.name", source);
                activity.SetTag("messaging.message.id", envelope.MessageId);
            }

            bool success = await messageHandler(envelope.Payload);

            if (!success)
            {
                // An in-process channel has no redelivery mechanism, so there is nothing to retry
                // against — but the signal must not vanish silently.
                _logger.LogWarning("ChannelListener handler returned false for message {MessageId} on source {Source}; the message is dropped, as channels have no redelivery",
                    envelope.MessageId, source);
            }
        }

        private ActivityContext ExtractTraceContext(ChannelEnvelope<T> envelope, string source)
        {
            if (string.IsNullOrEmpty(envelope?.TraceParent))
                return default;

            var parts = envelope.TraceParent.Split('-');
            if (parts.Length != 4)
                return default;

            try
            {
                var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
                var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
                var traceFlags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

                return new ActivityContext(traceId, spanId, traceFlags, envelope.TraceState);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Unreachable while ChannelPublisher is the only writer, but the cost of being
                // wrong about that is a message lost to a telemetry detail.
                _logger.LogWarning("ChannelListener could not parse the trace parent '{TraceParent}' on source {Source}; processing the message without a parent trace context", envelope.TraceParent, source);
                return default;
            }
        }

        private ChannelOptions Resolve(string source)
        {
            if (_options.Sources == null || !_options.Sources.TryGetValue(source, out ChannelOptions shape))
            {
                throw new KeyNotFoundException(
                    $"No source named '{source}' is configured. Configured sources: " +
                    string.Join(", ", _options.Sources?.Keys ?? Enumerable.Empty<string>()));
            }

            return shape;
        }
    }
}
