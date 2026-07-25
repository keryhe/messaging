using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Keryhe.Messaging.Azure
{
    public class ServiceBusListener<T> : IMessageListener<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.Azure");
        private readonly ServiceBusListenerOptions _options;
        private readonly ILogger<ServiceBusListener<T>> _logger;
        private ServiceBusClient _client;
        private readonly ConcurrentDictionary<string, ServiceBusProcessor> _processors = new();

        public ServiceBusListener(ServiceBusListenerOptions options, ILogger<ServiceBusListener<T>> logger)
        {
            _options = options;
            _logger = logger;

            _client = new ServiceBusClient(_options.ConnectionString);
        }

        public ServiceBusListener(IOptions<ServiceBusListenerOptions> options, ILogger<ServiceBusListener<T>> logger)
            : this(options.Value, logger)
        {
        }

        public async Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            ServiceBusSourceOptions sourceOptions = Resolve(source);
            string destinationName = string.IsNullOrEmpty(sourceOptions.QueueName) ? sourceOptions.TopicName : sourceOptions.QueueName;

            ServiceBusProcessor processor = string.IsNullOrEmpty(sourceOptions.QueueName)
                ? _client.CreateProcessor(sourceOptions.TopicName, sourceOptions.SubscriptionName)
                : _client.CreateProcessor(sourceOptions.QueueName);

            processor.ProcessMessageAsync += args => ProcessMessageAsync(messageHandler, destinationName, args);
            processor.ProcessErrorAsync += ProcessErrorAsync;

            await processor.StartProcessingAsync(cancellationToken);

            _processors[source] = processor;

            _logger.LogDebug("ServiceBusListener started for source {Source}", source);
        }

        public async Task UnsubscribeAsync(string source, CancellationToken cancellationToken)
        {
            await StopSourceAsync(source);

            _logger.LogDebug("ServiceBusListener stopped for source {Source}", source);
        }

        private async Task StopSourceAsync(string source)
        {
            if (_processors.TryRemove(source, out ServiceBusProcessor processor))
            {
                await processor.StopProcessingAsync();
                await processor.DisposeAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (string source in _processors.Keys.ToList())
            {
                await StopSourceAsync(source);
            }

            await _client.DisposeAsync();
        }

        private async Task ProcessMessageAsync(Func<T, Task<bool>> messageHandler, string destinationName, ProcessMessageEventArgs args)
        {
            var parentContext = ExtractTraceContext(args.Message.ApplicationProperties);
            using var activity = _activitySource.StartActivity($"process {destinationName}", ActivityKind.Consumer, parentContext);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "servicebus");
                activity.SetTag("messaging.operation.name", "process");
                activity.SetTag("messaging.operation.type", "process");
                activity.SetTag("messaging.destination.name", destinationName);
                activity.SetTag("messaging.message.body.size", args.Message.Body.ToMemory().Length);
                activity.SetTag("messaging.message.id", args.Message.MessageId);
            }

            T message = Deserialize(args.Message.Body.ToString());
            bool success = await messageHandler(message);

            if (success)
            {
                await args.CompleteMessageAsync(args.Message);
            }
        }

        private ActivityContext ExtractTraceContext(IReadOnlyDictionary<string, object> applicationProperties)
        {
            if (applicationProperties == null)
                return default;

            if (applicationProperties.TryGetValue("traceparent", out object traceparentObj)
                && traceparentObj is string traceparent
                && !string.IsNullOrEmpty(traceparent))
            {
                var parts = traceparent.Split('-');
                if (parts.Length == 4)
                {
                    var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
                    var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
                    var traceFlags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

                    string traceState = null;
                    if (applicationProperties.TryGetValue("tracestate", out object tracestateObj))
                    {
                        traceState = tracestateObj as string;
                    }

                    return new ActivityContext(traceId, spanId, traceFlags, traceState);
                }
            }

            return default;
        }

        private Task ProcessErrorAsync(ProcessErrorEventArgs args)
        {
            _logger.LogError(args.Exception.Message);
            return Task.CompletedTask;
        }

        private T Deserialize(string message)
        {
            T data = JsonSerializer.Deserialize<T>(message);
            return data;
        }

        private ServiceBusSourceOptions Resolve(string source)
        {
            if (_options.Sources == null || !_options.Sources.TryGetValue(source, out ServiceBusSourceOptions sourceOptions))
            {
                throw new KeyNotFoundException(
                    $"No source named '{source}' is configured. Configured sources: " +
                    string.Join(", ", _options.Sources?.Keys ?? Enumerable.Empty<string>()));
            }

            return sourceOptions;
        }
    }
}
