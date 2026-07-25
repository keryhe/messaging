using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQPublisher<T> : IMessagePublisher<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new ("Keryhe.Messaging.RabbitMQ");
        private readonly RabbitMQPublisherOptions _options;
        private readonly ILogger<RabbitMQPublisher<T>> _logger;

        private readonly ConnectionFactory _factory;
        private readonly ConcurrentDictionary<string, bool> _declared = new();
        private IConnection _connection;
        private IChannel _channel;

        public RabbitMQPublisher(IRabbitMQPublisherOptionsProvider optionsProvider, ILogger<RabbitMQPublisher<T>> logger)
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

        public RabbitMQPublisher(IOptions<RabbitMQPublisherOptions> options, ILogger<RabbitMQPublisher<T>> logger)
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

        public async Task SendAsync(T message, string destination)
        {
            RabbitMQDestinationOptions destinationOptions = Resolve(destination);

            _connection ??= await _factory.CreateConnectionAsync();

            if (_channel == null)
            {
                _channel = await _connection.CreateChannelAsync();

                _channel.BasicAcksAsync += (sender, ea) =>
                {
                    _logger.LogDebug("Message {DeliveryTag} ACKED by broker.", ea.DeliveryTag);
                    return Task.CompletedTask;
                };
                _channel.BasicNacksAsync += (sender, ea) =>
                {
                    _logger.LogWarning("Message {DeliveryTag} NACKED by broker.", ea.DeliveryTag);
                    return Task.CompletedTask;
                };
                _channel.BasicReturnAsync += (sender, ea) =>
                {
                    _logger.LogWarning("Message returned: {Body} with reply code {ReplyCode} and reply text {ReplyText}.", Encoding.UTF8.GetString(ea.Body.ToArray()), ea.ReplyCode, ea.ReplyText);
                    return Task.CompletedTask;
                };
            }

            if (_declared.TryAdd(destination, true))
            {
                if (!string.IsNullOrEmpty(destinationOptions.Exchange?.Name))
                {
                    await _channel.ExchangeDeclareAsync(
                        exchange: destinationOptions.Exchange.Name,
                        type: destinationOptions.Exchange.Type,
                        durable: destinationOptions.Exchange.Durable,
                        arguments: null);
                }

                if (!string.IsNullOrEmpty(destinationOptions.Queue?.Name))
                {
                    await _channel.QueueDeclareAsync(
                        queue: destinationOptions.Queue.Name,
                        durable: destinationOptions.Queue.Durable,
                        exclusive: destinationOptions.Queue.Exclusive,
                        autoDelete: destinationOptions.Queue.AutoDelete,
                        arguments: null);
                }
            }

            string routingKey = !string.IsNullOrEmpty(destinationOptions.Exchange?.RoutingKey)
                ? destinationOptions.Exchange.RoutingKey
                : destinationOptions.Queue?.Name;
            string destinationName = destinationOptions.Exchange?.Name ?? destinationOptions.Queue?.Name;

            var body = Serialize(message);
            var properties = new BasicProperties
            {
                Persistent = _options.Persistent,
                ContentType = "application/json",
                Headers = new Dictionary<string, object>(),
                MessageId = Guid.NewGuid().ToString()
            };

            using var activity = _activitySource.StartActivity($"send {destinationName}", ActivityKind.Producer);
            InjectTraceContext(properties, activity);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "rabbitmq");
                activity.SetTag("messaging.operation.name", "send");
                activity.SetTag("messaging.operation.type", "send");
                activity.SetTag("messaging.destination.name", destinationName);
                activity.SetTag("messaging.rabbitmq.destination_kind", string.IsNullOrEmpty(destinationOptions.Exchange?.Name) ? "queue" : "exchange");
                activity.SetTag("messaging.rabbitmq.routing_key", routingKey);
                activity.SetTag("messaging.message.body.size", body.Length);
                activity.SetTag("messaging.message.id", properties.MessageId);
            }

            await _channel.BasicPublishAsync(
                exchange: destinationOptions.Exchange.Name,
                routingKey: routingKey,
                mandatory: _options.Mandatory,
                basicProperties: properties,
                body: body);
        }

        private RabbitMQDestinationOptions Resolve(string destination)
        {
            if (_options.Destinations == null || !_options.Destinations.TryGetValue(destination, out RabbitMQDestinationOptions destinationOptions))
            {
                throw new KeyNotFoundException(
                    $"No destination named '{destination}' is configured. Configured destinations: " +
                    string.Join(", ", _options.Destinations?.Keys ?? Enumerable.Empty<string>()));
            }

            return destinationOptions;
        }

        private byte[] Serialize(T data)
        {
            string jsonified = JsonSerializer.Serialize<T>(data);
            _logger.LogDebug("Publisher sent a message: {Message}", jsonified);
            byte[] databuffer = Encoding.UTF8.GetBytes(jsonified);
            return databuffer;
        }

        private void InjectTraceContext(BasicProperties properties, Activity activity)
        {
            if (activity == null)
                return;

            // Inject W3C Trace Context headers (traceparent and tracestate)
            properties.Headers ??= new Dictionary<string, object>();
            
            // traceparent format: version-trace_id-span_id-flags
            var traceparent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
            properties.Headers["traceparent"] = traceparent;

            // Include tracestate if present
            if (!string.IsNullOrEmpty(activity.TraceStateString))
            {
                properties.Headers["tracestate"] = activity.TraceStateString;
            }

            // Optional: Add correlation ID for easier debugging
            properties.CorrelationId = activity.TraceId.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            if(_channel != null)
            {
                await _channel.CloseAsync();
                await _channel.DisposeAsync();
            }

            if(_connection != null)
            {
                await _connection.CloseAsync();
                await _connection.DisposeAsync();
            }
        }
    }
}
