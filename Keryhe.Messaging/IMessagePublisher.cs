using System;
using System.Collections.Generic;
using System.Text;

namespace Keryhe.Messaging
{
    public interface IMessagePublisher<T>
    {
        void Send(T message);
    }
}
