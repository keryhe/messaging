using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;

namespace Keryhe.Messaging.Polling.Delay
{
    public class FibonacciDelay : IDelay, IDisposable
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        private readonly ILogger<FibonacciDelay> _logger;
        private readonly int _maxWait;
        private int _previousWait;
        private int _wait;

        public FibonacciDelay(FibonacciOptions options, ILogger<FibonacciDelay> logger)
        {
            _previousWait = 0;
            _wait = 1;
            _maxWait = options.MaxWait;
            _logger = logger;
        }

        public FibonacciDelay(IOptions<FibonacciOptions> options, ILogger<FibonacciDelay> logger)
            : this(options.Value, logger)
        {
        }

        public void Wait()
        {
            _logger.LogDebug("Waiting " + _wait + " seconds");

            // Floor at one second so a zero or negative interval cannot spin the caller's loop.
            _resetEvent.WaitOne(TimeSpan.FromSeconds(Math.Max(1, _wait)));

            // Re-arm, or a single Cancel leaves the event signalled forever and every later Wait
            // returns instantly — turning the poll loop into a spin.
            _resetEvent.Reset();

            if (_wait < _maxWait)
            {
                int currentWait = _wait;
                // Clamp, so the final step lands on MaxWait rather than past it.
                _wait = Math.Min(_previousWait + currentWait, _maxWait);
                _previousWait = currentWait;
            }
        }

        public void Cancel()
        {
            _logger.LogDebug("Cancelling FibonacciDelay");
            _resetEvent.Set();
        }

        public void Reset()
        {
            _logger.LogDebug("Resetting FibonacciDelay");
            _wait = 1;
            _previousWait = 0;
        }

        public void Dispose()
        {
            _resetEvent.Close();
        }
    }

    public class FibonacciOptions
    {
        public FibonacciOptions()
        {
            // Left at the int default of 0 the backoff never grows past its initial second.
            MaxWait = 60;
        }

        public int MaxWait { get; set; }
    }
}