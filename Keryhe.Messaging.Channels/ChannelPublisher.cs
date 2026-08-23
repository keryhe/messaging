using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Keryhe.Messaging.Channels
{
    public class ChannelPublisher<T> : IMessagePublisher<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.Channels");
        private readonly IOptionsMonitor<ChannelPublisherOptions> _optionsMonitor;
        private readonly IDisposable _changeToken;

        private ChannelPublisherOptions _options;
        private readonly IChannelRegistry<T> _registry;

        public ChannelPublisher(ChannelPublisherOptions options, IServiceProvider serviceProvider)
        {
            _options = options;
            _registry = serviceProvider.GetRequiredService<IChannelRegistry<T>>();
        }

        public ChannelPublisher(IOptions<ChannelPublisherOptions> options, IServiceProvider serviceProvider)
            : this(options.Value, serviceProvider)
        {
        }

        public ChannelPublisher(IOptionsMonitor<ChannelPublisherOptions> options, IServiceProvider serviceProvider)
            : this(options.CurrentValue, serviceProvider)
        {
            _optionsMonitor = options;

            // Channels are in-process, singleton-registry-backed resources with no "reconnect"
            // concept, so OnChange only swaps _options for destinations declared after the change.
            _changeToken = _optionsMonitor.OnChange(updated =>
            {
                Interlocked.Exchange(ref _options, updated);
            });
        }

        public async Task SendAsync(T message, string destination)
        {
            // Snapshot once: an options change between these reads would otherwise mix old and
            // new configuration within a single send.
            ChannelPublisherOptions options = _options;

            ChannelOptions shape = Resolve(options, destination);
            Channel<ChannelEnvelope<T>> channel = _registry.GetOrCreate(destination, shape);

            using var activity = _activitySource.StartActivity($"send {destination}", ActivityKind.Producer);
            string messageId = Guid.NewGuid().ToString();
            if(activity != null)
            {
                activity.SetTag("messaging.system", "in-process");
                activity.SetTag("messaging.operation.name", "send");
                activity.SetTag("messaging.operation.type", "send");
                activity.SetTag("messaging.destination.name", destination);
                activity.SetTag("messaging.message.id", messageId);
            }

            var envelope = new ChannelEnvelope<T>
            {
                Payload = message,
                MessageId = messageId,
                TraceParent = activity == null ? null : $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}",
                TraceState = activity == null || string.IsNullOrEmpty(activity.TraceStateString) ? null : activity.TraceStateString
            };

            int timeout = options.SendTimeoutMilliseconds;

            if (timeout <= 0)
            {
                await channel.Writer.WriteAsync(envelope);
                return;
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeout));

            try
            {
                await channel.Writer.WriteAsync(envelope, timeoutCts.Token);
            }
            catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The channel for destination '{destination}' did not accept message {messageId} within {timeout}ms. " +
                    "A bounded channel stays full while nothing is reading it.",
                    ex);
            }
        }

        private static ChannelOptions Resolve(ChannelPublisherOptions options, string destination)
        {
            if (options.Destinations == null || !options.Destinations.TryGetValue(destination, out ChannelOptions shape))
            {
                throw new KeyNotFoundException(
                    $"No destination named '{destination}' is configured. Configured destinations: " +
                    string.Join(", ", options.Destinations?.Keys ?? Enumerable.Empty<string>()));
            }

            return shape;
        }

        public ValueTask DisposeAsync()
        {
            _changeToken?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
