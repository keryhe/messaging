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
    "Factory": {
        "UserName": "guest",
        "Password": "guest",
        "VirtualHost": "/",
        "HostName": "localhost",
        "Port": 5672
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
        "UserName": "guest",
        "Password": "guest",
        "VirtualHost": "/",
        "HostName": "localhost",
        "Port": 5672
    },
    "Persistent" : true,
    "Mandatory" : false,
    "PublisherConfirms" : false,
    "ConfirmTimeoutMilliseconds" : 5000,
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

Each key under **Destinations** is the name you pass to `SendAsync(message, name)`. If `Exchange.Name` is blank, the message publishes directly to `Queue.Name` via the default exchange (as in the `orders` example above); otherwise it publishes to that exchange using `Exchange.RoutingKey`, falling back to `Queue.Name` if `RoutingKey` is blank (as in the `audit` example). When a destination names both an exchange and a queue, the publisher binds the queue to the exchange on that routing key the first time it publishes there.

### Publisher confirms

By default `SendAsync` returns as soon as the message is written to the socket. That is fast, but it means a broker that rejects the message — or a broker that is no longer there — still produces a successfully completed `SendAsync`. **If your code treats a returning `SendAsync` as "the message is safe", set `PublisherConfirms` to `true`.**

With `PublisherConfirms` enabled, `SendAsync` waits for the broker to confirm the message and throws `RabbitMQ.Client.Exceptions.PublishException` if the broker nacks it. Combined with `Mandatory: true`, it also throws when the message is unroutable — no exchange binding matches — rather than letting the broker silently drop it.

`ConfirmTimeoutMilliseconds` (default `5000`) bounds that wait, so a silent broker surfaces as a `TimeoutException` instead of hanging. Set it to `0` or less to wait indefinitely. Publishes are serialized on a single channel, so this timeout is also the longest one stuck publish can block others; keep it modest under concurrency.

Confirms cost a broker round-trip per message, which is why they default to off.
