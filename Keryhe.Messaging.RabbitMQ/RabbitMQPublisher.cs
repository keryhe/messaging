using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQPublisher<T> : IMessagePublisher<T>
    {
        private static readonly ActivitySource _activitySource = new ("Keryhe.Messaging.RabbitMQ");
        private readonly RabbitMQPublisherOptions _options;
        private readonly ILogger<RabbitMQPublisher<T>> _logger;

        private readonly ConnectionFactory _factory;
        private IConnection _connection;  

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

        public async Task SendAsync(T message)
        {
            _connection ??= await _factory.CreateConnectionAsync();

            using var channel = await _connection.CreateChannelAsync();
            channel.BasicAcksAsync += (sender, ea) =>
            {
                _logger.LogDebug("Message {DeliveryTag} ACKED by broker.", ea.DeliveryTag);
                return Task.CompletedTask;
            };
            channel.BasicNacksAsync += (sender, ea) =>
            {
                _logger.LogWarning("Message {DeliveryTag} NACKED by broker.", ea.DeliveryTag);
                return Task.CompletedTask;
            };
            channel.BasicReturnAsync += (sender, ea) =>
            {
                _logger.LogWarning("Message returned: {Body} with reply code {ReplyCode} and reply text {ReplyText}.", Encoding.UTF8.GetString(ea.Body.ToArray()), ea.ReplyCode, ea.ReplyText);
                return Task.CompletedTask;
            };

            if (!string.IsNullOrEmpty(_options.Exchange?.Name))
            {
                await channel.ExchangeDeclareAsync(
                    exchange: _options.Exchange.Name,
                    type: _options.Exchange.Type,
                    durable: _options.Exchange.Durable,
                    arguments: null);
            }

            if(!string.IsNullOrEmpty(_options.Queue?.Name))
            {
                await channel.QueueDeclareAsync(
                    queue: _options.Queue.Name,
                    durable: _options.Queue.Durable,
                    exclusive: _options.Queue.Exclusive,
                    autoDelete: _options.Queue.AutoDelete,
                    arguments: null);
            }
            var body = Serialize(message);
            var properties = new BasicProperties
            {
                Persistent = _options.Persistent,
                ContentType = "application/json",
                Headers = new Dictionary<string, object>()
            };
            
            using var activity = _activitySource.StartActivity("RabbitMQ Publish", ActivityKind.Producer);
            InjectTraceContext(properties, activity);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "rabbitmq");
                activity.SetTag("messaging.destination", _options.Exchange?.Name ?? _options.Queue?.Name);
                activity.SetTag("messaging.destination_kind", string.IsNullOrEmpty(_options.Exchange?.Name) ? "queue" : "exchange");
                activity.SetTag("messaging.rabbitmq.routing_key", _options.Queue?.Name);
                activity.SetTag("messaging.message_payload_size_bytes", body.Length);
            }

            await channel.BasicPublishAsync(
                exchange: _options.Exchange.Name,
                routingKey: _options.Queue.Name,
                mandatory: _options.Mandatory,
                basicProperties: properties,
                body: body);
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
    }
}
