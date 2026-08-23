using Microsoft.Extensions.Logging;
using Keryhe.Messaging.Channels.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace Keryhe.Messaging.Tests;

/// <summary>
/// Channels are fully in-process, so these run without any infrastructure. The pattern they encode
/// — one bad message must not end consumption — is the same one behind S1, F2 and G1 on the
/// providers that do need a broker.
/// </summary>
public class ChannelListenerTests
{
    private const string Source = "orders";

    private static (IMessagePublisher<Order> Publisher, IMessageListener<Order> Listener, ServiceProvider Provider) Build()
    {
        ServiceProvider provider = TestServices.ForChannels<Order>(Source);

        return (provider.GetRequiredService<IMessagePublisher<Order>>(),
                provider.GetRequiredService<IMessageListener<Order>>(),
                provider);
    }

    [Fact]
    public async Task ThrowingHandlerDoesNotStopConsumption()
    {
        // C2: an exception from the handler used to escape Run and fault the discarded task,
        // ending consumption permanently and silently.
        var (publisher, listener, provider) = Build();
        await using var _ = provider;

        var handled = new ConcurrentQueue<string>();

        await listener.SubscribeAsync(Source, order =>
        {
            if (order.Id == "boom")
            {
                throw new InvalidOperationException("handler blew up");
            }

            handled.Enqueue(order.Id);
            return Task.FromResult(true);
        }, CancellationToken.None);

        await publisher.SendAsync(new Order("boom", 1), Source);
        await publisher.SendAsync(new Order("second", 1), Source);
        await publisher.SendAsync(new Order("third", 1), Source);

        await Wait.ForAssert(() => handled.Count == 2, "consumption must continue after a handler throws");
        Assert.Equal(new[] { "second", "third" }, handled.ToArray());
    }

    [Fact]
    public async Task FalseReturnDoesNotStopConsumption()
    {
        // C1: the bool was discarded entirely. It is now logged; consumption continues either way.
        var (publisher, listener, provider) = Build();
        await using var _ = provider;

        var handled = new ConcurrentQueue<string>();

        await listener.SubscribeAsync(Source, order =>
        {
            handled.Enqueue(order.Id);
            return Task.FromResult(order.Id != "rejected");
        }, CancellationToken.None);

        await publisher.SendAsync(new Order("rejected", 1), Source);
        await publisher.SendAsync(new Order("accepted", 1), Source);

        await Wait.ForAssert(() => handled.Count == 2, "consumption must continue after a handler returns false");
    }

    [Fact]
    public async Task UnsubscribeStopsConsumption()
    {
        var (publisher, listener, provider) = Build();
        await using var _ = provider;

        var handled = new ConcurrentQueue<string>();

        await listener.SubscribeAsync(Source, order =>
        {
            handled.Enqueue(order.Id);
            return Task.FromResult(true);
        }, CancellationToken.None);

        await publisher.SendAsync(new Order("first", 1), Source);
        await Wait.ForAssert(() => handled.Count == 1, "the first message should be handled");

        await listener.UnsubscribeAsync(Source, CancellationToken.None);
        await publisher.SendAsync(new Order("after-unsubscribe", 1), Source);

        // Give the (now cancelled) reader a chance to misbehave before asserting it did not.
        await Task.Delay(250);
        Assert.Single(handled);
    }

    [Fact]
    public async Task CancellingTheSubscriptionTokenStopsConsumption()
    {
        var (publisher, listener, provider) = Build();
        await using var _ = provider;

        var handled = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource();

        await listener.SubscribeAsync(Source, order =>
        {
            handled.Enqueue(order.Id);
            return Task.FromResult(true);
        }, cts.Token);

        await publisher.SendAsync(new Order("first", 1), Source);
        await Wait.ForAssert(() => handled.Count == 1, "the first message should be handled");

        await cts.CancelAsync();
        await publisher.SendAsync(new Order("after-cancel", 1), Source);

        await Task.Delay(250);
        Assert.Single(handled);
    }

    [Fact]
    public async Task ListenerAndPublisherAreRegisteredAsSingletons()
    {
        // X1: these were transient, so a container tracked a fresh instance — each holding a
        // background loop or client — for every resolution, all released only at shutdown.
        await using ServiceProvider provider = TestServices.ForChannels<Order>(Source);

        Assert.Same(provider.GetRequiredService<IMessageListener<Order>>(), provider.GetRequiredService<IMessageListener<Order>>());
        Assert.Same(provider.GetRequiredService<IMessagePublisher<Order>>(), provider.GetRequiredService<IMessagePublisher<Order>>());
    }

    [Fact]
    public async Task SubscribingTwiceLeavesOnlyOneReader()
    {
        // The second subscribe overwrote the first reader's CancellationTokenSource without
        // cancelling it, so UnsubscribeAsync could only ever stop the second — the first kept
        // consuming for the life of the process.
        var (publisher, listener, provider) = Build();
        await using var _ = provider;

        var handled = new ConcurrentQueue<string>();
        Task<bool> Handler(Order order)
        {
            handled.Enqueue(order.Id);
            return Task.FromResult(true);
        }

        await listener.SubscribeAsync(Source, Handler, CancellationToken.None);
        await listener.SubscribeAsync(Source, Handler, CancellationToken.None);

        await publisher.SendAsync(new Order("first", 1), Source);
        await Wait.ForAssert(() => handled.Count == 1, "the surviving reader should handle the message");

        // One unsubscribe must stop consumption entirely.
        await listener.UnsubscribeAsync(Source, CancellationToken.None);
        await publisher.SendAsync(new Order("after-unsubscribe", 1), Source);

        await Task.Delay(400);
        Assert.Single(handled);
    }

    [Fact]
    public async Task AShapeMismatchIsWarnedAboutOnceNotPerMessage()
    {
        // GetOrCreate runs per send, so warning unconditionally floods the log at message rate.
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Publisher and listener disagree about the shape of the same named channel.
                ["Destinations:mismatch:Capacity"] = "5",
                ["Sources:mismatch:Capacity"] = "50"
            })
            .Build();

        var warnings = new CapturingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(warnings).SetMinimumLevel(LogLevel.Debug));
        services.AddChannelListener<Order>(config);
        services.AddChannelPublisher<Order>(config);

        await using ServiceProvider provider = services.BuildServiceProvider();

        // The listener resolves the name first, so the publisher's shape is the one ignored.
        await provider.GetRequiredService<IMessageListener<Order>>()
            .SubscribeAsync("mismatch", _ => Task.FromResult(true), CancellationToken.None);

        var publisher = provider.GetRequiredService<IMessagePublisher<Order>>();
        for (int i = 0; i < 5; i++)
        {
            await publisher.SendAsync(new Order($"m{i}", 1), "mismatch");
        }

        Assert.Equal(1, warnings.Count(LogLevel.Warning, "different shape"));
    }

    [Fact]
    public async Task SendToAFullBoundedChannelTimesOutRatherThanHanging()
    {
        // FullMode.Wait plus no CancellationToken on SendAsync means a full channel with no
        // consumer blocks the publisher forever. SendTimeoutMilliseconds bounds that wait.
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SendTimeoutMilliseconds"] = "250",
                ["Destinations:tiny:Capacity"] = "1",
                ["Destinations:tiny:FullMode"] = "Wait"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddChannelPublisher<Order>(config);

        await using ServiceProvider provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IMessagePublisher<Order>>();

        // Nobody is reading, so the second send has nowhere to go.
        await publisher.SendAsync(new Order("first", 1), "tiny");

        await Assert.ThrowsAsync<TimeoutException>(() => publisher.SendAsync(new Order("second", 1), "tiny"));
    }

    [Fact]
    public async Task SendWaitsIndefinitelyByDefault()
    {
        // The timeout is opt-in: without it the historical blocking behaviour is unchanged.
        var (publisher, listener, provider) = Build();
        await using var _ = provider;

        var send = publisher.SendAsync(new Order("first", 1), Source);

        Assert.True(await Wait.CompletesWithin(send, 2000), "an unbounded channel should accept the message immediately");
    }

    [Fact]
    public async Task SubscribeToUnknownSourceThrowsWithConfiguredNames()
    {
        var (_, listener, provider) = Build();
        await using var __ = provider;

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => listener.SubscribeAsync("nope", _ => Task.FromResult(true), CancellationToken.None));

        Assert.Contains("nope", ex.Message);
        Assert.Contains(Source, ex.Message);
    }
}
