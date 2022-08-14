using Keryhe.Messaging;
using Keryhe.Messaging.Polling;

namespace MessagingTester
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IPoller<List<string>> _poller;

        public Worker(IPoller<List<string>> poller, ILogger<Worker> logger)
        {
            _poller = poller;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _poller.StartAsync(Execute, stoppingToken);

        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _poller.StopAsync(cancellationToken);
            return base.StopAsync(cancellationToken);
        }

        private async Task<bool> Execute(List<string> files)
        {
            foreach(string file in files)
            {
                string text = await File.ReadAllTextAsync(file);
                _logger.LogInformation(text);
                File.Delete(file);
            }

            
            return true;
        }

        
    }
}