using System;
using System.Threading;
using System.Threading.Tasks;

namespace Keryhe.Messaging
{
    public interface IMessageListener<T> : IAsyncDisposable
    {
        Task SubscribeAsync(Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken);
        Task UnsubscribeAsync(CancellationToken cancellationToken);
    }
}
