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
        private static ManualResetEvent _resetEvent = new ManualResetEvent(false);
        private readonly ILogger<ConstantDelay> _logger;
        private readonly int _increment;
        private readonly int _maxWait;
        private int _wait;

        public LinearDelay(LinearOptions options, ILogger<ConstantDelay> logger)
        {
            _increment = options.Increment;
            _wait = 1;
            _maxWait = options.MaxWait;
            _logger = logger;
        }

        public LinearDelay(IOptions<LinearOptions> options, ILogger<ConstantDelay> logger)
            : this(options.Value, logger)
        {
        }

        public void Wait()
        {
            _logger.LogDebug("Waiting " + _wait + " seconds");
            _resetEvent.WaitOne(TimeSpan.FromSeconds(_wait));

            if (_wait < _maxWait)
            {
                _wait = _wait + _increment;
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
        public int Increment { get; set; }
        public int MaxWait { get; set; }
    }
}