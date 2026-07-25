using Amazon.SQS;
using Amazon.SQS.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Keryhe.Messaging.AWS
{
    public class SQSPublisher<T> : IMessagePublisher<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.AWS");
        private readonly SQSPublisherOptions _options;
        private readonly AmazonSQSClient _sqsClient;

        public SQSPublisher(SQSPublisherOptions options)
        {
            _options = options;
            _sqsClient = new AmazonSQSClient();
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

            SendMessageResponse response = await _sqsClient.SendMessageAsync(request);
            activity?.SetTag("messaging.message.id", response.MessageId);

            if(response.HttpStatusCode != HttpStatusCode.OK)
            {

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
            _sqsClient.Dispose();
            return ValueTask.CompletedTask;
        }

        private string Serialize(T data)
        {
            return JsonSerializer.Serialize<T>(data);

        }
    }
}
