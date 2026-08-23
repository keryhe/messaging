using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Keryhe.Messaging.AWS
{
    public class SQSListener<T> : IMessageListener<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.AWS");
        private static readonly TimeSpan ReceiveErrorBackoff = TimeSpan.FromSeconds(5);

        private readonly IOptionsMonitor<SQSListenerOptions> _optionsMonitor;
        private readonly IDisposable _changeToken;

        private SQSListenerOptions _options;
        private readonly ILogger<SQSListener<T>> _logger;
        private AmazonSQSClient _sqsClient;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private readonly ConcurrentDictionary<string, (Func<T, Task<bool>> Handler, CancellationToken CancellationToken)> _handlers = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();
        private int _disposed;

        // Serializes subscribe against unsubscribe. Stopping the previous loop first only avoids a
        // leak if no second subscribe can interleave between the stop and the dictionary write.
        private readonly object _subscribeGate = new();

        public SQSListener(SQSListenerOptions options, ILogger<SQSListener<T>> logger)
        {
            _options = options;
            _logger = logger;

            _sqsClient = BuildClient(_options);
        }

        public SQSListener(IOptions<SQSListenerOptions> options, ILogger<SQSListener<T>> logger)
            : this(options.Value, logger)
        {
        }

        public SQSListener(IOptionsMonitor<SQSListenerOptions> options, ILogger<SQSListener<T>> logger)
            : this(options.CurrentValue, logger)
        {
            _optionsMonitor = options;

            _changeToken = _optionsMonitor.OnChange(updated =>
            {
                _ = ResetConnectionAsync(updated);
            });
        }

        private static AmazonSQSClient BuildClient(SQSListenerOptions options)
        {
            // Matches SQSPublisher: with no static credentials configured, fall back to the ambient
            // credential chain (instance role, environment, profile) rather than authenticating
            // with empty keys.
            if (string.IsNullOrEmpty(options.Region) && string.IsNullOrEmpty(options.AccessKey))
            {
                return new AmazonSQSClient();
            }

            AmazonSQSConfig sqsConfig = new AmazonSQSConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region)
            };
            var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(options.AccessKey, options.SecretKey);
            return new AmazonSQSClient(awsCredentials, sqsConfig);
        }

        public Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            SQSSourceOptions sourceOptions = Resolve(source);

            // Fail here with something readable rather than on every receive with an opaque
            // InvalidParameterValue from SQS.
            if (sourceOptions.MaxNumberOfMessages < 1 || sourceOptions.MaxNumberOfMessages > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceOptions.MaxNumberOfMessages),
                    $"Source '{source}' sets MaxNumberOfMessages to {sourceOptions.MaxNumberOfMessages}. SQS allows 1 to 10.");
            }

            if (sourceOptions.WaitTimeSeconds < 0 || sourceOptions.WaitTimeSeconds > 20)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceOptions.WaitTimeSeconds),
                    $"Source '{source}' sets WaitTimeSeconds to {sourceOptions.WaitTimeSeconds}. SQS allows 0 to 20.");
            }

            lock (_subscribeGate)
            {
                // Subscribing a source twice would otherwise leave the first loop running and
                // unreachable, receiving from the same queue at double the API cost.
                StopSource(source);

                _handlers[source] = (messageHandler, cancellationToken);

                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _cancellations[source] = linkedCts;

                Task.Run(() => Run(source, sourceOptions, messageHandler, linkedCts.Token), linkedCts.Token)
                    .ContinueWith(
                        t => _logger.LogError(t.Exception, "SqsListener receive loop for source {Source} faulted and has stopped", source),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
            }

            _logger.LogDebug("SqsListener started for source {Source}", source);
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(string source, CancellationToken cancellationToken)
        {
            lock (_subscribeGate)
            {
                StopSource(source);
            }

            _logger.LogDebug("SqsListener stopped for source {Source}", source);
            return Task.CompletedTask;
        }

        private void StopSource(string source)
        {
            if (_cancellations.TryRemove(source, out CancellationTokenSource cts))
            {
                cts.Cancel();
                cts.Dispose();
            }

            _handlers.TryRemove(source, out _);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return ValueTask.CompletedTask;
            }

            _changeToken?.Dispose();

            lock (_subscribeGate)
            {
                foreach (string source in _cancellations.Keys.ToList())
                {
                    StopSource(source);
                }
            }

            _sqsClient.Dispose();
            _connectionLock.Dispose();

            return ValueTask.CompletedTask;
        }

        private async Task ResetConnectionAsync(SQSListenerOptions updated)
        {
            // OnChange fires the callback fire-and-forget; a change racing DisposeAsync would
            // otherwise operate on a lock disposal is concurrently tearing down.
            if (Volatile.Read(ref _disposed) == 1)
            {
                return;
            }

            try
            {
                AmazonSQSClient oldClient;
                List<(string Source, Func<T, Task<bool>> MessageHandler, CancellationToken CancellationToken)> existingSources;

                await _connectionLock.WaitAsync();
                try
                {
                    Interlocked.Exchange(ref _options, updated);

                    oldClient = _sqsClient;
                    _sqsClient = BuildClient(updated);

                    existingSources = _cancellations.Keys
                        .Select(source => (Source: source, Found: _handlers.TryGetValue(source, out var entry), Entry: entry))
                        .Where(x => x.Found)
                        .Select(x => (x.Source, x.Entry.Handler, x.Entry.CancellationToken))
                        .ToList();

                    lock (_subscribeGate)
                    {
                        foreach (var entry in existingSources)
                        {
                            StopSource(entry.Source);
                        }
                    }
                }
                finally
                {
                    _connectionLock.Release();
                }

                oldClient.Dispose();

                foreach (var entry in existingSources)
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

                _logger.LogInformation("SqsListener reset connection due to options change");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset SqsListener connection after options change");
            }
        }

        private async Task Run(string source, SQSSourceOptions sourceOptions, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReceiveMessageResponse response;

                try
                {
                    response = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
                    {
                        QueueUrl = sourceOptions.QueueUrl,
                        MaxNumberOfMessages = sourceOptions.MaxNumberOfMessages,
                        WaitTimeSeconds = sourceOptions.WaitTimeSeconds,
                        MessageAttributeNames = new List<string> { "traceparent", "tracestate" }
                    }, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Throttling, network faults and service errors are transient: back off and
                    // keep receiving rather than ending the loop for the life of the process.
                    _logger.LogError(ex, "SqsListener failed to receive messages for source {Source}; retrying after {Delay}", source, ReceiveErrorBackoff);

                    try
                    {
                        await Task.Delay(ReceiveErrorBackoff, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }

                if(response.HttpStatusCode == HttpStatusCode.OK && response.Messages != null)
                {
                    foreach(var m in response.Messages)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        try
                        {
                            bool success = await ProcessMessageAsync(sourceOptions, messageHandler, m);

                            if (success)
                            {
                                await _sqsClient.DeleteMessageAsync(sourceOptions.QueueUrl, m.ReceiptHandle, cancellationToken);
                            }
                            else
                            {
                                // Without this the message is simply left to time out, so
                                // redelivery is silently delayed by the queue's visibility timeout.
                                // Zero makes it immediate — the SQS equivalent of a nack-with-requeue.
                                _logger.LogWarning("SqsListener handler returned false for message {MessageId} from source {Source}; resetting visibility for immediate redelivery", m.MessageId, source);

                                await _sqsClient.ChangeMessageVisibilityAsync(sourceOptions.QueueUrl, m.ReceiptHandle, 0, cancellationToken);
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            // Leave the message undeleted so the visibility timeout redelivers it.
                            _logger.LogError(ex, "SqsListener failed to process message {MessageId} from source {Source}; leaving it for redelivery", m.MessageId, source);
                        }
                    }
                }
            }
        }

        private async Task<bool> ProcessMessageAsync(SQSSourceOptions sourceOptions, Func<T, Task<bool>> messageHandler, Message m)
        {
            var parentContext = ExtractTraceContext(m.MessageAttributes);
            using var activity = _activitySource.StartActivity($"process {sourceOptions.QueueUrl}", ActivityKind.Consumer, parentContext);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "aws_sqs");
                activity.SetTag("messaging.operation.name", "process");
                activity.SetTag("messaging.operation.type", "process");
                activity.SetTag("messaging.destination.name", sourceOptions.QueueUrl);
                activity.SetTag("messaging.message.body.size", Encoding.UTF8.GetByteCount(m.Body));
                activity.SetTag("messaging.message.id", m.MessageId);
            }

            T message = Deserialize(m.Body);

            // The handler is captured in SubscribeAsync and passed through, so an unsubscribe
            // during a message's flight can no longer throw KeyNotFoundException here.
            return await messageHandler(message);
        }

        private ActivityContext ExtractTraceContext(Dictionary<string, MessageAttributeValue> attributes)
        {
            if (attributes == null)
                return default;

            if (attributes.TryGetValue("traceparent", out MessageAttributeValue traceparentAttribute)
                && !string.IsNullOrEmpty(traceparentAttribute?.StringValue))
            {
                var parts = traceparentAttribute.StringValue.Split('-');
                if (parts.Length == 4)
                {
                    string traceState = null;
                    if (attributes.TryGetValue("tracestate", out MessageAttributeValue tracestateAttribute))
                    {
                        traceState = tracestateAttribute?.StringValue;
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
                        _logger.LogWarning("SqsListener could not parse the traceparent attribute '{TraceParent}'; processing the message without a parent trace context", traceparentAttribute.StringValue);
                    }
                }
            }

            return default;
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

        private SQSSourceOptions Resolve(string source)
        {
            if (_options.Sources == null || !_options.Sources.TryGetValue(source, out SQSSourceOptions sourceOptions))
            {
                throw new KeyNotFoundException(
                    $"No source named '{source}' is configured. Configured sources: " +
                    string.Join(", ", _options.Sources?.Keys ?? Enumerable.Empty<string>()));
            }

            return sourceOptions;
        }
    }
}
