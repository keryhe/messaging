# Keryhe.Messaging

![Keryhe.Messaging](https://img.shields.io/nuget/v/Keryhe.Messaging.svg)

There are two interfaces in the messaging namespace, IMessageListener and IMessagePublisher. To implement messaging for your chosen transport layer, install the [Keryhe.Messaging](https://www.nuget.org/packages/keryhe.messaging) package from Nuget.

**IMessageListener** - Listens for messages and calls the provided function when a message is received. This interface also implements IDisposableAsync for cleaning up resources.

```c#
Task SubscribeAsync((Func<T, Task<bool>> messageHandler, CancelationToken cancelationToken);
Task UnsubscribeAsync(CancelationToken cancelationToken);
```

Use the **SubscribeAsync** method to start listening for messages. When a message is received, the messagerHandler function called and the message passed to the calling class for processing. **Unsubscribe**, stops listening for messages.

**IMessagePublisher** - Publishes a message. This interface also implements IDisposable for cleaning up resources.

```c#
void Send(T message);
```

The **Send** method publishes a message to the chosen destination.

