# messaging

A messaging wrapper for sending data from a source to a destination. It currently supports the following transport mechanisms:

- Events
- FileSystem
- RabbitMQ

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
    "ErrorFolder": "",
    "Interval": 1
}

"FileSystemPublisher": 
{
    "Folder": "c:\\QueueFolder",
    "FileType": "Json"
}
```

To use the File System as your transport layer, install the [Keryhe.Messaging.FileSystem](https://www.nuget.org/packages/keryhe.messaging.filesystem) package from NuGet.

# Keryhe.Messaging.AWS

![Keryhe.Messaging.AWS](https://img.shields.io/nuget/v/Keryhe.Messaging.aws.svg)

An Amazon SQS implementation of the IMessageListener and IMessagePublisher interfaces. 

**appsettings configuration section**

```json
"AmazonSQSListener": 
{
    "QueueUrl": "",
    "Region": "",
    "AccessKey": "",
    "SecretKey": "",
    "MaxNumberOfMessages": 1,
    "WaitTimeSeconds": 5
}

"AmazonSQSPublisher": 
{
    "QueueUrl": "",
    "Region": "",
    "AccessKey": "",
    "SecretKey": "",
}
```

To use Amazon SQS as your transport layer, install the [Keryhe.Messaging.AWS](https://www.nuget.org/packages/keryhe.messaging.aws) package from NuGet.

# Keryhe.Messaging.Azure

![Keryhe.Messaging.Azure](https://img.shields.io/nuget/v/Keryhe.Messaging.Azure.svg)

A Microsoft Azure Service Bus implementation of the IMessageListener and IMessagePublisher interfaces.

**appsettings configuration section**

```json
"AzureServiceBusListener": 
{
    "ConnectionString": "",
    "QueueName": "",
    "TopicName": "",
    "SubscriptionName": ""
}

"AzureServiceBusPublisher": 
{
    "ConnectionString": "",
    "QueueName": "",
    "TopicName": "",
}
```

To use Microsoft Azure Service Bus as your transport layer, install the [Keryhe.Messaging.Azure](https://www.nuget.org/packages/keryhe.messaging.azure) package from NuGet.


# Keryhe.Messaging.RabbitMQ

![Keryhe.Messaging.RabbitMQ](https://img.shields.io/nuget/v/Keryhe.Messaging.RabbitMQ.svg)

A RabbitMQ implementation of the IMessageListener and IMessagePublisher interfaces. Uses the [RabbitMQ.Client](https://www.nuget.org/packages/rabbitmq.client) package.

**appsettings configuration section**

```json
"RabbitMQListener": 
{
    "Exchange": {
        "Name": "",
        "Type": "",
        "Durable": true,
        "AutoDelete": false,
        "RoutingKey": ""
    },
    "Queue": {
        "Name": "",
        "Durable": true,
        "Exclusive": false,
        "AutoDelete": false
    },
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
    }
}

"RabbitMQPublisher": 
{
    "Exchange": {
        "Name": "",
        "Type": "",
        "Durable": true,
        "AutoDelete": false,
        "RoutingKey": ""
    },
    "Queue": {
        "Name": "",
        "Durable": true,
        "Exclusive": false,
        "AutoDelete": false
    },
    "Factory": {
        "UserName": "",
        "Password": "",
        "VirtualHost": "",
        "HostName": "",
        "Port": ""
    },
    "Persistent" : true
}
```

To use RabbitMQ as your transport layer, install the [Keryhe.Messaging.RabbitMQ](https://www.nuget.org/packages/keryhe.messaging.rabbitmq) package from NuGet.

# Keryhe.Messaging.Polling

![Keryhe.Messaging.Polling](https://img.shields.io/nuget/v/Keryhe.Messaging.Polling.svg)

## Poller

The IPolling interface implements the IMessageListener interface. To implement your own polling class, you will need to create a class that inherits from the Poller abstract class. this class implements the IPolling interface and provides an abstract method, Poll for you to implement yourself.

```c#
protected abstract T Poll();
```

Poll is called whenever it is time to retrieve data from your chosen source, usually a database.

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