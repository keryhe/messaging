using Azure.Messaging.ServiceBus;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Keryhe.Messaging.Azure
{
    public class ServiceBusPublisher<T> : IMessagePublisher<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.Azure");
        private readonly ServiceBusPublisherOptions _options;
        private readonly ServiceBusClient _client;
        private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

        public ServiceBusPublisher(ServiceBusPublisherOptions options)
        {
            _options = options;

            _client = new ServiceBusClient(_options.ConnectionString);
        }

        public async Task SendAsync(T message, string destination)
        {
            string name = Resolve(destination);
            ServiceBusSender sender = _senders.GetOrAdd(destination, _ => _client.CreateSender(name));

            var body = Serialize(message);
            var sbMessage = new ServiceBusMessage(body)
            {
                MessageId = Guid.NewGuid().ToString()
            };

            using var activity = _activitySource.StartActivity($"send {name}", ActivityKind.Producer);
            InjectTraceContext(sbMessage.ApplicationProperties, activity);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "servicebus");
                activity.SetTag("messaging.operation.name", "send");
                activity.SetTag("messaging.operation.type", "send");
                activity.SetTag("messaging.destination.name", name);
                activity.SetTag("messaging.message.body.size", Encoding.UTF8.GetByteCount(body));
                activity.SetTag("messaging.message.id", sbMessage.MessageId);
            }

            await sender.SendMessageAsync(sbMessage);
        }

        private void InjectTraceContext(IDictionary<string, object> applicationProperties, Activity activity)
        {
            if (activity == null)
                return;

            var traceparent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
            applicationProperties["traceparent"] = traceparent;

            if (!string.IsNullOrEmpty(activity.TraceStateString))
            {
                applicationProperties["tracestate"] = activity.TraceStateString;
            }
        }

        private string Resolve(string destination)
        {
            if (_options.Destinations == null || !_options.Destinations.TryGetValue(destination, out string name))
            {
                throw new KeyNotFoundException(
                    $"No destination named '{destination}' is configured. Configured destinations: " +
                    string.Join(", ", _options.Destinations?.Keys ?? Enumerable.Empty<string>()));
            }

            return name;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (ServiceBusSender sender in _senders.Values)
            {
                await sender.DisposeAsync();
            }

            await _client.DisposeAsync();
        }

        private string Serialize(T data)
        {
            return JsonSerializer.Serialize<T>(data);

        }
    }
}
