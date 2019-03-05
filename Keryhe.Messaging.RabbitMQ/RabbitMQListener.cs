using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQListener<T> : IMessageListener<T>
    {
        private readonly RabbitMQOptions _options;
        private IConnection _connection;
        private IModel _channel;

        private Action<T> _callback;
        private EventingBasicConsumer _consumer;

        public RabbitMQListener(RabbitMQListenerOptions options)
        {
            _options = options;
            var factory = new ConnectionFactory() { HostName = _options.Host };
            _connection = factory.CreateConnection();
        }

        public RabbitMQListener(IOptions<RabbitMQListenerOptions> options)
            :this(options.Value)
        {
        }

        public void Start(Action<T> callback)
        {
            _callback = callback;

            _channel = _connection.CreateModel();
            _channel.QueueDeclare(
                queue: _options.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            _consumer = new EventingBasicConsumer(_channel);

            _consumer.Received += Consumer_Received;

            _channel.BasicConsume(
                queue: _options.Queue,
                autoAck: true,
                consumer: _consumer);
        }

        private void Consumer_Received(object sender, BasicDeliverEventArgs e)
        {
            var body = e.Body;
            T message = Deserialize(body);
            _callback(message);
        }

        public void Stop()
        {
            _consumer.Received -= Consumer_Received;
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
