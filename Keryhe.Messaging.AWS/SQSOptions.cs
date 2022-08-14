using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keryhe.Messaging.AWS
{
    public class SQSOptions
    {
        public string QueueUrl { get; set; }
        public string Region { get; set; }
        public string AccessKey { get; set; }
        public string SecretKey { get; set; }
    }

    public class SQSListenerOptions: SQSOptions
    {
        public int MaxNumberOfMessages { get; set; }
        public int WaitTimeSeconds { get; set; }
    }

    public class SQSPublisherOptions: SQSOptions
    {

    }
}
