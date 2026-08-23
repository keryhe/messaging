using System.Collections.Generic;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQOptions
    {
        public RabbitMQOptions()
        {
            // Every other nested options object is default-instantiated by its parent. Leaving this
            // one null means a configuration without a "Factory" section — including the
            // AddRabbitMQListener/AddRabbitMQPublisher overloads that take no IConfiguration —
            // throws a NullReferenceException from the constructor.
            Factory = new FactoryOptions();
        }

        public FactoryOptions Factory { get; set; }
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

    public class RabbitMQListenerOptions : RabbitMQOptions
    {
        public RabbitMQListenerOptions()
            : base()
        {
            BasicQos = new BasicQosOptions();
        }

        public BasicQosOptions BasicQos { get; set; }
        public Dictionary<string, RabbitMQListenerSourceOptions> Sources { get; set; }
    }

    public class RabbitMQListenerSourceOptions
    {
        public RabbitMQListenerSourceOptions()
        {
            Exchange = new ExchangeOptions();
            Queue = new QueueOptions();
            AutoAck = true;
        }

        public ExchangeOptions Exchange { get; set; }
        public QueueOptions Queue { get; set; }
        public bool AutoAck { get; set; }
    }

    public class RabbitMQPublisherOptions : RabbitMQOptions
    {
        public RabbitMQPublisherOptions()
            :base()
        {
            Persistent = true;
            Mandatory = false;
            PublisherConfirms = false;
            ConfirmTimeoutMilliseconds = 5000;
        }

        public bool Persistent { get; set; }
        public bool Mandatory { get; set; }

        /// <summary>
        /// When true, SendAsync waits for the broker to confirm the message and throws if the
        /// broker nacks or returns it. When false (the default) SendAsync completes as soon as the
        /// frame is written to the socket, so a rejected or undelivered message looks like success.
        /// </summary>
        public bool PublisherConfirms { get; set; }

        /// <summary>
        /// How long SendAsync waits for a broker confirmation before throwing a TimeoutException.
        /// Zero or less waits indefinitely. Ignored unless PublisherConfirms is true. Note that
        /// publishes are serialized, so a silent broker blocks other publishers for this long.
        /// </summary>
        public int ConfirmTimeoutMilliseconds { get; set; }
        public Dictionary<string, RabbitMQDestinationOptions> Destinations { get; set; }
    }

    public class RabbitMQDestinationOptions
    {
        public RabbitMQDestinationOptions()
        {
            Exchange = new ExchangeOptions();
            Queue = new QueueOptions();
        }

        public ExchangeOptions Exchange { get; set; }
        public QueueOptions Queue { get; set; }
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
