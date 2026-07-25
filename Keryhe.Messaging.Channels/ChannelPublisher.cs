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
    public class ChannelPublisher<T> : IMessagePublisher<T>
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.Channels");
        private readonly ChannelPublisherOptions _options;
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

        public async Task SendAsync(T message, string destination)
        {
            ChannelOptions shape = Resolve(destination);
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

            await channel.Writer.WriteAsync(envelope);
        }

        private ChannelOptions Resolve(string destination)
        {
            if (_options.Destinations == null || !_options.Destinations.TryGetValue(destination, out ChannelOptions shape))
            {
                throw new KeyNotFoundException(
                    $"No destination named '{destination}' is configured. Configured destinations: " +
                    string.Join(", ", _options.Destinations?.Keys ?? Enumerable.Empty<string>()));
            }

            return shape;
        }
    }
}
