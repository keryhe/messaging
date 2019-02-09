using System;

namespace Keryhe.Messaging
{
    public interface IMessageListener<T> : IDisposable
    {
        void Start(Action<T> callback);
        void Stop();
    }
}
