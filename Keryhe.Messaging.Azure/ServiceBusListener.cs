using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Keryhe.Messaging.Azure
{
    public class ServiceBusListener<T> : IMessageListener<T>
    {
        private readonly ServiceBusListenerOptions _options;
        private readonly ILogger<ServiceBusListener<T>> _logger;
        private ServiceBusClient _client;
        private ServiceBusProcessor _processor;
        private Func<T, Task<bool>> _messageHandler;

        public ServiceBusListener(ServiceBusListenerOptions options, ILogger<ServiceBusListener<T>> logger)
        {
            _options = options;
            _logger = logger;

            _client = new ServiceBusClient(_options.ConnectionString);
        }

        public ServiceBusListener(IOptions<ServiceBusListenerOptions> options, ILogger<ServiceBusListener<T>> logger)
            : this(options.Value, logger)
        {
        }

        public async Task SubscribeAsync(Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            _messageHandler = messageHandler;

            if(string.IsNullOrEmpty(_options.QueueName))
            {
                _processor = _client.CreateProcessor(_options.TopicName, _options.SubscriptionName);
            }
            else
            {
                _processor = _client.CreateProcessor(_options.QueueName);
            }
            

            _processor.ProcessMessageAsync += ProcessMessageAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            await _processor.StartProcessingAsync(cancellationToken);

            _logger.LogDebug("ServiceBusListener Started");
        }

        public async Task UnsubscribeAsync(CancellationToken cancellation)
        {
            _processor.ProcessMessageAsync -= ProcessMessageAsync;
            _processor.ProcessErrorAsync -= ProcessErrorAsync;

            await _processor.DisposeAsync();
            

            _logger.LogDebug("ServiceBusListener Stopped");
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
        }

        private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
        {
            T message = Deserialize(args.Message.Body.ToString());
            bool success = await _messageHandler(message);

            if (success)
            {
                await args.CompleteMessageAsync(args.Message);
            }
        }

        private Task ProcessErrorAsync(ProcessErrorEventArgs args)
        {
            _logger.LogError(args.Exception.Message);
            return Task.CompletedTask;
        }

        private T Deserialize(string message)
        {
            T data = JsonSerializer.Deserialize<T>(message);
            return data;
        } 
    }
}