# messaging

A messaging wrapper for sending data from a source to a destination. It currently supports the following transport mechanisms:

- FileSystem
- RabbitMQ
- Amazon SQS
- Azure Service Bus
- Channels (in-process, via System.Threading.Channels)
- Polling (a base abstraction for building custom polling-based listeners)

# Keryhe.Messaging

![Keryhe.Messaging](https://img.shields.io/nuget/v/Keryhe.Messaging.svg)

There are two interfaces in the messaging namespace, IMessageListener and IMessagePublisher. To implement messaging for your chosen transport layer, install the [Keryhe.Messaging](https://www.nuget.org/packages/keryhe.messaging) package from Nuget.

**IMessageListener** - Listens for messages and calls the provided function when a message is received. This interface also implements IDisposableAsync for cleaning up resources.

```c#
Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken);
Task UnsubscribeAsync(string source, CancellationToken cancellationToken);
```

Call **SubscribeAsync** once per named source you want to listen to; when a message arrives on that source, its handler is invoked. **UnsubscribeAsync(source, token)** stops listening to that source.

**IMessagePublisher** - Publishes a message. This interface also implements IDisposable for cleaning up resources.

```c#
Task SendAsync(T message, string destination);
```

The **SendAsync** method publishes a message to a specific named destination.


# Keryhe.Messaging.IO

![Keryhe.Messaging.IO](https://img.shields.io/nuget/v/Keryhe.Messaging.io.svg)

An implementation of the IMessageListener and IMessagePublisher interfaces by storing and reading files. Supports xml and json file types.

**appsettings configuration section**

```json
"FileSystemListener": 
{
    "Sources": {
        "orders": {
            "Folder": "c:\\QueueFolder\\Orders",
            "FileType": "Json",
            "CompletedFolder": "",
            "ErrorFolder": "",
            "Interval": 1
        },
        "audit": {
            "Folder": "c:\\QueueFolder\\Audit",
            "FileType": "Xml",
            "CompletedFolder": "",
            "ErrorFolder": "",
            "Interval": 5
        }
    }
}

"FileSystemPublisher": 
{
    "Destinations": {
        "orders": {
            "Folder": "c:\\QueueFolder\\Orders",
            "FileType": "Json"
        },
        "audit": {
            "Folder": "c:\\QueueFolder\\Audit",
            "FileType": "Xml"
        }
    }
}
```

Each key under **Sources** is the name you pass to `SubscribeAsync(source, handler, token)` — call it once per source to listen to. `UnsubscribeAsync(source, token)` stops listening to that source.

Each key under **Destinations** is the name you pass to `SendAsync(message, name)`.

To use the File System as your transport layer, install the [Keryhe.Messaging.IO](https://www.nuget.org/packages/keryhe.messaging.io) package from NuGet.

# Keryhe.Messaging.AWS

![Keryhe.Messaging.AWS](https://img.shields.io/nuget/v/Keryhe.Messaging.aws.svg)

An Amazon SQS implementation of the IMessageListener and IMessagePublisher interfaces. 

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

# Keryhe.Messaging.Azure

![Keryhe.Messaging.Azure](https://img.shields.io/nuget/v/Keryhe.Messaging.Azure.svg)

A Microsoft Azure Service Bus implementation of the IMessageListener and IMessagePublisher interfaces.

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


# Keryhe.Messaging.RabbitMQ

![Keryhe.Messaging.RabbitMQ](https://img.shields.io/nuget/v/Keryhe.Messaging.RabbitMQ.svg)

A RabbitMQ implementation of the IMessageListener and IMessagePublisher interfaces. Uses the [RabbitMQ.Client](https://www.nuget.org/packages/rabbitmq.client) package.

**appsettings configuration section**

```json
"RabbitMQFactory": {
        "UserName": "",
        "Password": "",
        "VirtualHost": "",
        "HostName": "",
        "Port": ""
}

"RabbitMQListener": 
{
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
    },
    "Sources": {
        "orders": {
            "Exchange": { "Name": "", "Type": "", "Durable": true, "AutoDelete": false, "RoutingKey": "" },
            "Queue": { "Name": "orders", "Durable": true, "Exclusive": false, "AutoDelete": false },
            "AutoAck": true
        },
        "audit": {
            "Exchange": { "Name": "audit-exchange", "Type": "fanout", "Durable": true, "AutoDelete": false, "RoutingKey": "audit.events" },
            "Queue": { "Name": "audit-queue", "Durable": true, "Exclusive": false, "AutoDelete": false },
            "AutoAck": false
        }
    }
}

"RabbitMQPublisher": 
{
    "Factory": {
        "UserName": "",
        "Password": "",
        "VirtualHost": "",
        "HostName": "",
        "Port": ""
    },
    "Persistent" : true,
    "Mandatory" : false,
    "Destinations": {
        "orders": {
            "Exchange": { "Name": "", "Type": "", "Durable": true, "AutoDelete": false, "RoutingKey": "" },
            "Queue": { "Name": "orders", "Durable": true, "Exclusive": false, "AutoDelete": false }
        },
        "audit": {
            "Exchange": { "Name": "audit-exchange", "Type": "fanout", "Durable": true, "AutoDelete": false, "RoutingKey": "audit.events" },
            "Queue": { "Name": "", "Durable": true, "Exclusive": false, "AutoDelete": false }
        }
    }
}
```

Each key under **Sources** is the name you pass to `SubscribeAsync(source, handler, token)` — call it once per source to listen to. `UnsubscribeAsync(source, token)` stops listening to that source. `BasicQos` applies to every source on the listener; `AutoAck` is per source, so different sources can use automatic or manual acknowledgement independently (as in the `orders`/`audit` examples above). `Queue.Name` is required for every source — a listener must consume from a real named queue, unlike a publisher which can route via the default exchange alone.

Each key under **Destinations** is the name you pass to `SendAsync(message, name)`. If `Exchange.Name` is blank, the message publishes directly to `Queue.Name` via the default exchange (as in the `orders` example above); otherwise it publishes to that exchange using `Exchange.RoutingKey`, falling back to `Queue.Name` if `RoutingKey` is blank (as in the `audit` example).

To use RabbitMQ as your transport layer, install the [Keryhe.Messaging.RabbitMQ](https://www.nuget.org/packages/keryhe.messaging.rabbitmq) package from NuGet.

# Keryhe.Messaging.Channels

![Keryhe.Messaging.Channels](https://img.shields.io/nuget/v/Keryhe.Messaging.Channels.svg)

An in-process implementation of the IMessageListener and IMessagePublisher interfaces using
[System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).
Unlike the other transports in this repo, a channel doesn't connect to anything external — it's a
FIFO producer/consumer queue that only exists within a single process. Use it when you want
queue-like behavior (ordering, optional backpressure) between components in the same process,
without the overhead of a real broker.

**appsettings configuration section**

```json
"ChannelListener": 
{
    "Sources": {
        "orders": {
            "Capacity": 100,
            "FullMode": "Wait",
            "SingleReader": true,
            "SingleWriter": false
        }
    }
}

"ChannelPublisher": 
{
    "Destinations": {
        "orders": {
            "Capacity": 100,
            "FullMode": "Wait",
            "SingleReader": true,
            "SingleWriter": false
        }
    }
}
```

Each key under **Sources**/**Destinations** is the name you pass to
`SubscribeAsync(source, handler, token)` / `SendAsync(message, name)`. `Capacity` left unset (or
`null`) creates an unbounded channel; setting it creates a bounded channel with that capacity.
`FullMode` (`Wait`, `DropOldest`, `DropNewest`, or `DropWrite`) controls what happens when a
bounded channel is full — `Wait` (the default) makes `SendAsync` asynchronously wait for room,
giving genuine backpressure.

**Important**: the publisher and listener for a given name share the same underlying channel —
whichever side resolves that name first (the first `SendAsync` or `SubscribeAsync` call) creates
it using *its own* `Capacity`/`FullMode`/`SingleReader`/`SingleWriter` settings. If the other side's
configuration for that same name differs, its settings are silently ignored (the channel already
exists) and a warning is logged noting the mismatch. Configure both sides identically for a given
name to avoid relying on call order.

To use in-process channels as your transport layer, install the
[Keryhe.Messaging.Channels](https://www.nuget.org/packages/keryhe.messaging.channels) package from
NuGet.

# Keryhe.Messaging.Polling

![Keryhe.Messaging.Polling](https://img.shields.io/nuget/v/Keryhe.Messaging.Polling.svg)

## Poller

`Poller<T>` implements `IMessageListener<T>`, so once you have a concrete subclass it plugs into
the same `SubscribeAsync`/`UnsubscribeAsync` contract as every other listener in this repo. To
implement your own polling class, create a class that inherits from the `Poller<T>` abstract class
and implement its one abstract method:

```c#
protected abstract Task<T> Poll();
```

`Poll` is called whenever it's time to retrieve data from your chosen source, usually a database.
If it returns a null or empty `T`, the poller waits (via the configured `IDelay`) before polling
again; otherwise your message handler is invoked with the result and the delay resets.

`Poller<T>` has a single implicit source — there's nothing to poll "by name" the way a queue or
topic would have one. `SubscribeAsync(source, handler, token)` still takes a `source` argument to
satisfy `IMessageListener<T>`, but it's accepted and ignored; pass any value you like.

The constructor of the Poller abstract class accepts as one of its parameters an object of type IDelay.

## IDelay

Implementers of the IDelay interface specify the amount of time to wait if no data is found when querying the source. There are four built in IDelay implementations (wait times are in seconds). Included are the appsettings sections needed to configure the delays:

- **ConstantDelay** - Uses a constant wait time. 
    ```json
    "ConstantOptions": 
    {
        "Interval": 1
    }
    ```
- **ExponentialDelay** - Multiplies the previous wait time by a factor in order to get the next wait time.
    ```json
    "ExponentialOptions": 
    {
        "Factor": 2,
        "MaxWait": 60
    }
    ```
- **FibonacciDelay** - Adds the last two wait times together starting at 1 (based on the fibonacci sequence) to get the next wait time.
    ```json
    "FibonacciOptions": 
    {
        "MaxWait": 60
    }
    ```
- **LinearDelay** - Adds a given value to the wait time.
    ```json
    "LinearOptions": 
    {
        "Increment": 1,
        "MaxWait": 60
    }
    ```

Everything is taken care of by the Poller base class. All you need to do is implement the Poll method.