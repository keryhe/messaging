using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Keryhe.Messaging.Channels
{
    internal class ChannelRegistry<T> : IChannelRegistry<T>
    {
        private readonly ILogger<ChannelRegistry<T>> _logger;
        // Lazy so a contended GetOrAdd cannot build several channels for one name: only the winning
        // Lazy is ever evaluated. Losing factories that had already created a channel would leave
        // writers and readers holding different objects for the same name.
        private readonly ConcurrentDictionary<string, Lazy<(Channel<ChannelEnvelope<T>> Channel, ChannelOptions Shape)>> _channels = new();
        private readonly ConcurrentDictionary<string, bool> _warned = new();

        public ChannelRegistry(ILogger<ChannelRegistry<T>> logger)
        {
            _logger = logger;
        }

        public Channel<ChannelEnvelope<T>> GetOrCreate(string name, ChannelOptions shape)
        {
            var entry = _channels.GetOrAdd(name, _ => new Lazy<(Channel<ChannelEnvelope<T>>, ChannelOptions)>(() => (Create(shape), shape))).Value;

            // GetOrCreate runs on every send, so warning unconditionally floods the log at message
            // rate. The mismatch is a startup-configuration problem: saying it once is enough.
            if (!ShapesMatch(entry.Shape, shape) && _warned.TryAdd(name, true))
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
