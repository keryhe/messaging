using Keryhe.Messaging.Events.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace Keryhe.Messaging.Events
{
    public class EventsPublisher<T> : IMessagePublisher<T>
    {
        private IEventsHelper<T> _helper;

        public EventsPublisher(IEventsHelper<T> helper)
        {
            _helper = helper;
        }

        public void Send(T message)
        {
            _helper.Publish(message);
        }

        public void Dispose()
        {
        }
    }
}
