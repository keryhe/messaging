using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;

namespace Keryhe.Messaging.Polling.Delay
{
    public class ExponentialDelay : IDelay, IDisposable
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        private readonly ILogger<ExponentialDelay> _logger;
        private readonly int _maxWait;
        private readonly int _factor;
        private int _wait;

        public ExponentialDelay(ExponentialOptions options, ILogger<ExponentialDelay> logger)
        {
            _factor = options.Factor;
            _wait = 1;
            _maxWait = options.MaxWait;
            _logger = logger;
        }

        public ExponentialDelay(IOptions<ExponentialOptions> options, ILogger<ExponentialDelay> logger)
            : this(options.Value, logger)
        {
        }

        public void Wait()
        {
            _logger.LogDebug("Waiting " + _wait + " seconds");

            // Floor at one second: a Factor of 0 from configuration collapses _wait to 0 and never
            // recovers, turning the caller's poll loop into a spin.
            _resetEvent.WaitOne(TimeSpan.FromSeconds(Math.Max(1, _wait)));

            // Re-arm, or a single Cancel leaves the event signalled forever and every later Wait
            // returns instantly — turning the poll loop into a spin.
            _resetEvent.Reset();

            if (_wait < _maxWait)
            {
                // Clamp: without this the last step overshoots, so a MaxWait of 60 with a factor
                // of 10 actually waits 630 seconds.
                _wait = Math.Min(_wait * _factor, _maxWait);
            }
        }

        public void Cancel()
        {
            _logger.LogDebug("Cancelling ExponentialDelay");
            _resetEvent.Set();
        }

        public void Reset()
        {
            _logger.LogDebug("Resetting ExponentialDelay");
            _wait = 1;
        }

        public void Dispose()
        {
            _resetEvent.Close();
        }
    }

    public class ExponentialOptions
    {
        public ExponentialOptions()
        {
            // A Factor left at the int default of 0 multiplies the wait down to zero and keeps it
            // there; a MaxWait of 0 stops the backoff growing at all.
            Factor = 2;
            MaxWait = 60;
        }

        public int Factor { get; set; }
        public int MaxWait { get; set; }
    }
}