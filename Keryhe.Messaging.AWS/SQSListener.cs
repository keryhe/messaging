using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace Keryhe.Messaging.AWS
{
    public class SQSListener<T> : IMessageListener<T>
    {
        private readonly SQSListenerOptions _options;
        private readonly ILogger<SQSListener<T>> _logger;
        private AmazonSQSClient _sqsClient;
        private Func<T, Task<bool>> _messageHandler;
        private bool _status;

        public SQSListener(SQSListenerOptions options, ILogger<SQSListener<T>> logger)
        {
            _options = options;
            _logger = logger;
            _status = false;

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

        public Task SubscribeAsync(Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            _status = true;

            _messageHandler = messageHandler;

            Task.Run(() => Run(cancellationToken), cancellationToken);

            _logger.LogDebug("SqsListener Started");
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken)
        {
            _status = false;

            _logger.LogDebug("SqsListener Stopped");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _sqsClient.Dispose();

            return ValueTask.CompletedTask;
        }

        private async Task Run(CancellationToken cancellationToken)
        {
            while (_status)
            {
                ReceiveMessageResponse response = await _sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = _options.QueueUrl,
                    MaxNumberOfMessages = _options.MaxNumberOfMessages,
                    WaitTimeSeconds = _options.WaitTimeSeconds
                }, cancellationToken);

                if(response.HttpStatusCode == HttpStatusCode.OK)
                {
                    foreach(var m in response.Messages)
                    {
                        T message = Deserialize(m.Body);
                        bool success = await _messageHandler(message);

                        if (success)
                        {
                            await _sqsClient.DeleteMessageAsync(_options.QueueUrl, m.ReceiptHandle);
                        }
                    }
                }
            }
            
        }

        private T Deserialize(string message)
        {
            T data = JsonSerializer.Deserialize<T>(message);
            return data;
        }

        
    }
}