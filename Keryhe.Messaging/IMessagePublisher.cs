using System;
using System.Collections.Generic;
using System.Text;

namespace Keryhe.Messaging
{
    public interface IMessagePublisher<T> : IDisposable
    {
        void Send(T message);
    }
}
