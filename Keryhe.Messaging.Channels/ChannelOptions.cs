using System.Collections.Generic;
using System.Threading.Channels;

namespace Keryhe.Messaging.Channels
{
    public class ChannelPublisherOptions
    {
        public Dictionary<string, ChannelOptions> Destinations { get; set; }
    }

    public class ChannelListenerOptions
    {
        public Dictionary<string, ChannelOptions> Sources { get; set; }
    }

    // Shared shape for a named channel. A channel is one physical in-process resource, so the
    // same shape class is used by both the publisher's Destinations and the listener's Sources —
    // whichever side resolves a given name first is the one whose shape actually takes effect.
    public class ChannelOptions
    {
        public ChannelOptions()
        {
            FullMode = BoundedChannelFullMode.Wait;
        }

        public int? Capacity { get; set; }
        public BoundedChannelFullMode FullMode { get; set; }
        public bool SingleReader { get; set; }
        public bool SingleWriter { get; set; }
    }
}
