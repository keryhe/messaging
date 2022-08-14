using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQPublisher<T> : IMessagePublisher<T>
    {
        private readonly RabbitMQPublisherOptions _options;
        private IConnection _connection;

        public RabbitMQPublisher(RabbitMQPublisherOptions options)
        {
            _options = options;
            var factory = new ConnectionFactory() 
            {
                UserName = _options.Factory.UserName,
                Password = _options.Factory.Password,
                VirtualHost = _options.Factory.VirtualHost,
                HostName = _options.Factory.HostName,
                Port = _options.Factory.Port
            };
            _connection = factory.CreateConnection();
        }

        public RabbitMQPublisher(IOptions<RabbitMQPublisherOptions> options)
            :this(options.Value)
        {
        }

        public Task SendAsync(T message)
        {
            using (var channel = _connection.CreateModel())
            {
                channel.QueueDeclare(
                    queue: _options.Queue.Name,
                    durable: _options.Queue.Durable,
                    exclusive: _options.Queue.Exclusive,
                    autoDelete: _options.Queue.AutoDelete,
                    arguments: null);

                var body = Serialize(message);
                var properties = channel.CreateBasicProperties();
                properties.Persistent = _options.Persistent;
                properties.ContentType = "application/json";

                channel.BasicPublish(
                    exchange: _options.Exchange.Name,
                    routingKey: _options.Queue.Name,
                    basicProperties: properties,
                    body: body);
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _connection.Close();
            return ValueTask.CompletedTask;
        }

        private byte[] Serialize(T data)
        {
            string jsonified = JsonSerializer.Serialize<T>(data);
            byte[] databuffer = Encoding.UTF8.GetBytes(jsonified);
            return databuffer;
        }
    }
}
