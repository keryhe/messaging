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
        public string QueueName { get; set; }
        public string TopicName { get; set; }
        
    }

    public class ServiceBusListenerOptions : ServiceBusOptions
    {
        public string SubscriptionName { get; set; }
    }

    public class ServiceBusPublisherOptions : ServiceBusOptions
    {

    }
}
