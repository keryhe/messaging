using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Keryhe.Messaging.Polling.Delay
{
    public class LinearDelay : IDelay, IDisposable
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        private readonly ILogger<LinearDelay> _logger;
        private readonly int _increment;
        private readonly int _maxWait;
        private int _wait;

        public LinearDelay(LinearOptions options, ILogger<LinearDelay> logger)
        {
            _increment = options.Increment;
            _wait = 1;
            _maxWait = options.MaxWait;
            _logger = logger;
        }

        public LinearDelay(IOptions<LinearOptions> options, ILogger<LinearDelay> logger)
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
                // Clamp, so the final step lands on MaxWait rather than past it.
                _wait = Math.Min(_wait + _increment, _maxWait);
            }
        }

        public void Cancel()
        {
            _logger.LogDebug("Cancelling LinearDelay");
            _resetEvent.Set();
        }

        public void Reset()
        {
            _logger.LogDebug("Resetting LinearDelay");
            _wait = 1;
        }

        public void Dispose()
        {
            _resetEvent.Close();
        }
    }

    public class LinearOptions
    {
        public LinearOptions()
        {
            // Left at the int default of 0 the backoff never grows past its initial second.
            Increment = 5;
            MaxWait = 60;
        }

        public int Increment { get; set; }
        public int MaxWait { get; set; }
    }
}