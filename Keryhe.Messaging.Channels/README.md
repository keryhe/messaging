# Keryhe.Messaging.Channels

![Keryhe.Messaging.Channels](https://img.shields.io/nuget/v/Keryhe.Messaging.Channels.svg)

An in-process implementation of the IMessageListener and IMessagePublisher interfaces using
[System.Threading.Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels).
Unlike the other transports in this repo, a channel doesn't connect to anything external — it's a
FIFO producer/consumer queue that only exists within a single process. Use it when you want
queue-like behavior (ordering, optional backpressure) between components in the same process,
without the overhead of a real broker.

**How to add the ChannelListener and ChannelPublisher**

```csharp
using Keryhe.Messaging.Channels.Extensions;

builder.Services.AddChannelListener<Message>(builder.Configuration.GetSection("ChannelListener"));

builder.Services.AddChannelPublisher<Message>(builder.Configuration.GetSection("ChannelPublisher"));
```

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

**Unbounded means unbounded**: with `Capacity` unset, a channel grows in memory for as long as it
is written to. A broker holds a backlog on disk and tells you about it; an in-process channel holds
it in your heap and does not. Nothing drains a channel whose listener has stopped, or one that was
never subscribed to at all — messages accumulate until the process runs out of memory. **Set
`Capacity` on any channel whose producer can outpace its consumer**, and pick a `FullMode` that
matches what should happen when it fills.

`FullMode: "Wait"` (the default) gives real backpressure, but `SendAsync` has no `CancellationToken`,
so a full channel with a stopped consumer blocks the publisher indefinitely. Set
`SendTimeoutMilliseconds` on the publisher to bound that wait — `SendAsync` then throws
`TimeoutException` instead of hanging. It defaults to `0`, meaning wait forever.

**No redelivery**: an in-process channel has no broker behind it, so there is nothing to retry
against. A handler returning `false` logs a warning naming the message id, and the message is
dropped — unlike RabbitMQ (nacked), SQS (visibility reset) or Service Bus (abandoned), where the
message comes back. The same applies to a handler that throws: the exception is logged and
consumption continues with the next message. If a message matters, do not rely on the channel to
give you another chance at it.

To use in-process channels as your transport layer, install the
[Keryhe.Messaging.Channels](https://www.nuget.org/packages/keryhe.messaging.channels) package from
NuGet.
