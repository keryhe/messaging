# Keryhe.Messaging.Polling

![Keryhe.Messaging.Polling](https://img.shields.io/nuget/v/Keryhe.Messaging.Polling.svg)

A base abstraction for building custom polling-based listeners — for example, polling a database
table for new rows. `Poller<T>` implements `IMessageListener<T>`, so once you have a concrete
subclass it plugs into the same `SubscribeAsync`/`UnsubscribeAsync` contract as every other
listener in this repo.

## Poller

To implement your own polling class, create a class that inherits from the `Poller<T>` abstract
class and implement its one abstract method:

```csharp
protected abstract Task<T> Poll();
```

`Poll` is called whenever it's time to retrieve data from your chosen source (usually a database
or an API). If it returns a null or empty `T`, the poller waits (via the configured `IDelay`)
before polling again; otherwise your message handler is invoked with the result and the delay
resets.

`Poller<T>` has a single implicit source — there's nothing to poll "by name" the way a queue or
topic would have one. `SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)`
still takes a `source` argument to satisfy `IMessageListener<T>`, but it's accepted and ignored;
pass any value you like.

The `Poller<T>` constructor takes an `IDelay`, which controls how long to wait between polls when
nothing was found.

## IDelay

Implementers of the `IDelay` interface specify how long to wait if no data is found when querying
the source. There are four built-in implementations (wait times are in seconds). Included are the
appsettings sections needed to configure each:

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

Everything else is taken care of by the `Poller<T>` base class — all you need to do is implement
the `Poll` method.

To build a custom polling listener, install the
[Keryhe.Messaging.Polling](https://www.nuget.org/packages/keryhe.messaging.polling) package from
NuGet.
