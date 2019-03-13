using System;
using System.Collections.Generic;
using System.Text;

namespace Keryhe.Messaging.Events.Helpers
{
    public interface IEventsHelper<T>
    {
        event MessagingEventHandler<T> MessageReceived;

        void Publish(T message);
    }

    public delegate void MessagingEventHandler<T>(object sender, MessagingEventsArgs<T> eventArgs);
}
