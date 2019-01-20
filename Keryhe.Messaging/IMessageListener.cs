using System;

namespace Keryhe.Messaging
{
    public interface IMessageListener<T>
    {
        void Start(Action<T> callback);
        void Stop();
    }
}
