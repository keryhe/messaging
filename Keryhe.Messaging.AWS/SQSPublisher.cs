using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Keryhe.Messaging.AWS
{
    public class SQSPublisher<T> : IMessagePublisher<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.AWS");
        private readonly IOptionsMonitor<SQSPublisherOptions> _optionsMonitor;
        private readonly IDisposable _changeToken;

        private SQSPublisherOptions _options;
        private readonly ILogger<SQSPublisher<T>> _logger;
        private AmazonSQSClient _sqsClient;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private int _disposed;

        public SQSPublisher(SQSPublisherOptions options, ILogger<SQSPublisher<T>> logger)
        {
            _options = options;
            _logger = logger;
            _sqsClient = BuildClient(_options);
        }

        public SQSPublisher(IOptions<SQSPublisherOptions> options, ILogger<SQSPublisher<T>> logger)
            : this(options.Value, logger)
        {
        }

        public SQSPublisher(IOptionsMonitor<SQSPublisherOptions> options, ILogger<SQSPublisher<T>> logger)
            : this(options.CurrentValue, logger)
        {
            _optionsMonitor = options;

            _changeToken = _optionsMonitor.OnChange(updated =>
            {
                _ = ResetConnectionAsync(updated);
            });
        }

        private static AmazonSQSClient BuildClient(SQSPublisherOptions options)
        {
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

        public async Task SendAsync(T message, string destination)
        {
            string queueUrl = Resolve(destination);
            var body = Serialize(message);

            var request = new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = body,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>()
            };

            using var activity = _activitySource.StartActivity($"send {queueUrl}", ActivityKind.Producer);
            InjectTraceContext(request.MessageAttributes, activity);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "aws_sqs");
                activity.SetTag("messaging.operation.name", "send");
                activity.SetTag("messaging.operation.type", "send");
                activity.SetTag("messaging.destination.name", queueUrl);
                activity.SetTag("messaging.message.body.size", Encoding.UTF8.GetByteCount(body));
            }

            // Capture the client rather than re-reading the field after the await: ResetConnectionAsync
            // disposes the old client as soon as it swaps the field, with no wait for an in-flight send.
            AmazonSQSClient client = _sqsClient;
            SendMessageResponse response;
            try
            {
                response = await client.SendMessageAsync(request);
            }
            catch (ObjectDisposedException)
            {
                // A concurrent options change disposed the client this send was using. Retry once
                // against whatever client is current now.
                _logger.LogWarning("SqsPublisher client was disposed by a connection reset while sending to {QueueUrl}; retrying once", queueUrl);

                response = await _sqsClient.SendMessageAsync(request);
            }

            activity?.SetTag("messaging.message.id", response.MessageId);

            if(response.HttpStatusCode != HttpStatusCode.OK)
            {
                // Returning normally here would report a message as sent that SQS rejected.
                throw new InvalidOperationException(
                    $"SQS rejected the message published to '{queueUrl}': HTTP {(int)response.HttpStatusCode} ({response.HttpStatusCode}), request id {response.ResponseMetadata?.RequestId ?? "unknown"}.");
            }
        }

        private void InjectTraceContext(Dictionary<string, MessageAttributeValue> attributes, Activity activity)
        {
            if (activity == null)
                return;

            var traceparent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
            attributes["traceparent"] = new MessageAttributeValue { DataType = "String", StringValue = traceparent };

            if (!string.IsNullOrEmpty(activity.TraceStateString))
            {
                attributes["tracestate"] = new MessageAttributeValue { DataType = "String", StringValue = activity.TraceStateString };
            }
        }

        private string Resolve(string destination)
        {
            if (_options.Destinations == null || !_options.Destinations.TryGetValue(destination, out string queueUrl))
            {
                throw new KeyNotFoundException(
                    $"No destination named '{destination}' is configured. Configured destinations: " +
                    string.Join(", ", _options.Destinations?.Keys ?? Enumerable.Empty<string>()));
            }

            return queueUrl;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return ValueTask.CompletedTask;
            }

            _changeToken?.Dispose();
            _sqsClient.Dispose();
            _connectionLock.Dispose();
            return ValueTask.CompletedTask;
        }

        private async Task ResetConnectionAsync(SQSPublisherOptions updated)
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

                await _connectionLock.WaitAsync();
                try
                {
                    Interlocked.Exchange(ref _options, updated);

                    oldClient = _sqsClient;
                    _sqsClient = BuildClient(updated);
                }
                finally
                {
                    _connectionLock.Release();
                }

                oldClient.Dispose();

                _logger.LogInformation("SqsPublisher reset connection due to options change");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset SqsPublisher connection after options change");
            }
        }

        private string Serialize(T data)
        {
            return JsonSerializer.Serialize<T>(data);

        }
    }
}
