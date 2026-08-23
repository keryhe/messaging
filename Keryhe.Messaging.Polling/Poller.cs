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
        private CancellationTokenSource _cancellation;

        public Poller(IDelay delay, ILogger<Poller<T>> logger)
        {
            _delay = delay;
            _logger = logger;
        }

        // A Poller<T> has a single implicit source (its one abstract Poll() method), so
        // `source` is accepted only for IMessageListener<T> compatibility and otherwise ignored.
        public Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            _messageHandlerAsync = messageHandler;

            // The loop is driven by this token rather than a plain bool: it makes the caller's
            // token actually stop polling, and removes the non-volatile read from the loop
            // condition. Any previous run is stopped first.
            var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken token = linkedCancellation.Token;

            StopPolling(Interlocked.Exchange(ref _cancellation, linkedCancellation));

            // Cancellation has to break the blocking wait too, or the loop keeps running until the
            // current delay elapses.
            token.Register(() => _delay.Cancel());

            Task.Run(() => Run(token), token)
                .ContinueWith(
                    t => _logger.LogError(t.Exception, "Polling loop faulted and has stopped"),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);

            _logger.LogDebug("Polling started");
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(string source, CancellationToken cancellationToken)
        {
            StopPolling(Interlocked.Exchange(ref _cancellation, null));

            _logger.LogDebug("Polling stopped");

            return Task.CompletedTask;
        }

        private static void StopPolling(CancellationTokenSource cancellation)
        {
            if (cancellation == null)
            {
                return;
            }

            // Cancel runs the registration above, which releases a delay already in progress.
            cancellation.Cancel();
            cancellation.Dispose();
        }

        protected abstract Task<T> Poll();

        private async Task Run(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
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
                catch (Exception ex)
                {
                    // Poll() is usually a database call, so transient failures are expected. Back
                    // off and keep polling instead of ending the loop for the process lifetime.
                    _logger.LogError(ex, "Polling iteration failed; backing off before the next poll");

                    _delay.Wait();
                }
            }
        }

        public static bool CheckNullOrEmpty(T value)
        {
            if (typeof(T) == typeof(string))
                return string.IsNullOrEmpty(value as string);

            if (value == null)
                return true;

            // A collection with no items is "nothing to do", not a message. Without this an empty
            // List<T> reaches the handler and resets the delay, so the loop polls again straight
            // away — an unthrottled spin against whatever Poll() talks to.
            if (value is System.Collections.IEnumerable sequence)
            {
                System.Collections.IEnumerator enumerator = sequence.GetEnumerator();
                try
                {
                    return !enumerator.MoveNext();
                }
                finally
                {
                    (enumerator as IDisposable)?.Dispose();
                }
            }

            return value.Equals(default(T));
        }
    }
}
