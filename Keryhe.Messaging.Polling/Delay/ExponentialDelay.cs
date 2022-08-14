using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;

namespace Keryhe.Messaging.Polling.Delay
{
    public class ExponentialDelay : IDelay, IDisposable
    {
        private static ManualResetEvent _resetEvent = new ManualResetEvent(false);
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
            _resetEvent.WaitOne(TimeSpan.FromSeconds(_wait));

            if (_wait < _maxWait)
            {
                _wait = _wait * _factor;
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
        public int Factor { get; set; }
        public int MaxWait { get; set; }
    }
}