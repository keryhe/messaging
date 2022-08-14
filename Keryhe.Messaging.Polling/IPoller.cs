using Keryhe.Messaging;
using System;
using System.Collections.Generic;

namespace Keryhe.Messaging.Polling
{
    public interface IPoller<T> : IMessageListener<T>
    {
    }
}