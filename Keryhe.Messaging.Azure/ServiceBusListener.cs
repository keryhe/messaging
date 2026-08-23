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
        private readonly IOptionsMonitor<ServiceBusListenerOptions> _optionsMonitor;
        private readonly IDisposable _changeToken;

        private ServiceBusListenerOptions _options;
        private readonly ILogger<ServiceBusListener<T>> _logger;
        private ServiceBusClient _client;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        // Serializes subscribe/unsubscribe. Stopping the previous processor first only prevents a
        // leak if no second subscribe can interleave between the stop and the dictionary write.
        private readonly SemaphoreSlim _subscribeLock = new(1, 1);
        private readonly ConcurrentDictionary<string, SourceSubscription> _processors = new();
        private int _disposed;

        private sealed class SourceSubscription
        {
            public ServiceBusProcessor Processor { get; init; }
            public Func<T, Task<bool>> MessageHandler { get; init; }
            public CancellationToken CancellationToken { get; init; }
            public CancellationTokenRegistration Registration { get; set; }
        }

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

        public ServiceBusListener(IOptionsMonitor<ServiceBusListenerOptions> options, ILogger<ServiceBusListener<T>> logger)
            : this(options.CurrentValue, logger)
        {
            _optionsMonitor = options;

            _changeToken = _optionsMonitor.OnChange(updated =>
            {
                _ = ResetConnectionAsync(updated);
            });
        }

        public async Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            await _subscribeLock.WaitAsync();
            try
            {
                await SubscribeCoreAsync(source, messageHandler, cancellationToken);
            }
            finally
            {
                _subscribeLock.Release();
            }
        }

        private async Task SubscribeCoreAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            ServiceBusSourceOptions sourceOptions = Resolve(source);
            string destinationName = string.IsNullOrEmpty(sourceOptions.QueueName) ? sourceOptions.TopicName : sourceOptions.QueueName;

            // Subscribing a source twice would otherwise leave the previous processor running and
            // unreachable, with both consuming from the same entity.
            await StopSourceCoreAsync(source);

            // AutoCompleteMessages defaults to true, which completes — that is, deletes — any
            // message whose handler returned without throwing, including one that returned false.
            var processorOptions = new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false
            };

            // Read the client under the connection lock: a concurrent options change disposes the
            // one a plain field read would have handed us.
            ServiceBusProcessor processor;
            await _connectionLock.WaitAsync();
            try
            {
                processor = string.IsNullOrEmpty(sourceOptions.QueueName)
                    ? _client.CreateProcessor(sourceOptions.TopicName, sourceOptions.SubscriptionName, processorOptions)
                    : _client.CreateProcessor(sourceOptions.QueueName, processorOptions);
            }
            finally
            {
                _connectionLock.Release();
            }

            processor.ProcessMessageAsync += args => ProcessMessageAsync(messageHandler, destinationName, args);
            processor.ProcessErrorAsync += ProcessErrorAsync;

            try
            {
                await processor.StartProcessingAsync(cancellationToken);
            }
            catch
            {
                // Not in _processors yet, so nothing else will ever dispose this one — an entity
                // that doesn't exist, or an auth failure, would otherwise leak it on every failed
                // subscribe.
                await processor.DisposeAsync();
                throw;
            }

            var subscription = new SourceSubscription
            {
                Processor = processor,
                MessageHandler = messageHandler,
                CancellationToken = cancellationToken
            };

            _processors[source] = subscription;

            // The token previously only bounded the start operation, so cancelling it never stopped
            // the processor. Register after the entry exists so an already-cancelled token cannot
            // fire before there is anything to stop.
            subscription.Registration = cancellationToken.Register(() => _ = StopSourceOnCancellationAsync(source));

            if (cancellationToken.IsCancellationRequested)
            {
                await StopSourceCoreAsync(source);
                return;
            }

            _logger.LogDebug("ServiceBusListener started for source {Source}", source);
        }

        private async Task StopSourceOnCancellationAsync(string source)
        {
            try
            {
                await StopSourceAsync(source);

                _logger.LogInformation("ServiceBusListener stopped for source {Source}: its subscription token was cancelled", source);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop source {Source} after its subscription token was cancelled", source);
            }
        }

        public async Task UnsubscribeAsync(string source, CancellationToken cancellationToken)
        {
            await StopSourceAsync(source);

            _logger.LogDebug("ServiceBusListener stopped for source {Source}", source);
        }

        private async Task StopSourceAsync(string source)
        {
            await _subscribeLock.WaitAsync();
            try
            {
                await StopSourceCoreAsync(source);
            }
            finally
            {
                _subscribeLock.Release();
            }
        }

        /// <summary>Callers already holding <see cref="_subscribeLock"/> use this.</summary>
        private async Task StopSourceCoreAsync(string source)
        {
            if (_processors.TryRemove(source, out SourceSubscription subscription))
            {
                subscription.Registration.Dispose();

                await subscription.Processor.StopProcessingAsync();
                await subscription.Processor.DisposeAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _changeToken?.Dispose();

            foreach (string source in _processors.Keys.ToList())
            {
                await StopSourceAsync(source);
            }

            await _client.DisposeAsync();
            _connectionLock.Dispose();
            _subscribeLock.Dispose();
        }

        private async Task ResetConnectionAsync(ServiceBusListenerOptions updated)
        {
            // OnChange fires the callback fire-and-forget; a change racing DisposeAsync would
            // otherwise operate on locks and dictionaries that disposal is concurrently tearing down.
            if (Volatile.Read(ref _disposed) == 1)
            {
                return;
            }

            try
            {
                ServiceBusClient oldClient;
                List<(string Source, Func<T, Task<bool>> MessageHandler, CancellationToken CancellationToken)> existingEntries;

                await _connectionLock.WaitAsync();
                try
                {
                    Interlocked.Exchange(ref _options, updated);

                    existingEntries = _processors
                        .Select(kvp => (kvp.Key, kvp.Value.MessageHandler, kvp.Value.CancellationToken))
                        .ToList();

                    oldClient = _client;
                    _client = new ServiceBusClient(updated.ConnectionString);
                }
                finally
                {
                    _connectionLock.Release();
                }

                // Processors belong to the old client, so they have to be stopped and disposed —
                // along with their token registrations — before it is disposed.
                foreach (var entry in existingEntries)
                {
                    await StopSourceAsync(entry.Source);
                }

                await oldClient.DisposeAsync();

                foreach (var entry in existingEntries)
                {
                    try
                    {
                        // Carry the caller's original token across, or cancelling it would stop
                        // having any effect after the first options change.
                        await SubscribeAsync(entry.Source, entry.MessageHandler, entry.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to resubscribe source {Source} after options change", entry.Source);
                    }
                }

                _logger.LogInformation("ServiceBusListener reset connection due to options change");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset ServiceBusListener connection after options change");
            }
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
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            }
            else
            {
                // Abandon releases the lock for immediate redelivery and increments DeliveryCount,
                // so Service Bus's own MaxDeliveryCount eventually dead-letters it. This is the
                // closest match to RabbitMQ's nack and SQS's visibility reset.
                _logger.LogWarning("ServiceBusListener handler returned false for message {MessageId} from {Destination}; abandoning it for redelivery",
                    args.Message.MessageId, destinationName);

                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
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
                    string traceState = null;
                    if (applicationProperties.TryGetValue("tracestate", out object tracestateObj))
                    {
                        traceState = tracestateObj as string;
                    }

                    try
                    {
                        var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
                        var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
                        var traceFlags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

                        return new ActivityContext(traceId, spanId, traceFlags, traceState);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // A malformed header must not cost us the message.
                        _logger.LogWarning("ServiceBusListener could not parse the traceparent property '{TraceParent}'; processing the message without a parent trace context", traceparent);
                    }
                }
            }

            return default;
        }

        private Task ProcessErrorAsync(ProcessErrorEventArgs args)
        {
            // Log the exception itself rather than passing its message as a log template — an
            // exception message containing braces would otherwise be parsed as placeholders.
            _logger.LogError(args.Exception, "ServiceBusListener error from {ErrorSource} on entity {EntityPath}", args.ErrorSource, args.EntityPath);
            return Task.CompletedTask;
        }

        private T Deserialize(string message)
        {
            T data = JsonSerializer.Deserialize<T>(message);

            // A "null" payload is not a message. Handing null to the handler pushes the failure
            // into caller code that has no way to settle the message.
            if (data == null)
            {
                throw new InvalidOperationException("The message payload deserialized to null.");
            }

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
