using Amazon.SQS;
using Amazon.SQS.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Keryhe.Messaging.AWS
{
    public class SQSPublisher<T> : IMessagePublisher<T>
    {
        private readonly SQSPublisherOptions _options;
        private readonly AmazonSQSClient _sqsClient;

        public SQSPublisher(SQSPublisherOptions options)
        {
            _options = options;
            _sqsClient = new AmazonSQSClient();
        }

        public async Task SendAsync(T message)
        {
            var body = Serialize(message);
            SendMessageResponse response = await _sqsClient.SendMessageAsync(_options.QueueUrl, body);

            if(response.HttpStatusCode != HttpStatusCode.OK)
            {

            }
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
