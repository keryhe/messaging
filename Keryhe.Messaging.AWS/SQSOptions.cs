using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keryhe.Messaging.AWS
{
    public class SQSOptions
    {
        public string Region { get; set; }
        public string AccessKey { get; set; }
        public string SecretKey { get; set; }
    }

    public class SQSListenerOptions: SQSOptions
    {
        public Dictionary<string, SQSSourceOptions> Sources { get; set; }
    }

    public class SQSSourceOptions
    {
        public SQSSourceOptions()
        {
            // Both are sent to SQS on every receive, so leaving them at the int default of 0 means
            // asking for zero messages — which SQS rejects outright — with short polling, which
            // turns the receive loop into a billed request spin.
            MaxNumberOfMessages = 1;
            WaitTimeSeconds = 20;
        }

        public string QueueUrl { get; set; }

        /// <summary>How many messages one receive may return. SQS allows 1 to 10.</summary>
        public int MaxNumberOfMessages { get; set; }

        /// <summary>
        /// How long a receive waits for a message before returning empty. SQS allows 0 to 20;
        /// 0 is short polling, which costs a request per empty poll.
        /// </summary>
        public int WaitTimeSeconds { get; set; }
    }

    public class SQSPublisherOptions: SQSOptions
    {
        public Dictionary<string, string> Destinations { get; set; }
    }
}
