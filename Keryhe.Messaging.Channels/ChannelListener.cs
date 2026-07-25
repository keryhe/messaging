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
        private readonly ChannelListenerOptions _options;
        private readonly IChannelRegistry<T> _registry;
        private readonly ILogger<ChannelListener<T>> _logger;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();

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

        public Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            ChannelOptions shape = Resolve(source);
            Channel<ChannelEnvelope<T>> channel = _registry.GetOrCreate(source, shape);

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellations[source] = linkedCts;

            Task.Run(() => Run(source, channel, messageHandler, linkedCts.Token), linkedCts.Token);

            _logger.LogDebug("ChannelListener started for source {Source}", source);
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(string source, CancellationToken cancellationToken)
        {
            StopSource(source);

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
            foreach (string source in _cancellations.Keys.ToList())
            {
                StopSource(source);
            }

            return ValueTask.CompletedTask;
        }

        private async Task Run(string source, Channel<ChannelEnvelope<T>> channel, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (ChannelEnvelope<T> envelope in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    await ProcessAsync(source, envelope, messageHandler);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ProcessAsync(string source, ChannelEnvelope<T> envelope, Func<T, Task<bool>> messageHandler)
        {
            var parentContext = ExtractTraceContext(envelope);
            using var activity = _activitySource.StartActivity($"process {source}", ActivityKind.Consumer, parentContext);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "in-process");
                activity.SetTag("messaging.operation.name", "process");
                activity.SetTag("messaging.operation.type", "process");
                activity.SetTag("messaging.destination.name", source);
                activity.SetTag("messaging.message.id", envelope.MessageId);
            }

            await messageHandler(envelope.Payload);
        }

        private ActivityContext ExtractTraceContext(ChannelEnvelope<T> envelope)
        {
            if (string.IsNullOrEmpty(envelope?.TraceParent))
                return default;

            var parts = envelope.TraceParent.Split('-');
            if (parts.Length != 4)
                return default;

            var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
            var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
            var traceFlags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

            return new ActivityContext(traceId, spanId, traceFlags, envelope.TraceState);
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
