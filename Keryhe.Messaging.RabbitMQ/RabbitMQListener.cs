using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQListener<T> : IMessageListener<T>
    {
        private readonly RabbitMQOptions _options;
        private IConnection _connection;
        private IModel _channel;

        public RabbitMQListener(IOptions<RabbitMQListenerOptions> options)
        {
            _options = options.Value;
            var factory = new ConnectionFactory() { HostName = _options.Host };
            _connection = factory.CreateConnection();
        }

        public void Start(Action<T> callback)
        {

            _channel = _connection.CreateModel();

            _channel.QueueDeclare(
                queue: _options.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body;

                T message = Deserialize(body);
                callback(message);
            };
            _channel.BasicConsume(
                queue: _options.Queue,
                autoAck: true,
                consumer: consumer);

        }

        public void Stop()
        {
            
        }

        public void Dispose()
        {
            _connection.Close();
        }

        private T Deserialize(byte[] array)
        {
            var stream = new MemoryStream(array);
            var formatter = new BinaryFormatter();
            return (T)formatter.Deserialize(stream);
        }
    }
}
