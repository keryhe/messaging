﻿namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQOptions
    {
        public string Host { get; set; }
        public string Queue { get; set; }
    }

    public class RabbitMQListenerOptions : RabbitMQOptions
    {
    }

    public class RabbitMQPublisherOptions : RabbitMQOptions
    {
    }
}
