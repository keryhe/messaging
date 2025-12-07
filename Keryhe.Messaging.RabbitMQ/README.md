# Keryhe.Messaging.RabbitMQ

![Keryhe.Messaging.RabbitMQ](https://img.shields.io/nuget/v/Keryhe.Messaging.RabbitMQ.svg)

A RabbitMQ implementation of the IMessageListener and IMessagePublisher interfaces. Uses the [RabbitMQ.Client](https://www.nuget.org/packages/rabbitmq.client) package.

**How to add the RabbitMQListener and RabbitMQPublisher**

```csharp
using Keryhe.Messaging.RabbitMQ.Extensions;

builder.Services.AddRabbitMQListener<Message>(builder.Configuration.GetSection("RabbitMQListener"));

builder.Services.AddRabbitMQPublisher<Message>(builder.Configuration.GetSection("RabbitMQPublisher"));
```

**appsettings configuration section**

```json
"RabbitMQListener": 
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
    "Factory": {
        "UserName": "",
        "Password": "",
        "VirtualHost": "",
        "HostName": "",
        "Port": ""
    },
    "BasicQos" : 
    {
        "PrefetchSize" : 0,
        "PrefetchCount" : 1,
        "Global" : false
    }
}

"RabbitMQPublisher": 
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
    "Factory": {
        "UserName": "",
        "Password": "",
        "VirtualHost": "",
        "HostName": "",
        "Port": ""
    }
    "Persistent" : true,
    "Manditory" : false
}
```
