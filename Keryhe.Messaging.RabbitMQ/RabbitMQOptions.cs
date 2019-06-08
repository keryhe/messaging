namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQOptions
    {
        public RabbitMQOptions()
        {
            Host = "";
            Queue = "";
            Durable = true;
            Exclusive = false;
            AutoDelete = false;
        }

        public string Host { get; set; }
        public string Queue { get; set; }
        public bool Durable { get; set; }
        public bool Exclusive { get; set; }
        public bool AutoDelete { get; set; }
    }

    public class RabbitMQListenerOptions : RabbitMQOptions
    {
        public RabbitMQListenerOptions()
            : base()
        {
            AutoAck = true;
            BasicQos = new BasicQosOptions();
        }

        public bool AutoAck { get; set; }
        public BasicQosOptions BasicQos { get; set; }
    }

    public class RabbitMQPublisherOptions : RabbitMQOptions
    {
        public RabbitMQPublisherOptions()
            :base()
        {
            Persistent = true;
        }

        public bool Persistent { get; set; }
    }

    public class BasicQosOptions
    {
        public BasicQosOptions()
        {
            PrefetchSize = 0;
            PrefetchCount = 1;
            Global = false;
        }

        public uint PrefetchSize { get; set; }
        public ushort PrefetchCount { get; set; }
        public bool Global { get; set; }

    }
}
