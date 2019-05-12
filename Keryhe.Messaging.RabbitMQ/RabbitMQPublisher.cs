using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Newtonsoft.Json;
using System.Text;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQPublisher<T> : IMessagePublisher<T>
    {
        private readonly RabbitMQOptions _options;
        private IConnection _connection;

        public RabbitMQPublisher(RabbitMQPublisherOptions options)
        {
            _options = options;
            var factory = new ConnectionFactory() { HostName = _options.Host };
            _connection = factory.CreateConnection();
        }

        public RabbitMQPublisher(IOptions<RabbitMQPublisherOptions> options)
            :this(options.Value)
        {
        }

        public void Send(T message)
        {
            using (var channel = _connection.CreateModel())
            {
                channel.QueueDeclare(
                    queue: _options.Queue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);

                var body = Serialize(message);
                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;

                channel.BasicPublish(
                    exchange: "",
                    routingKey: _options.Queue,
                    basicProperties: properties,
                    body: body);
            }
        }

        public void Dispose()
        {
            _connection.Close();
        }

        private byte[] Serialize(T data)
        {
            string jsonified = JsonConvert.SerializeObject(data);
            byte[] databuffer = Encoding.UTF8.GetBytes(jsonified);
            return databuffer;
        }
    }
}
