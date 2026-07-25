using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Keryhe.Messaging.Channels
{
    internal class ChannelRegistry<T> : IChannelRegistry<T>
    {
        private readonly ILogger<ChannelRegistry<T>> _logger;
        private readonly ConcurrentDictionary<string, (Channel<ChannelEnvelope<T>> Channel, ChannelOptions Shape)> _channels = new();

        public ChannelRegistry(ILogger<ChannelRegistry<T>> logger)
        {
            _logger = logger;
        }

        public Channel<ChannelEnvelope<T>> GetOrCreate(string name, ChannelOptions shape)
        {
            var entry = _channels.GetOrAdd(name, _ => (Create(shape), shape));

            if (!ShapesMatch(entry.Shape, shape))
            {
                _logger.LogWarning(
                    "Channel '{Name}' was already created with a different shape than requested. " +
                    "The channel keeps the shape it was first created with; the requested shape is ignored.",
                    name);
            }

            return entry.Channel;
        }

        private static bool ShapesMatch(ChannelOptions a, ChannelOptions b)
        {
            return a.Capacity == b.Capacity
                && a.FullMode == b.FullMode
                && a.SingleReader == b.SingleReader
                && a.SingleWriter == b.SingleWriter;
        }

        private static Channel<ChannelEnvelope<T>> Create(ChannelOptions shape)
        {
            if (shape.Capacity is int capacity)
            {
                return Channel.CreateBounded<ChannelEnvelope<T>>(new BoundedChannelOptions(capacity)
                {
                    FullMode = shape.FullMode,
                    SingleReader = shape.SingleReader,
                    SingleWriter = shape.SingleWriter
                });
            }

            return Channel.CreateUnbounded<ChannelEnvelope<T>>(new UnboundedChannelOptions
            {
                SingleReader = shape.SingleReader,
                SingleWriter = shape.SingleWriter
            });
        }
    }
}
