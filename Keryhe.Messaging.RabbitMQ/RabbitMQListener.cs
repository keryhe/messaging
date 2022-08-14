using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQListener<T> : IMessageListener<T>
    {
        private readonly RabbitMQListenerOptions _options;
        private readonly ILogger<RabbitMQListener<T>> _logger;
        private IConnection _connection;
        private IModel _channel;
        private AsyncEventingBasicConsumer _consumer;
        private Func<T, Task<bool>> _messageHandler;

        public RabbitMQListener(RabbitMQListenerOptions options, ILogger<RabbitMQListener<T>> logger)
        {
            _options = options;
            _logger = logger;
            var factory = new ConnectionFactory()
            {
                UserName = _options.Factory.UserName,
                Password = _options.Factory.Password,
                VirtualHost = _options.Factory.VirtualHost,
                HostName = _options.Factory.HostName,
                Port = _options.Factory.Port
            };
            factory.DispatchConsumersAsync = true;
            _connection = factory.CreateConnection();
        }

        public RabbitMQListener(IOptions<RabbitMQListenerOptions> options, ILogger<RabbitMQListener<T>> logger)
            : this(options.Value, logger)
        {
        }

        public Task SubscribeAsync(Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            _messageHandler = messageHandler;

            _channel = _connection.CreateModel();

            if (!string.IsNullOrEmpty(_options.Exchange.Name))
            {
                _channel.ExchangeDeclare(
                    exchange: _options.Exchange.Name,
                    type: _options.Exchange.Type,
                    durable: _options.Exchange.Durable,
                    arguments: null);
            }

            _channel.QueueDeclare(
                queue: _options.Queue.Name,
                durable: _options.Queue.Durable,
                exclusive: _options.Queue.Exclusive,
                autoDelete: _options.Queue.AutoDelete,
                arguments: null);

            if (!string.IsNullOrEmpty(_options.Exchange.Name))
            {
                _channel.QueueBind(
                    queue: _options.Queue.Name,
                    exchange: _options.Exchange.Name,
                    routingKey: _options.Exchange.RoutingKey,
                    arguments: null);
            }

            _channel.BasicQos(
                prefetchSize: _options.BasicQos.PrefetchSize,
                prefetchCount: _options.BasicQos.PrefetchCount,
                global: _options.BasicQos.Global);

            _consumer = new AsyncEventingBasicConsumer(_channel);
            _consumer.Received += Consumer_Received;

            _channel.BasicConsume(
                queue: _options.Queue.Name,
                autoAck: _options.AutoAck,
                consumer: _consumer);

            _logger.LogDebug("RabbitMQListener Started");
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken)
        {
            _channel.Close();
            _consumer.Received -= Consumer_Received;

            _logger.LogDebug("RabbitMQListener Stopped");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _connection.Close();
            return ValueTask.CompletedTask;
        }

        private async Task Consumer_Received(object sender, BasicDeliverEventArgs e)
        {
            var body = e.Body;
            T message = Deserialize(body.ToArray());
            try
            {

                bool success = await _messageHandler(message);

                if (!_options.AutoAck)
                {
                    if (!success)
                    {
                        _channel.BasicNack(e.DeliveryTag, false, false);
                    }
                    _channel.BasicAck(e.DeliveryTag, false);
                }
            }
            catch
            {
                if (!_options.AutoAck)
                {
                    _channel.BasicNack(e.DeliveryTag, false, false);
                }
            }
        }

        private T Deserialize(byte[] array)
        {
            string jsonified = Encoding.UTF8.GetString(array);
            T data = JsonSerializer.Deserialize<T>(jsonified);
            return data;
        }
    }


}
