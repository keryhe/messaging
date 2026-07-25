using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keryhe.Messaging.Azure
{
    public class ServiceBusOptions
    {
        public string ConnectionString { get; set; }
    }

    public class ServiceBusListenerOptions : ServiceBusOptions
    {
        public Dictionary<string, ServiceBusSourceOptions> Sources { get; set; }
    }

    public class ServiceBusSourceOptions
    {
        public string QueueName { get; set; }
        public string TopicName { get; set; }
        public string SubscriptionName { get; set; }
    }

    public class ServiceBusPublisherOptions : ServiceBusOptions
    {
        public Dictionary<string, string> Destinations { get; set; }
    }
}
