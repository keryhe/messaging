# Keryhe.Messaging.Azure

![Keryhe.Messaging.Azure](https://img.shields.io/nuget/v/Keryhe.Messaging.Azure.svg)

A Microsoft Azure Service Bus implementation of the IMessageListener and IMessagePublisher interfaces.

**How to add the ServiceBusListener and ServiceBusPublisher**

```csharp
using Keryhe.Messaging.Azure.Extensions;

builder.Services.AddServiceBusListener<Message>(builder.Configuration.GetSection("AzureServiceBusListener"));

builder.Services.AddServiceBusPublisher<Message>(builder.Configuration.GetSection("AzureServiceBusPublisher"));
```

**appsettings configuration section**

```json
"AzureServiceBusListener": 
{
    "ConnectionString": "",
    "Sources": {
        "orders": {
            "QueueName": "orders-queue",
            "TopicName": "",
            "SubscriptionName": ""
        },
        "notifications": {
            "QueueName": "",
            "TopicName": "notifications-topic",
            "SubscriptionName": "notifications-subscription"
        }
    }
}

"AzureServiceBusPublisher": 
{
    "ConnectionString": "",
    "Destinations": {
        "orders": "orders-queue",
        "notifications": "notifications-topic"
    }
}
```

Each key under **Sources** is the name you pass to `SubscribeAsync(source, handler, token)` — call it once per source to listen to. `UnsubscribeAsync(source, token)` stops listening to that source. Leave `TopicName`/`SubscriptionName` blank to listen on a queue (as in the `orders` example above), or leave `QueueName` blank and set both `TopicName` and `SubscriptionName` to listen on a topic subscription (as in `notifications`).

Each key under **Destinations** is the name you pass to `SendAsync(message, name)`, mapped to a queue or topic name — Service Bus treats both identically when sending, so no separate configuration is needed for each.

To use Microsoft Azure Service Bus as your transport layer, install the [Keryhe.Messaging.Azure](https://www.nuget.org/packages/keryhe.messaging.azure) package from NuGet.
