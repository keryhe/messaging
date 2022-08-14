using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Keryhe.Messaging
{
    public interface IMessagePublisher<T> : IAsyncDisposable
    {
        Task SendAsync(T message);
    }
}
