# Keryhe.Messaging.RabbitMQ

![Keryhe.Messaging.RabbitMQ](https://img.shields.io/nuget/v/Keryhe.Messaging.RabbitMQ.svg)

A RabbitMQ implementation of the IMessageListener and IMessagePublisher interfaces. Uses the [RabbitMQ.Client](https://www.nuget.org/packages/rabbitmq.client) package.

**appsettings configuration section**

```json
"Keryhe.Messaging.RabbitMQ.RabbitMQFactoryOptions": {
    "UserName": "",
    "Password": "",
    "VirtualHost": "",
    "HostName": "",
    "Port": ""
}

"Keryhe.Messaging.RabbitMQ.RabbitMQListenerOptions": 
{
    "Exchange": {
        "Name": "",
        "Type": "",
        "Durable": true,
        "AutoDelete": false,
        "RoutingKey": ""
    },
    "Queue": {
        "Name": "",
        "Durable": true,
        "Exclusive": false,
        "AutoDelete": false
    },
    "BasicQos" : 
    {
        "PrefetchSize" : 0,
        "PrefetchCount" : 1,
        "Global" : false
    }
}

"Keryhe.Messaging.RabbitMQ.RabbitMQPublisherOptions": 
{
    "Exchange": {
        "Name": "",
        "Type": "",
        "Durable": true,
        "AutoDelete": false,
        "RoutingKey": ""
    },
    "Queue": {
        "Name": "",
        "Durable": true,
        "Exclusive": false,
        "AutoDelete": false
    },
    "Persistent" : true,
    "Manditory" : false
}
```

To use RabbitMQ as your transport layer, install the [Keryhe.Messaging.RabbitMQ](https://www.nuget.org/packages/keryhe.messaging.rabbitmq) package from NuGet.
