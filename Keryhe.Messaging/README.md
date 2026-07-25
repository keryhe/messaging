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

