using Azure.Messaging.ServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Keryhe.Messaging.Azure
{
    public class ServiceBusPublisher<T> : IMessagePublisher<T>, IAsyncDisposable
    {
        private readonly ServiceBusPublisherOptions _options;
        private readonly ServiceBusClient _client;
        private readonly ServiceBusSender _sender;


        public ServiceBusPublisher(ServiceBusPublisherOptions options)
        {
            _options = options;

            _client = new ServiceBusClient(_options.ConnectionString);

            if(string.IsNullOrEmpty(_options.QueueName))
            {
                _sender = _client.CreateSender(_options.TopicName);
            }
            else
            {
                _sender = _client.CreateSender(_options.QueueName);
            }
            
        }

        public async Task SendAsync(T message)
        {
            var body = Serialize(message);
            await _sender.SendMessageAsync(new ServiceBusMessage(body));
        }

        public async ValueTask DisposeAsync()
        {
            await _sender.DisposeAsync();
            await _client.DisposeAsync();
        }

        private string Serialize(T data)
        {
            return JsonSerializer.Serialize<T>(data);

        }
    }
}
