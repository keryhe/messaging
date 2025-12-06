using Keryhe.Messaging.Polling.Delay;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Keryhe.Messaging.Polling
{
    public abstract class Poller<T> : IPoller<T>
    {
        private readonly IDelay _delay;
        private readonly ILogger<Poller<T>> _logger;
        private Func<T, Task<bool>> _messageHandlerAsync;
        private bool _status;

        public Poller(IDelay delay, ILogger<Poller<T>> logger)
        {
            _delay = delay;
            _logger = logger;
            _status = false;
        }

        public Task SubscribeAsync(Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            _messageHandlerAsync = messageHandler;
            _status = true;

            Task.Run(() => Run(), cancellationToken);

            _logger.LogDebug("Polling started");
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken)
        {
            _status = false;
            _delay.Cancel();
            _logger.LogDebug("Polling stopped");

            return Task.CompletedTask;
        }

        protected abstract Task<T> Poll();

        private async Task Run()
        {
            while (_status)
            {
                T item = await Poll();

                if (CheckNullOrEmpty(item))
                {
                    _delay.Wait();
                }
                else
                {
                    await _messageHandlerAsync(item);

                    _delay.Reset();
                }
            }
        }

        public static bool CheckNullOrEmpty(T value)
        {
            if (typeof(T) == typeof(string))
                return string.IsNullOrEmpty(value as string);

            return value == null || value.Equals(default(T));
        }
    }
}
