﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.Json;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQListener<T> : IMessageListener<T>
    {
        private readonly RabbitMQListenerOptions _options;
        private readonly ILogger<RabbitMQListener<T>> _logger;
        private IConnection _connection;
        private IModel _channel;

        private Action<T> _callback;
        private EventingBasicConsumer _consumer;

        public RabbitMQListener(RabbitMQListenerOptions options, ILogger<RabbitMQListener<T>> logger)
        {
            _options = options;
            _logger = logger;
            var factory = new ConnectionFactory() { HostName = _options.Host };
            _connection = factory.CreateConnection();
        }

        public RabbitMQListener(IOptions<RabbitMQListenerOptions> options, ILogger<RabbitMQListener<T>> logger)
            :this(options.Value, logger)
        {
        }

        public void Start(Action<T> callback)
        {
            _logger.LogDebug("RabbitMQListener Started");

            _callback = callback;

            _channel = _connection.CreateModel();
            _channel.QueueDeclare(
                queue: _options.Queue,
                durable: _options.Durable,
                exclusive: _options.Exclusive,
                autoDelete: _options.AutoDelete,
                arguments: null);

            _channel.BasicQos(_options.BasicQos.PrefetchSize, _options.BasicQos.PrefetchCount, _options.BasicQos.Global);

            _consumer = new EventingBasicConsumer(_channel);

            _consumer.Received += Consumer_Received;

            _channel.BasicConsume(
                queue: _options.Queue,
                autoAck: _options.AutoAck,
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
            _channel.Close();
            _consumer.Received -= Consumer_Received;
            _logger.LogDebug("RabbitMQListener Stopped");
        }

        public void Dispose()
        {
            _connection.Close();          
        }

        private T Deserialize(byte[] array)
        {
            string jsonified = Encoding.UTF8.GetString(array);
            T data = JsonSerializer.Deserialize<T>(jsonified);
            return data;
        }
    }
}
