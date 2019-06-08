# messaging

A messaging wrapper for sending data from a source to a destination. It currently supports the following transport mechanisms:

- Events
- FileSystem
- RabbitMQ

# Keryhe.Messaging

![Keryhe.Messaging](https://img.shields.io/nuget/v/Keryhe.Messaging.svg)

There are two interfaces in the messaging namespace, IMessageListener and IMessagePublisher. To implement messaging for your chosen transport layer, install the [Keryhe.Messaging](https://www.nuget.org/packages/keryhe.messaging) package from Nuget.

**IMessageListener** - Listens for messages and calls the provided function when a message is received. This interface also implements IDisposable for cleaning up resources.

```c#
void Start(Action<T> callback);
void Stop();
```

Use the **Start** method to start listening for messages. When a message is received, the callback action called and the message passed to the calling class for processing. **Stop**, stops listening for messages.

**IMessagePublisher** - Publishes a message. This interface also implements IDisposable for cleaning up resources.

```c#
void Send(T message);
```

The **Send** method publishes a message to the chosen destination. 

# Keryhe.Messaging.Events

![Keryhe.Messaging.RabbitMQ](https://img.shields.io/nuget/v/Keryhe.Messaging.Events.svg)

An implementation of the IMessageListener and IMessagePublisher interfaces by publishing and subscribing to events.

To use Events as your transport layer, install the [Keryhe.Messaging.Events](https://www.nuget.org/packages/keryhe.messaging.events) package from NuGet.

# Keryhe.Messaging.IO

![Keryhe.Messaging.IO](https://img.shields.io/nuget/v/Keryhe.Messaging.io.svg)

An implementation of the IMessageListener and IMessagePublisher interfaces by storing and reading files. Supports xml and json file types.

**appsettings configuration section**

```json
"FileSystemListener": 
{
    "Folder": "c:\\QueueFolder",
    "FileType": "Json",
    "CompletedFolder": "",
    "Interval": 1
}

"FileSystemPublisher": 
{
    "Folder": "c:\\QueueFolder",
    "FileType": "Json"
}
```

To use Events as your transport layer, install the [Keryhe.Messaging.FileSystem](https://www.nuget.org/packages/keryhe.messaging.filesystem) package from NuGet.

# Keryhe.Messaging.RabbitMQ

![Keryhe.Messaging.RabbitMQ](https://img.shields.io/nuget/v/Keryhe.Messaging.RabbitMQ.svg)

A RabbitMQ implementation of the IMessageListener and IMessagePublisher interfaces. Uses the [RabbitMQ.Client](https://www.nuget.org/packages/rabbitmq.client) package.

**appsettings configuration section**

```json
"RabbitMQListener": 
{
    "Host": "localhost",
    "Queue": "QueueName",
    "Durable" : true,
    "Exclusive" : false,
    "AutoDelete" : false,
    "AutoAck" : true,
    "BasicQos" : 
    {
        "PrefetchSize" : 0,
        "PrefetchCount" : 1,
        "Global" : false
    }
}

"RabbitMQPublisher": 
{
    "Host": "localhost",
    "Queue": "QueueName",
    "Durable" : true,
    "Exclusive" : false,
    "AutoDelete" : false,
    "Persistent" : true
}
```

To use RabbitMQ as your transport layer, install the [Keryhe.Messaging.RabbitMQ](https://www.nuget.org/packages/keryhe.messaging.rabbitmq) package from NuGet.
