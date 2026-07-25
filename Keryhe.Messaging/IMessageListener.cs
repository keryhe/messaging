using System;
using System.Threading;
using System.Threading.Tasks;

namespace Keryhe.Messaging
{
    public interface IMessageListener<T>
    {
        Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken);
        Task UnsubscribeAsync(string source, CancellationToken cancellationToken);
    }
}
