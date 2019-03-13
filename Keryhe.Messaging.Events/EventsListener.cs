using Keryhe.Messaging.Events.Helpers;
using Microsoft.Extensions.Logging;
using System;

namespace Keryhe.Messaging.Events
{
    public class EventsListener<T> : IMessageListener<T>
    {
        private IEventsHelper<T> _helper;
        private readonly ILogger<EventsListener<T>> _logger;
        private Action<T> _callback;

        public EventsListener(IEventsHelper<T> helper, ILogger<EventsListener<T>> logger)
        {
            _helper = helper;
            _logger = logger;
        }

        public void Start(Action<T> callback)
        {
            _logger.LogDebug("EventsListener Started");
            _callback = callback;
            _helper.MessageReceived += Helper_MessageReceived;
        }

        private void Helper_MessageReceived(object sender, MessagingEventsArgs<T> eventArgs)
        {
            _callback(eventArgs.Message);
        }

        public void Stop()
        {
            _helper.MessageReceived -= Helper_MessageReceived;
            _logger.LogDebug("EventsListener Stopped");
        }

        public void Dispose()
        {
            
        }
    }
}
