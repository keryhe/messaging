using System;
using System.Collections.Generic;
using System.Text;

namespace Keryhe.Messaging.Events.Helpers
{
    public class EventsHelper<T> : IEventsHelper<T>
    {
        public event MessagingEventHandler<T> MessageReceived;

        public void Publish(T message)
        {
            OnMessageReceived(new MessagingEventsArgs<T>(message));
        }

        protected  virtual void OnMessageReceived(MessagingEventsArgs<T> e)
        {
            MessageReceived?.Invoke(this, e);
        }
    }
}
