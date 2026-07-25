# Keryhe.Messaging.AWS

![Keryhe.Messaging.AWS](https://img.shields.io/nuget/v/Keryhe.Messaging.aws.svg)

An Amazon SQS implementation of the IMessageListener and IMessagePublisher interfaces.

**How to add the SQSListener and SQSPublisher**

```csharp
using Keryhe.Messaging.AWS.Extensions;

builder.Services.AddSQSListener<Message>(builder.Configuration.GetSection("AmazonSQSListener"));

builder.Services.AddSQSPublisher<Message>(builder.Configuration.GetSection("AmazonSQSPublisher"));
```

**appsettings configuration section**

```json
"AmazonSQSListener": 
{
    "Region": "",
    "AccessKey": "",
    "SecretKey": "",
    "Sources": {
        "orders": {
            "QueueUrl": "https://sqs.us-east-1.amazonaws.com/123456789012/orders",
            "MaxNumberOfMessages": 1,
            "WaitTimeSeconds": 5
        },
        "audit": {
            "QueueUrl": "https://sqs.us-east-1.amazonaws.com/123456789012/audit",
            "MaxNumberOfMessages": 1,
            "WaitTimeSeconds": 5
        }
    }
}

"AmazonSQSPublisher": 
{
    "Region": "",
    "AccessKey": "",
    "SecretKey": "",
    "Destinations": {
        "orders": "https://sqs.us-east-1.amazonaws.com/123456789012/orders",
        "audit": "https://sqs.us-east-1.amazonaws.com/123456789012/audit"
    }
}
```

Each key under **Sources** is the name you pass to `SubscribeAsync(source, handler, token)` — call it once per source to listen to. `UnsubscribeAsync(source, token)` stops listening to that source.

Each key under **Destinations** is the name you pass to `SendAsync(message, name)`, mapped to the queue's URL.

To use Amazon SQS as your transport layer, install the [Keryhe.Messaging.AWS](https://www.nuget.org/packages/keryhe.messaging.aws) package from NuGet.
