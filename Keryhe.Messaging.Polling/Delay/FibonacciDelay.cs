using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;

namespace Keryhe.Messaging.Polling.Delay
{
    public class FibonacciDelay : IDelay, IDisposable
    {
        private ManualResetEvent _resetEvent = new ManualResetEvent(false);
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
            _resetEvent.WaitOne(TimeSpan.FromSeconds(_wait));

            if (_wait < _maxWait)
            {
                int currentWait = _wait;
                _wait = _previousWait + currentWait;
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
        public int MaxWait { get; set; }
    }
}