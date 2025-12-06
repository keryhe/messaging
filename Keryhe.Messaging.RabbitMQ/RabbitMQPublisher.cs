using Keryhe.Messaging.RabbitMQ.Factory;
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
        private readonly ActivitySource _activitySource = new ("Keryhe.Messaging.RabbitMQ");
        private readonly RabbitMQPublisherOptions _options;
        private readonly IRabbitMQConnection _connection;
        private readonly ILogger<RabbitMQPublisher<T>> _logger;

        public RabbitMQPublisher(RabbitMQPublisherOptions options, IRabbitMQConnection connection, ILogger<RabbitMQPublisher<T>> logger)
        {
            _options = options;
            _connection = connection;
            _logger = logger;
        }

        public RabbitMQPublisher(IOptions<RabbitMQPublisherOptions> options, IRabbitMQConnection connection, ILogger<RabbitMQPublisher<T>> logger)
            :this(options.Value, connection, logger)
        {
        }

        public async Task SendAsync(T message)
        {
            using var activity = _activitySource.StartActivity("RabbitMQ Publish", ActivityKind.Producer);

            var connection = await _connection.GetConnectionAsync();
            using var channel = await connection.CreateChannelAsync();

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

            InjectTraceContext(properties, activity);
            // Add useful tags to the activity
            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.destination", _options.Exchange?.Name ?? _options.Queue?.Name);
            activity.SetTag("messaging.destination_kind", string.IsNullOrEmpty(_options.Exchange?.Name) ? "queue" : "exchange");
            activity.SetTag("messaging.rabbitmq.routing_key", _options.Queue?.Name);
            activity.SetTag("messaging.message_payload_size_bytes", body.Length);

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
