using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Keryhe.Messaging.Azure
{
    public class ServiceBusPublisher<T> : IMessagePublisher<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.Azure");
        private readonly IOptionsMonitor<ServiceBusPublisherOptions> _optionsMonitor;
        private readonly IDisposable _changeToken;

        private ServiceBusPublisherOptions _options;
        private readonly ILogger<ServiceBusPublisher<T>> _logger;
        private ServiceBusClient _client;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        // Lazy, because ConcurrentDictionary.GetOrAdd may run its factory on several threads at
        // once: only the winning Lazy is ever evaluated, so the losers cannot leak a live sender.
        private readonly ConcurrentDictionary<string, Lazy<ServiceBusSender>> _senders = new();
        private int _disposed;

        public ServiceBusPublisher(ServiceBusPublisherOptions options, ILogger<ServiceBusPublisher<T>> logger)
        {
            _options = options;
            _logger = logger;

            _client = new ServiceBusClient(_options.ConnectionString);
        }

        public ServiceBusPublisher(IOptions<ServiceBusPublisherOptions> options, ILogger<ServiceBusPublisher<T>> logger)
            : this(options.Value, logger)
        {
        }

        public ServiceBusPublisher(IOptionsMonitor<ServiceBusPublisherOptions> options, ILogger<ServiceBusPublisher<T>> logger)
            : this(options.CurrentValue, logger)
        {
            _optionsMonitor = options;

            _changeToken = _optionsMonitor.OnChange(updated =>
            {
                _ = ResetConnectionAsync(updated);
            });
        }

        public async Task SendAsync(T message, string destination)
        {
            string name = Resolve(destination);
            ServiceBusSender sender = GetSender(destination, name);

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

            try
            {
                await sender.SendMessageAsync(sbMessage);
            }
            catch (ObjectDisposedException)
            {
                // A concurrent options change disposed the client this sender belongs to. Drop the
                // stale sender and retry once against the new one.
                _logger.LogWarning("ServiceBusPublisher sender for destination {Destination} was disposed by a connection reset; retrying once", destination);

                _senders.TryRemove(destination, out _);

                await GetSender(destination, name).SendMessageAsync(sbMessage);
            }
        }

        private ServiceBusSender GetSender(string destination, string name)
        {
            return _senders.GetOrAdd(destination, _ => new Lazy<ServiceBusSender>(() => _client.CreateSender(name))).Value;
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
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _changeToken?.Dispose();

            foreach (Lazy<ServiceBusSender> sender in _senders.Values)
            {
                if (sender.IsValueCreated)
                {
                    await sender.Value.DisposeAsync();
                }
            }

            await _client.DisposeAsync();
            _connectionLock.Dispose();
        }

        private async Task ResetConnectionAsync(ServiceBusPublisherOptions updated)
        {
            // OnChange fires the callback fire-and-forget; a change racing DisposeAsync would
            // otherwise operate on a lock disposal is concurrently tearing down.
            if (Volatile.Read(ref _disposed) == 1)
            {
                return;
            }

            try
            {
                ServiceBusClient oldClient;
                List<Lazy<ServiceBusSender>> oldSenders;

                await _connectionLock.WaitAsync();
                try
                {
                    Interlocked.Exchange(ref _options, updated);

                    oldSenders = _senders.Values.ToList();
                    _senders.Clear();

                    oldClient = _client;
                    _client = new ServiceBusClient(updated.ConnectionString);
                }
                finally
                {
                    _connectionLock.Release();
                }

                foreach (var sender in oldSenders)
                {
                    if (sender.IsValueCreated)
                    {
                        await sender.Value.DisposeAsync();
                    }
                }

                await oldClient.DisposeAsync();

                _logger.LogInformation("ServiceBusPublisher reset connection due to options change");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset ServiceBusPublisher connection after options change");
            }
        }

        private string Serialize(T data)
        {
            return JsonSerializer.Serialize<T>(data);

        }
    }
}
