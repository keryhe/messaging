using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQPublisher<T> : IMessagePublisher<T>
    {
        private readonly RabbitMQOptions _options;
        private IConnection _connection;

        public RabbitMQPublisher(IOptions<RabbitMQOptions> options)
        {
            _options = options.Value;
            var factory = new ConnectionFactory() { HostName = _options.Host };
            _connection = factory.CreateConnection();
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

        private byte[] Serialize(T data)
        {
            var formatter = new BinaryFormatter();
            var stream = new MemoryStream();
            formatter.Serialize(stream, data);
            return stream.ToArray();
        }
    }
}
