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
        private readonly SQSListenerOptions _options;
        private readonly ILogger<SQSListener<T>> _logger;
        private AmazonSQSClient _sqsClient;
        private readonly ConcurrentDictionary<string, Func<T, Task<bool>>> _handlers = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();

        public SQSListener(SQSListenerOptions options, ILogger<SQSListener<T>> logger)
        {
            _options = options;
            _logger = logger;

            AmazonSQSConfig sqsConfig = new AmazonSQSConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_options.Region)
            };
            var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
            _sqsClient = new AmazonSQSClient(awsCredentials, sqsConfig);
        }

        public SQSListener(IOptions<SQSListenerOptions> options, ILogger<SQSListener<T>> logger)
            : this(options.Value, logger)
        {
        }

        public Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            SQSSourceOptions sourceOptions = Resolve(source);

            _handlers[source] = messageHandler;

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellations[source] = linkedCts;

            Task.Run(() => Run(source, sourceOptions, linkedCts.Token), linkedCts.Token);

            _logger.LogDebug("SqsListener started for source {Source}", source);
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(string source, CancellationToken cancellationToken)
        {
            StopSource(source);

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
            foreach (string source in _cancellations.Keys.ToList())
            {
                StopSource(source);
            }

            _sqsClient.Dispose();

            return ValueTask.CompletedTask;
        }

        private async Task Run(string source, SQSSourceOptions sourceOptions, CancellationToken cancellationToken)
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
                catch (OperationCanceledException)
                {
                    break;
                }

                if(response.HttpStatusCode == HttpStatusCode.OK && response.Messages != null)
                {
                    foreach(var m in response.Messages)
                    {
                        bool success = await ProcessMessageAsync(source, sourceOptions, m);

                        if (success)
                        {
                            await _sqsClient.DeleteMessageAsync(sourceOptions.QueueUrl, m.ReceiptHandle);
                        }
                    }
                }
            }
        }

        private async Task<bool> ProcessMessageAsync(string source, SQSSourceOptions sourceOptions, Message m)
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
            return await _handlers[source](message);
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
                    var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
                    var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
                    var traceFlags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

                    string traceState = null;
                    if (attributes.TryGetValue("tracestate", out MessageAttributeValue tracestateAttribute))
                    {
                        traceState = tracestateAttribute?.StringValue;
                    }

                    return new ActivityContext(traceId, spanId, traceFlags, traceState);
                }
            }

            return default;
        }

        private T Deserialize(string message)
        {
            T data = JsonSerializer.Deserialize<T>(message);
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
