using System;
using System.Collections.Generic;
using System.Text;

namespace Keryhe.Messaging.Events.Helpers
{
    public class MessagingEventsArgs<T> : EventArgs
    {
        public MessagingEventsArgs(T message)
        {
            Message = message;
        }

        public T Message { get; }
    }
}
