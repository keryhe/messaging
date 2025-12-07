using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQListener<T> : IMessageListener<T>
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.RabbitMQ");
        private readonly RabbitMQListenerOptions _options;
        private readonly ILogger<RabbitMQListener<T>> _logger;

        private readonly ConnectionFactory _factory;
        private IConnection _connection;
        private AsyncEventingBasicConsumer _consumer;
        private AsyncEventHandler<BasicDeliverEventArgs> _consumerReceived;

        private Func<T, Task<bool>> _handleMessage;

        public RabbitMQListener(IRabbitMQListenerOptionsProvider optionsProvider, ILogger<RabbitMQListener<T>> logger)
        {
            _options = optionsProvider.LoadOptions();
            _logger = logger;

            _factory = new ConnectionFactory()
            {
                UserName = _options.Factory.UserName,
                Password = _options.Factory.Password,
                VirtualHost = _options.Factory.VirtualHost,
                HostName = _options.Factory.HostName,
                Port = _options.Factory.Port
            }; 
        }

        public RabbitMQListener(IOptions<RabbitMQListenerOptions> options, ILogger<RabbitMQListener<T>> logger)
        {
            _options = options.Value;
            _logger = logger;

            _factory = new ConnectionFactory()
            {
                UserName = _options.Factory.UserName,
                Password = _options.Factory.Password,
                VirtualHost = _options.Factory.VirtualHost,
                HostName = _options.Factory.HostName,
                Port = _options.Factory.Port
            };
        }

        public async Task SubscribeAsync(Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            if(string.IsNullOrEmpty(_options.Queue?.Name))
            {
                throw new ArgumentNullException("Queue Name cannot be null");
            }

            _connection ??= await _factory.CreateConnectionAsync();

            _handleMessage = messageHandler;

            using var channel = await _connection.CreateChannelAsync();

            if (!string.IsNullOrEmpty(_options.Exchange?.Name))
            {
                await channel.ExchangeDeclareAsync(
                    exchange: _options.Exchange.Name,
                    type: _options.Exchange.Type,
                    durable: _options.Exchange.Durable,
                    arguments: null);
            }

            await channel.QueueDeclareAsync(
                queue: _options.Queue.Name,
                durable: _options.Queue.Durable,
                exclusive: _options.Queue.Exclusive,
                autoDelete: _options.Queue.AutoDelete,
                arguments: null);

            if (!string.IsNullOrEmpty(_options.Exchange.Name))
            {
                await channel.QueueBindAsync(
                    queue: _options.Queue.Name,
                    exchange: _options.Exchange.Name,
                    routingKey: _options.Exchange.RoutingKey,
                    arguments: null);
            }

            await channel.BasicQosAsync(
                prefetchSize: _options.BasicQos.PrefetchSize,
                prefetchCount: _options.BasicQos.PrefetchCount,
                global: _options.BasicQos.Global);

            _consumer = new AsyncEventingBasicConsumer(channel);
            _consumerReceived = async (sender, ea) =>
            {
                var parentContext = ExtractTraceContext(ea.BasicProperties);
                using var activity = _activitySource.StartActivity("RabbitMQ Consume", ActivityKind.Consumer, parentContext);
                if(activity != null)
                {
                    activity.SetTag("messaging.system", "rabbitmq");
                    activity.SetTag("messaging.source", ea.Exchange);
                    activity.SetTag("messaging.rabbitmq.routing_key", ea.RoutingKey);
                    activity.SetTag("messaging.message_payload_size_bytes", ea.Body.Length);
                    activity.SetTag("messaging.operation", "consume");
                }

                var body = ea.Body;
                T message = Deserialize(body.ToArray());
                try
                {
                    bool success = await _handleMessage(message);

                    if (!_options.AutoAck)
                    {
                        if (!success)
                        {
                            await channel.BasicNackAsync(ea.DeliveryTag, false, false);
                        }
                        await channel.BasicAckAsync(ea.DeliveryTag, false);
                    }
                }
                catch
                {
                    if (!_options.AutoAck)
                    {
                        await channel.BasicNackAsync(ea.DeliveryTag, false, false);
                    }
                }
            };
            _consumer.ReceivedAsync += _consumerReceived;

            await channel.BasicConsumeAsync(
                queue: _options.Queue.Name,
                autoAck: _options.AutoAck,
                consumer: _consumer);

            _logger.LogInformation("RabbitMQListener Started");
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken)
        {
            _consumer.ReceivedAsync -= _consumerReceived;

            _logger.LogInformation("RabbitMQListener Stopped");
            return Task.CompletedTask;
        }

        private T Deserialize(byte[] array)
        {
            string jsonified = Encoding.UTF8.GetString(array);
            _logger.LogDebug("Listener received a message: {Message}", jsonified);
            T data = JsonSerializer.Deserialize<T>(jsonified);
            return data;
        }

        private ActivityContext ExtractTraceContext(IReadOnlyBasicProperties properties)
        {
            if (properties?.Headers == null)
                return default;

            // Extract traceparent header
            if (properties.Headers.TryGetValue("traceparent", out var traceparentObj))
            {
                var traceparent = traceparentObj switch
                {
                    byte[] bytes => Encoding.UTF8.GetString(bytes),
                    string str => str,
                    _ => null
                };

                if (!string.IsNullOrEmpty(traceparent))
                {
                    // Parse traceparent: version-trace_id-span_id-flags
                    var parts = traceparent.Split('-');
                    if (parts.Length == 4)
                    {
                        var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
                        var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
                        var traceFlags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

                        // Extract tracestate if present
                        string traceState = null;
                        if (properties.Headers.TryGetValue("tracestate", out var tracestateObj))
                        {
                            traceState = tracestateObj switch
                            {
                                byte[] bytes => Encoding.UTF8.GetString(bytes),
                                string str => str,
                                _ => null
                            };
                        }

                        return new ActivityContext(traceId, spanId, traceFlags, traceState);
                    }
                }
            }

            return default;
        }
    }
}
