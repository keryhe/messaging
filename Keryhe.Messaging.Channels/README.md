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

To use in-process channels as your transport layer, install the
[Keryhe.Messaging.Channels](https://www.nuget.org/packages/keryhe.messaging.channels) package from
NuGet.
