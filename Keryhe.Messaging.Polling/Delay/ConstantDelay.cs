using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;

namespace Keryhe.Messaging.Polling.Delay
{
    public class ConstantDelay : IDelay, IDisposable
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
        private readonly ILogger<ConstantDelay> _logger;
        private readonly int _wait;

        public ConstantDelay(ConstantOptions options, ILogger<ConstantDelay> logger)
        {
            _wait = options.Interval;
            _logger = logger;
        }

        public ConstantDelay(IOptions<ConstantOptions> options, ILogger<ConstantDelay> logger)
            : this(options.Value, logger)
        {
        }

        public void Wait()
        {
            _logger.LogDebug("Waiting " + _wait + " seconds");

            // Floor at one second: a zero or negative interval from configuration would otherwise
            // make this a no-op and turn the caller's poll loop into a spin.
            _resetEvent.WaitOne(TimeSpan.FromSeconds(Math.Max(1, _wait)));

            // Re-arm, or a single Cancel leaves the event signalled forever and every later Wait
            // returns instantly — turning the poll loop into a spin.
            _resetEvent.Reset();
        }

        public void Cancel()
        {
            _logger.LogDebug("Cancelling IntervalDelay");
            _resetEvent.Set();
        }

        public void Reset()
        {
            _logger.LogDebug("Resetting IntervalDelay");
        }

        public void Dispose()
        {
            _resetEvent.Close();
        }
    }

    public class ConstantOptions
    {
        public ConstantOptions()
        {
            // Left at the int default of 0 this waits for no time at all, so the poll loop spins.
            Interval = 5;
        }

        public int Interval { get; set; }
    }
}