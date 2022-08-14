namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQOptions
    {
        public RabbitMQOptions()
        {
            Exchange = new ExchangeOptions();
            Queue = new QueueOptions();
            Factory = new FactoryOptions();
        }

        public ExchangeOptions Exchange { get; set; }
        public QueueOptions Queue { get; set; }
        public FactoryOptions Factory { get; set; }
    }

    public class FactoryOptions
    {
        public FactoryOptions()
        {
            UserName = "guest";
            Password = "guest";
            VirtualHost = "/";
            HostName = "localhost";
            Port = 5672;
        }

        public string UserName { get; set; }
        public string Password { get; set; }
        public string VirtualHost { get; set; }
        public string HostName { get; set; }
        public int Port { get; set; }
    }

    public class ExchangeOptions
    {
        public ExchangeOptions()
        {
            Name = "";
            Type = "";
            Durable = true;
            AutoDelete = false;
            RoutingKey = "";
        }

        public string Name { get; set; }
        public string Type { get; set; }
        public bool Durable { get; set; }
        public bool AutoDelete { get; set; }
        public string RoutingKey { get; set; }
    }

    public class QueueOptions
    {
        public QueueOptions()
        {
            Name = "";
            Durable = true;
            Exclusive = false;
            AutoDelete = false;
        }

        public string Name { get; set; }
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
