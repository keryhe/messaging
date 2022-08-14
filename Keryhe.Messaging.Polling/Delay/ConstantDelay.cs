using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;

namespace Keryhe.Messaging.Polling.Delay
{
    public class ConstantDelay : IDelay, IDisposable
    {
        private static ManualResetEvent _resetEvent = new ManualResetEvent(false);
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
            _resetEvent.WaitOne(TimeSpan.FromSeconds(_wait));
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
        public int Interval { get; set; }
    }
}