using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQPublisher<T> : IMessagePublisher<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new ("Keryhe.Messaging.RabbitMQ");
        private readonly IOptionsMonitor<RabbitMQPublisherOptions> _optionsMonitor;
        private readonly IDisposable _changeToken;

        private RabbitMQPublisherOptions _options;
        private readonly ILogger<RabbitMQPublisher<T>> _logger;

        private ConnectionFactory _factory;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private readonly SemaphoreSlim _publishLock = new(1, 1);
        private readonly ConcurrentDictionary<string, bool> _declared = new();
        private IConnection _connection;
        private IChannel _channel;
        private int _disposed;

        public RabbitMQPublisher(IOptions<RabbitMQPublisherOptions> options, ILogger<RabbitMQPublisher<T>> logger)
        {
            _options = options.Value;
            _logger = logger;

            _factory = BuildFactory(_options);
        }

        public RabbitMQPublisher(IOptionsMonitor<RabbitMQPublisherOptions> options, ILogger<RabbitMQPublisher<T>> logger)
        {
            _optionsMonitor = options;
            _options = options.CurrentValue;
            _logger = logger;

            _factory = BuildFactory(_options);

            _changeToken = _optionsMonitor.OnChange(updated =>
            {
                _ = ResetConnectionAsync(updated);
            });
        }

        private static ConnectionFactory BuildFactory(RabbitMQPublisherOptions options)
        {
            // Configuration can still set Factory to an explicit null, which binding honours.
            FactoryOptions factory = options.Factory ?? new FactoryOptions();

            return new ConnectionFactory()
            {
                UserName = factory.UserName,
                Password = factory.Password,
                VirtualHost = factory.VirtualHost,
                HostName = factory.HostName,
                Port = factory.Port,

                // Without these a dropped connection leaves _connection and _channel non-null but
                // dead, and EnsureConnectionAsync's early return means the publisher never
                // reconnects for the life of the process.
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };
        }

        public async Task SendAsync(T message, string destination)
        {
            // Snapshot once: an options change mid-send would otherwise mix old and new values
            // across the reads below, the same way P8 snapshotted PublishAsync alone.
            RabbitMQPublisherOptions options = _options;

            RabbitMQDestinationOptions destinationOptions = Resolve(options, destination);

            // Capture the channel rather than re-reading the field further down: ResetConnectionAsync
            // holds _connectionLock, not _publishLock, so it can null _channel mid-send.
            IChannel channel = await EnsureConnectionAsync();

            // An empty exchange name is the default exchange, which routes by queue name — that is
            // the "publish straight to a queue" configuration, not a misconfiguration.
            string exchangeName = destinationOptions.Exchange?.Name ?? string.Empty;
            string queueName = destinationOptions.Queue?.Name;
            string routingKey = !string.IsNullOrEmpty(destinationOptions.Exchange?.RoutingKey)
                ? destinationOptions.Exchange.RoutingKey
                : queueName;
            string destinationName = !string.IsNullOrEmpty(exchangeName) ? exchangeName : queueName;

            var body = Serialize(message);
            var properties = new BasicProperties
            {
                Persistent = options.Persistent,
                ContentType = "application/json",
                Headers = new Dictionary<string, object>(),
                MessageId = Guid.NewGuid().ToString()
            };

            using var activity = _activitySource.StartActivity($"send {destinationName}", ActivityKind.Producer);
            InjectTraceContext(properties, activity);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "rabbitmq");
                activity.SetTag("messaging.operation.name", "send");
                activity.SetTag("messaging.operation.type", "send");
                activity.SetTag("messaging.destination.name", destinationName);
                activity.SetTag("messaging.rabbitmq.destination_kind", string.IsNullOrEmpty(exchangeName) ? "queue" : "exchange");
                activity.SetTag("messaging.rabbitmq.routing_key", routingKey);
                activity.SetTag("messaging.message.body.size", body.Length);
                activity.SetTag("messaging.message.id", properties.MessageId);
            }

            // IChannel is not safe for concurrent use, and this publisher shares one channel across
            // all destinations, so declare-and-publish has to be serialized. This must not be
            // _connectionLock: EnsureConnectionAsync takes that one.
            await _publishLock.WaitAsync();
            try
            {
                await DeclareTopologyAsync(channel, destination, destinationOptions, exchangeName, queueName, routingKey);

                await PublishAsync(channel, exchangeName, routingKey, properties, body);
            }
            finally
            {
                _publishLock.Release();
            }
        }

        private async Task DeclareTopologyAsync(IChannel channel, string destination, RabbitMQDestinationOptions destinationOptions, string exchangeName, string queueName, string routingKey)
        {
            if (_declared.ContainsKey(destination))
            {
                return;
            }

            if (!string.IsNullOrEmpty(exchangeName))
            {
                await channel.ExchangeDeclareAsync(
                    exchange: exchangeName,
                    type: destinationOptions.Exchange.Type,
                    durable: destinationOptions.Exchange.Durable,
                    arguments: null);
            }

            if (!string.IsNullOrEmpty(queueName))
            {
                await channel.QueueDeclareAsync(
                    queue: queueName,
                    durable: destinationOptions.Queue.Durable,
                    exclusive: destinationOptions.Queue.Exclusive,
                    autoDelete: destinationOptions.Queue.AutoDelete,
                    arguments: null);
            }

            // Declaring an exchange and a queue without binding them means everything published to
            // the exchange matches no binding and is silently dropped.
            if (!string.IsNullOrEmpty(exchangeName) && !string.IsNullOrEmpty(queueName))
            {
                await channel.QueueBindAsync(
                    queue: queueName,
                    exchange: exchangeName,
                    routingKey: routingKey,
                    arguments: null);
            }

            // Mark the destination declared only once the topology is actually in place. Marking it
            // up front means a failed declare is never retried and every later publish skips it.
            _declared[destination] = true;
        }

        private async Task PublishAsync(IChannel channel, string exchangeName, string routingKey, BasicProperties properties, byte[] body)
        {
            // Snapshot once: an options change mid-send would otherwise mix old and new values
            // across the reads below.
            RabbitMQPublisherOptions options = _options;

            if (!options.PublisherConfirms)
            {
                await channel.BasicPublishAsync(
                    exchange: exchangeName,
                    routingKey: routingKey,
                    mandatory: options.Mandatory,
                    basicProperties: properties,
                    body: body);
                return;
            }

            int timeout = options.ConfirmTimeoutMilliseconds;
            using var timeoutCts = timeout > 0 ? new CancellationTokenSource(TimeSpan.FromMilliseconds(timeout)) : null;

            try
            {
                // With confirms enabled this awaits the broker's ack and throws PublishException on
                // a nack or an unroutable mandatory message.
                await channel.BasicPublishAsync(
                    exchange: exchangeName,
                    routingKey: routingKey,
                    mandatory: options.Mandatory,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: timeoutCts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException ex) when (timeoutCts != null && timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The broker did not confirm message {properties.MessageId} published to exchange '{exchangeName}' with routing key '{routingKey}' within {timeout}ms.",
                    ex);
            }
        }

        private async Task<IChannel> EnsureConnectionAsync()
        {
            IChannel current = _channel;
            if (_connection != null && _connection.IsOpen && current != null && current.IsOpen)
            {
                return current;
            }

            await _connectionLock.WaitAsync();
            try
            {
                if (_connection != null && !_connection.IsOpen)
                {
                    // Automatic recovery retries a dropped connection, but a terminal close — bad
                    // credentials, a removed vhost — leaves a non-null dead object here, and every
                    // later CreateChannelAsync throws on it for the life of the process.
                    _logger.LogWarning("RabbitMQPublisher connection is closed ({CloseReason}); opening a new one", _connection.CloseReason?.ReplyText);

                    IConnection deadConnection = _connection;
                    IChannel deadChannel = _channel;

                    _connection = null;
                    _channel = null;
                    _declared.Clear();

                    if (deadChannel != null)
                    {
                        await deadChannel.DisposeAsync();
                    }

                    await deadConnection.DisposeAsync();
                }

                if (_connection == null)
                {
                    _connection = await _factory.CreateConnectionAsync();

                    // Topology declared on the old connection is gone; clear the cache so it is
                    // re-asserted on the next publish after recovery.
                    _connection.ConnectionShutdownAsync += (sender, ea) =>
                    {
                        _logger.LogWarning("RabbitMQPublisher connection shut down: {ReplyCode} {ReplyText}", ea.ReplyCode, ea.ReplyText);
                        _declared.Clear();
                        return Task.CompletedTask;
                    };
                }

                if (_channel != null && _channel.IsClosed)
                {
                    // A channel-level error (a mismatched declare, say) closes the channel for good.
                    // Automatic recovery does not cover that, and the non-null check below would
                    // otherwise keep handing out the dead channel until the process restarts.
                    _logger.LogWarning("RabbitMQPublisher channel is closed ({CloseReason}); opening a new one", _channel.CloseReason?.ReplyText);

                    IChannel dead = _channel;
                    _channel = null;
                    _declared.Clear();

                    await dead.DisposeAsync();
                }

                if (_channel == null)
                {
                    _channel = _options.PublisherConfirms
                        ? await _connection.CreateChannelAsync(new CreateChannelOptions(
                            publisherConfirmationsEnabled: true,
                            publisherConfirmationTrackingEnabled: true))
                        : await _connection.CreateChannelAsync();

                    _channel.BasicAcksAsync += (sender, ea) =>
                    {
                        _logger.LogDebug("Message {DeliveryTag} ACKED by broker.", ea.DeliveryTag);
                        return Task.CompletedTask;
                    };
                    _channel.BasicNacksAsync += (sender, ea) =>
                    {
                        _logger.LogWarning("Message {DeliveryTag} NACKED by broker.", ea.DeliveryTag);
                        return Task.CompletedTask;
                    };
                    _channel.BasicReturnAsync += (sender, ea) =>
                    {
                        _logger.LogWarning("Message returned: {Body} with reply code {ReplyCode} and reply text {ReplyText}.", Encoding.UTF8.GetString(ea.Body.ToArray()), ea.ReplyCode, ea.ReplyText);
                        return Task.CompletedTask;
                    };
                }

                return _channel;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task ResetConnectionAsync(RabbitMQPublisherOptions updated)
        {
            // OnChange fires the callback fire-and-forget; a change racing DisposeAsync would
            // otherwise operate on a lock disposal is concurrently tearing down.
            if (Volatile.Read(ref _disposed) == 1)
            {
                return;
            }

            try
            {
                IChannel oldChannel;
                IConnection oldConnection;

                await _connectionLock.WaitAsync();
                try
                {
                    Interlocked.Exchange(ref _options, updated);

                    _factory = BuildFactory(updated);

                    oldChannel = _channel;
                    oldConnection = _connection;
                    _channel = null;
                    _connection = null;

                    _declared.Clear();
                }
                finally
                {
                    _connectionLock.Release();
                }

                if (oldChannel != null)
                {
                    await oldChannel.DisposeAsync();
                }
                if (oldConnection != null)
                {
                    await oldConnection.DisposeAsync();
                }

                _logger.LogInformation("RabbitMQPublisher reset connection due to options change");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset RabbitMQPublisher connection after options change");
            }
        }

        private static RabbitMQDestinationOptions Resolve(RabbitMQPublisherOptions options, string destination)
        {
            if (options.Destinations == null || !options.Destinations.TryGetValue(destination, out RabbitMQDestinationOptions destinationOptions))
            {
                throw new KeyNotFoundException(
                    $"No destination named '{destination}' is configured. Configured destinations: " +
                    string.Join(", ", options.Destinations?.Keys ?? Enumerable.Empty<string>()));
            }

            return destinationOptions;
        }

        private byte[] Serialize(T data)
        {
            string jsonified = JsonSerializer.Serialize<T>(data);
            _logger.LogDebug("Publisher sent a message: {Message}", jsonified);
            byte[] databuffer = Encoding.UTF8.GetBytes(jsonified);
            return databuffer;
        }

        private void InjectTraceContext(BasicProperties properties, Activity activity)
        {
            if (activity == null)
                return;

            // Inject W3C Trace Context headers (traceparent and tracestate)
            properties.Headers ??= new Dictionary<string, object>();
            
            // traceparent format: version-trace_id-span_id-flags
            var traceparent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
            properties.Headers["traceparent"] = traceparent;

            // Include tracestate if present
            if (!string.IsNullOrEmpty(activity.TraceStateString))
            {
                properties.Headers["tracestate"] = activity.TraceStateString;
            }

            // Optional: Add correlation ID for easier debugging
            properties.CorrelationId = activity.TraceId.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _changeToken?.Dispose();

            if(_channel != null)
            {
                await _channel.CloseAsync();
                await _channel.DisposeAsync();
            }

            if(_connection != null)
            {
                await _connection.CloseAsync();
                await _connection.DisposeAsync();
            }

            _connectionLock.Dispose();
            _publishLock.Dispose();
        }
    }
}
