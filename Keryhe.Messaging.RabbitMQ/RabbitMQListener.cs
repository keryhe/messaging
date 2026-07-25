using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQListener<T> : IMessageListener<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.RabbitMQ");
        private readonly RabbitMQListenerOptions _options;
        private readonly ILogger<RabbitMQListener<T>> _logger;

        private readonly ConnectionFactory _factory;
        private readonly ConcurrentDictionary<string, (string ConsumerTag, AsyncEventingBasicConsumer Consumer)> _consumers = new();
        private IConnection _connection;
        private IChannel _channel;

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

        public async Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            RabbitMQListenerSourceOptions sourceOptions = Resolve(source);

            if(string.IsNullOrEmpty(sourceOptions.Queue?.Name))
            {
                throw new ArgumentNullException("Queue Name cannot be null");
            }

            _connection ??= await _factory.CreateConnectionAsync();

            _channel ??= await _connection.CreateChannelAsync();

            if (!string.IsNullOrEmpty(sourceOptions.Exchange?.Name))
            {
                await _channel.ExchangeDeclareAsync(
                    exchange: sourceOptions.Exchange.Name,
                    type: sourceOptions.Exchange.Type,
                    durable: sourceOptions.Exchange.Durable,
                    arguments: null);
            }

            await _channel.QueueDeclareAsync(
                queue: sourceOptions.Queue.Name,
                durable: sourceOptions.Queue.Durable,
                exclusive: sourceOptions.Queue.Exclusive,
                autoDelete: sourceOptions.Queue.AutoDelete,
                arguments: null);

            if (!string.IsNullOrEmpty(sourceOptions.Exchange.Name))
            {
                await _channel.QueueBindAsync(
                    queue: sourceOptions.Queue.Name,
                    exchange: sourceOptions.Exchange.Name,
                    routingKey: sourceOptions.Queue.Name,
                    arguments: null);
            }

            await _channel.BasicQosAsync(
                prefetchSize: _options.BasicQos.PrefetchSize,
                prefetchCount: _options.BasicQos.PrefetchCount,
                global: _options.BasicQos.Global);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += (sender, ea) => ConsumerReceivedAsync(messageHandler, sourceOptions.AutoAck, sender, ea);

            string consumerTag = await _channel.BasicConsumeAsync(
                queue: sourceOptions.Queue.Name,
                autoAck: sourceOptions.AutoAck,
                consumer: consumer);

            _consumers[source] = (consumerTag, consumer);

            _logger.LogInformation("RabbitMQListener started for source {Source}", source);
        }

        public async Task UnsubscribeAsync(string source, CancellationToken cancellationToken)
        {
            await StopSourceAsync(source);

            _logger.LogInformation("RabbitMQListener stopped for source {Source}", source);
        }

        private async Task StopSourceAsync(string source)
        {
            if (_consumers.TryRemove(source, out var entry))
            {
                if (_channel != null)
                {
                    await _channel.BasicCancelAsync(entry.ConsumerTag);
                }
            }
        }

        private async Task ConsumerReceivedAsync(Func<T, Task<bool>> messageHandler, bool autoAck, object sender, BasicDeliverEventArgs ea)
        {
            string destinationName = !string.IsNullOrEmpty(ea.Exchange) ? ea.Exchange : ea.RoutingKey;

            var parentContext = ExtractTraceContext(ea.BasicProperties);
            using var activity = _activitySource.StartActivity($"process {destinationName}", ActivityKind.Consumer, parentContext);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "rabbitmq");
                activity.SetTag("messaging.operation.name", "process");
                activity.SetTag("messaging.operation.type", "process");
                activity.SetTag("messaging.destination.name", destinationName);
                activity.SetTag("messaging.rabbitmq.routing_key", ea.RoutingKey);
                activity.SetTag("messaging.message.body.size", ea.Body.Length);
                activity.SetTag("messaging.message.id", ea.BasicProperties?.MessageId);
            }

            var body = ea.Body;
            T message = Deserialize(body.ToArray());
            try
            {
                bool success = await messageHandler(message);

                if (!autoAck)
                {
                    if (!success)
                    {
                        await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
                    }
                    await _channel.BasicAckAsync(ea.DeliveryTag, false);
                }
            }
            catch
            {
                if (!autoAck)
                {
                    await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
                }
            }
        }

        private T Deserialize(byte[] array)
        {
            string jsonified = Encoding.UTF8.GetString(array);
            _logger.LogDebug("Listener received a message: {Message}", jsonified);
            T data = JsonSerializer.Deserialize<T>(jsonified);
            return data;
        }

        private RabbitMQListenerSourceOptions Resolve(string source)
        {
            if (_options.Sources == null || !_options.Sources.TryGetValue(source, out RabbitMQListenerSourceOptions sourceOptions))
            {
                throw new KeyNotFoundException(
                    $"No source named '{source}' is configured. Configured sources: " +
                    string.Join(", ", _options.Sources?.Keys ?? Enumerable.Empty<string>()));
            }

            return sourceOptions;
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

        public async ValueTask DisposeAsync()
        {
            if(_channel != null)
            {
                await _channel.DisposeAsync();
            }
            if(_connection != null)
            {
                await _connection.DisposeAsync();
            }
        }
    }
}
