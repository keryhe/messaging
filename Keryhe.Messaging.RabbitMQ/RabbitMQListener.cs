using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Keryhe.Messaging.RabbitMQ
{
    public class RabbitMQListener<T> : IMessageListener<T>, IAsyncDisposable
    {
        private static readonly ActivitySource _activitySource = new("Keryhe.Messaging.RabbitMQ");

        private readonly IOptionsMonitor<RabbitMQListenerOptions>_optionsMonitor;
        private readonly IDisposable _changeToken;

        private RabbitMQListenerOptions _options;
        private readonly ILogger<RabbitMQListener<T>> _logger;

        private ConnectionFactory _factory;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        // Serializes subscribe/unsubscribe. Stopping the previous consumer first only prevents a
        // leak if no second subscribe can interleave between the stop and the dictionary write.
        private readonly SemaphoreSlim _subscribeLock = new(1, 1);
        private readonly ConcurrentDictionary<string, SourceSubscription> _consumers = new();
        private IConnection _connection;
        private int _disposed;

        // Each source owns its channel, so a channel-level fault, a topology declaration and a
        // settle on one source cannot disturb any other source.
        private sealed class SourceSubscription
        {
            public IChannel Channel { get; init; }
            public string ConsumerTag { get; init; }
            public Func<T, Task<bool>> MessageHandler { get; init; }
            public CancellationToken CancellationToken { get; init; }
            public CancellationTokenRegistration Registration { get; set; }
        }

        public RabbitMQListener(IOptions<RabbitMQListenerOptions> options, ILogger<RabbitMQListener<T>> logger)
        {
            _options = options.Value;
            _logger = logger;

            _factory = BuildFactory(_options);
        }

        public RabbitMQListener(IOptionsMonitor<RabbitMQListenerOptions> options, ILogger<RabbitMQListener<T>> logger)
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

        private static ConnectionFactory BuildFactory(RabbitMQListenerOptions options)
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

                // Without recovery a dropped connection leaves _connection non-null but dead, so
                // EnsureConnectionAsync returns early and the listener never consumes again —
                // silently, since there is no caller to throw to.
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true
            };
        }

        public async Task SubscribeAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            await _subscribeLock.WaitAsync();
            try
            {
                await SubscribeCoreAsync(source, messageHandler, cancellationToken);
            }
            finally
            {
                _subscribeLock.Release();
            }
        }

        private async Task SubscribeCoreAsync(string source, Func<T, Task<bool>> messageHandler, CancellationToken cancellationToken)
        {
            RabbitMQListenerSourceOptions sourceOptions = Resolve(source);

            // Resolve the optionally-null Exchange once, rather than guarding some uses and not
            // others — Exchange set explicitly to null in configuration is a valid queue-only source.
            ExchangeOptions exchange = sourceOptions.Exchange;
            QueueOptions queue = sourceOptions.Queue;
            string exchangeName = exchange?.Name;

            if(string.IsNullOrEmpty(queue?.Name))
            {
                throw new ArgumentNullException("Queue Name cannot be null");
            }

            // Subscribing a source twice would otherwise leave the first consumer live and
            // unreachable, competing for the same queue.
            await StopSourceCoreAsync(source);

            // Capture the connection rather than re-reading the field further down: an options
            // change holds _connectionLock, not _subscribeLock, so it can null _connection here.
            IConnection connection = await EnsureConnectionAsync();

            IChannel channel = await connection.CreateChannelAsync();

            try
            {
                if (!string.IsNullOrEmpty(exchangeName))
                {
                    await channel.ExchangeDeclareAsync(
                        exchange: exchangeName,
                        type: exchange.Type,
                        durable: exchange.Durable,
                        arguments: null);
                }

                await channel.QueueDeclareAsync(
                    queue: queue.Name,
                    durable: queue.Durable,
                    exclusive: queue.Exclusive,
                    autoDelete: queue.AutoDelete,
                    arguments: null);

                if (!string.IsNullOrEmpty(exchangeName))
                {
                    await channel.QueueBindAsync(
                        queue: queue.Name,
                        exchange: exchangeName,
                        routingKey: queue.Name,
                        arguments: null);
                }

                // Configuration can still set BasicQos to an explicit null, which binding honours,
                // the same way it can null out Factory (R8).
                BasicQosOptions basicQos = _options.BasicQos ?? new BasicQosOptions();

                await channel.BasicQosAsync(
                    prefetchSize: basicQos.PrefetchSize,
                    prefetchCount: basicQos.PrefetchCount,
                    global: basicQos.Global);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (sender, ea) => ConsumerReceivedAsync(channel, messageHandler, sourceOptions.AutoAck, sender, ea);

                string consumerTag = await channel.BasicConsumeAsync(
                    queue: queue.Name,
                    autoAck: sourceOptions.AutoAck,
                    consumer: consumer);

                var subscription = new SourceSubscription
                {
                    Channel = channel,
                    ConsumerTag = consumerTag,
                    MessageHandler = messageHandler,
                    CancellationToken = cancellationToken
                };

                _consumers[source] = subscription;

                // Cancelling the token the caller passed here is expected to stop the subscription,
                // as it does on every other provider. Register after the entry exists so an
                // already-cancelled token cannot fire before there is anything to stop.
                subscription.Registration = cancellationToken.Register(() => _ = StopSourceOnCancellationAsync(source));

                if (cancellationToken.IsCancellationRequested)
                {
                    await StopSourceCoreAsync(source);
                    return;
                }
            }
            catch
            {
                if (_consumers.TryRemove(source, out SourceSubscription failed))
                {
                    failed.Registration.Dispose();
                }

                await channel.DisposeAsync();
                throw;
            }

            _logger.LogInformation("RabbitMQListener started for source {Source}", source);
        }

        private async Task StopSourceOnCancellationAsync(string source)
        {
            try
            {
                await StopSourceAsync(source);

                _logger.LogInformation("RabbitMQListener stopped for source {Source}: its subscription token was cancelled", source);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop source {Source} after its subscription token was cancelled", source);
            }
        }

        private async Task<IConnection> EnsureConnectionAsync()
        {
            IConnection current = _connection;
            if (current != null && current.IsOpen)
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
                    _logger.LogWarning("RabbitMQListener connection is closed ({CloseReason}); opening a new one", _connection.CloseReason?.ReplyText);

                    IConnection dead = _connection;
                    _connection = null;

                    await dead.DisposeAsync();
                }

                if (_connection == null)
                {
                    _connection = await _factory.CreateConnectionAsync();

                    _connection.ConnectionShutdownAsync += (sender, ea) =>
                    {
                        _logger.LogWarning("RabbitMQListener connection shut down: {ReplyCode} {ReplyText}", ea.ReplyCode, ea.ReplyText);
                        return Task.CompletedTask;
                    };
                }

                return _connection;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task ResetConnectionAsync(RabbitMQListenerOptions updated)
        {
            // Guards the case where a change is already in flight when DisposeAsync runs — after
            // dispose completes, _changeToken.Dispose() has already unsubscribed OnChange, so this
            // check cannot be exercised by a change that arrives strictly after disposal.
            if (Volatile.Read(ref _disposed) == 1)
            {
                return;
            }

            try
            {
                IConnection oldConnection;
                List<(string Source, Func<T, Task<bool>> MessageHandler, CancellationToken CancellationToken)> existingSources;

                await _connectionLock.WaitAsync();
                try
                {
                    Interlocked.Exchange(ref _options, updated);

                    _factory = BuildFactory(updated);

                    oldConnection = _connection;
                    _connection = null;

                    existingSources = _consumers
                        .Select(kvp => (kvp.Key, kvp.Value.MessageHandler, kvp.Value.CancellationToken))
                        .ToList();
                }
                finally
                {
                    _connectionLock.Release();
                }

                // Per-source channels belong to the old connection, so they have to be closed
                // before it is disposed.
                foreach (var entry in existingSources)
                {
                    await StopSourceAsync(entry.Source);
                }

                if (oldConnection != null)
                {
                    await oldConnection.DisposeAsync();
                }

                foreach (var entry in existingSources)
                {
                    try
                    {
                        // Carry the caller's original token across, or cancelling it would stop
                        // having any effect after the first options change.
                        await SubscribeAsync(entry.Source, entry.MessageHandler, entry.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to resubscribe source {Source} after options change", entry.Source);
                    }
                }

                _logger.LogInformation("RabbitMQListener reset connection due to options change");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset RabbitMQListener connection after options change");
            }
        }

        public async Task UnsubscribeAsync(string source, CancellationToken cancellationToken)
        {
            await StopSourceAsync(source);

            _logger.LogInformation("RabbitMQListener stopped for source {Source}", source);
        }

        private async Task StopSourceAsync(string source)
        {
            await _subscribeLock.WaitAsync();
            try
            {
                await StopSourceCoreAsync(source);
            }
            finally
            {
                _subscribeLock.Release();
            }
        }

        /// <summary>Callers already holding <see cref="_subscribeLock"/> use this.</summary>
        private async Task StopSourceCoreAsync(string source)
        {
            if (!_consumers.TryRemove(source, out SourceSubscription subscription))
            {
                return;
            }

            subscription.Registration.Dispose();

            try
            {
                if (subscription.Channel.IsOpen)
                {
                    await subscription.Channel.BasicCancelAsync(subscription.ConsumerTag);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cancel the consumer for source {Source}; disposing its channel anyway", source);
            }

            await subscription.Channel.DisposeAsync();
        }

        private async Task ConsumerReceivedAsync(IChannel channel, Func<T, Task<bool>> messageHandler, bool autoAck, object sender, BasicDeliverEventArgs ea)
        {
            string destinationName = !string.IsNullOrEmpty(ea.Exchange) ? ea.Exchange : ea.RoutingKey;

            var parentContext = ExtractTraceContext(ea.BasicProperties);
            using var activity = _activitySource.StartActivity($"process {destinationName}", ActivityKind.Consumer, parentContext);
            if(activity != null)
            {
                activity.SetTag("messaging.system", "rabbitmq");
                activity.SetTag("messaging.operation.name", "process");
                activity.SetTag("messaging.operation.type", "process");
                activity.SetTag("messaging.destination.name", destinationName);
                activity.SetTag("messaging.rabbitmq.routing_key", ea.RoutingKey);
                activity.SetTag("messaging.message.body.size", ea.Body.Length);
                activity.SetTag("messaging.message.id", ea.BasicProperties?.MessageId);
            }

            byte[] body = ea.Body.ToArray();

            T message;
            try
            {
                message = Deserialize(body);
            }
            catch (Exception ex)
            {
                // A malformed payload can never succeed, so nack without requeue to dead-letter it
                // immediately rather than let it redeliver forever.
                _logger.LogError(ex, "RabbitMQListener could not deserialize a message from {Destination}; dead-lettering it. Payload: {Payload}",
                    destinationName, Encoding.UTF8.GetString(body));

                await SettleAsync(channel, ea.DeliveryTag, autoAck, ack: false);
                return;
            }

            try
            {
                bool success = await messageHandler(message);

                await SettleAsync(channel, ea.DeliveryTag, autoAck, ack: success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQListener handler threw for a message from {Destination}; nacking it", destinationName);

                await SettleAsync(channel, ea.DeliveryTag, autoAck, ack: false);
            }
        }

        private async Task SettleAsync(IChannel channel, ulong deliveryTag, bool autoAck, bool ack)
        {
            if (autoAck)
            {
                return;
            }

            try
            {
                if (ack)
                {
                    await channel.BasicAckAsync(deliveryTag, false);
                }
                else
                {
                    await channel.BasicNackAsync(deliveryTag, false, false);
                }
            }
            catch (Exception ex)
            {
                // The channel can close under us — throwing here would escape into the client's
                // callback dispatcher, where nothing observes it.
                _logger.LogError(ex, "RabbitMQListener could not {Action} delivery {DeliveryTag}", ack ? "ack" : "nack", deliveryTag);
            }
        }

        private T Deserialize(byte[] array)
        {
            string jsonified = Encoding.UTF8.GetString(array);
            _logger.LogDebug("Listener received a message: {Message}", jsonified);
            T data = JsonSerializer.Deserialize<T>(jsonified);

            if (data == null)
            {
                // A "null" payload is not a message. Handing null to the handler pushes the failure
                // into caller code that has no way to settle the delivery.
                throw new InvalidOperationException("The message payload deserialized to null.");
            }

            return data;
        }

        private RabbitMQListenerSourceOptions Resolve(string source)
        {
            if (_options.Sources == null || !_options.Sources.TryGetValue(source, out RabbitMQListenerSourceOptions sourceOptions))
            {
                throw new KeyNotFoundException(
                    $"No source named '{source}' is configured. Configured sources: " +
                    string.Join(", ", _options.Sources?.Keys ?? Enumerable.Empty<string>()));
            }

            return sourceOptions;
        }

        private ActivityContext ExtractTraceContext(IReadOnlyBasicProperties properties)
        {
            if (properties?.Headers == null)
                return default;

            // Extract traceparent header
            if (properties.Headers.TryGetValue("traceparent", out var traceparentObj))
            {
                var traceparent = traceparentObj switch
                {
                    byte[] bytes => Encoding.UTF8.GetString(bytes),
                    string str => str,
                    _ => null
                };

                if (!string.IsNullOrEmpty(traceparent))
                {
                    // Parse traceparent: version-trace_id-span_id-flags
                    var parts = traceparent.Split('-');
                    if (parts.Length == 4)
                    {
                        // Extract tracestate if present
                        string traceState = null;
                        if (properties.Headers.TryGetValue("tracestate", out var tracestateObj))
                        {
                            traceState = tracestateObj switch
                            {
                                byte[] bytes => Encoding.UTF8.GetString(bytes),
                                string str => str,
                                _ => null
                            };
                        }

                        try
                        {
                            var traceId = ActivityTraceId.CreateFromString(parts[1].AsSpan());
                            var spanId = ActivitySpanId.CreateFromString(parts[2].AsSpan());
                            var traceFlags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

                            return new ActivityContext(traceId, spanId, traceFlags, traceState);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            // The ids are only well-formed if the sender made them so. Throwing
                            // here would escape the consumer callback before the delivery is
                            // settled, leaving a poison message redelivering forever — losing the
                            // trace link is the lesser failure by a long way.
                            _logger.LogWarning("RabbitMQListener could not parse the traceparent header '{TraceParent}'; processing the message without a parent trace context", traceparent);
                        }
                    }
                }
            }

            return default;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _changeToken?.Dispose();

            foreach (string source in _consumers.Keys.ToList())
            {
                await StopSourceAsync(source);
            }

            if(_connection != null)
            {
                await _connection.DisposeAsync();
            }
            _connectionLock.Dispose();
            _subscribeLock.Dispose();
        }
    }
}
