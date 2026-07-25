using System.Threading.Channels;

namespace Keryhe.Messaging.Channels
{
    internal interface IChannelRegistry<T>
    {
        Channel<ChannelEnvelope<T>> GetOrCreate(string name, ChannelOptions shape);
    }
}
