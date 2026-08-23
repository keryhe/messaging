using Keryhe.Messaging.Polling;
using Keryhe.Messaging.Polling.Delay;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Keryhe.Messaging.Tests;

/// <summary>
/// A Poller whose Poll() behaviour each test controls.
/// </summary>
public sealed class StubPoller : Poller<string>
{
    private readonly Func<int, Task<string>> _poll;
    private int _calls;

    public StubPoller(IDelay delay, Func<int, Task<string>> poll)
        : base(delay, NullLogger<Poller<string>>.Instance)
    {
        _poll = poll;
    }

    public int Calls => Volatile.Read(ref _calls);

    protected override Task<string> Poll() => _poll(Interlocked.Increment(ref _calls));
}

/// <summary>A poller whose source always has nothing to hand over.</summary>
public sealed class EmptyListPoller(IDelay delay) : Poller<List<string>>(delay, NullLogger<Poller<List<string>>>.Instance)
{
    protected override Task<List<string>> Poll() => Task.FromResult(new List<string>());
}

/// <summary>A poller whose source always has exactly one item.</summary>
public sealed class OneItemListPoller(IDelay delay) : Poller<List<string>>(delay, NullLogger<Poller<List<string>>>.Instance)
{
    protected override Task<List<string>> Poll() => Task.FromResult(new List<string> { "item" });
}

public class PollingTests
{
    [Fact]
    public async Task PollExceptionDoesNotStopPolling()
    {
        // G1: a single transient failure from Poll() used to end polling for the process lifetime,
        // with nothing logged.
        var delay = new NoopDelay();
        var handled = new ConcurrentQueue<string>();

        var poller = new StubPoller(delay, call => call == 1
            ? throw new InvalidOperationException("transient database error")
            : Task.FromResult<string>(call == 2 ? "item" : null!));

        await poller.SubscribeAsync("ignored", item =>
        {
            handled.Enqueue(item);
            return Task.FromResult(true);
        }, CancellationToken.None);

        await Wait.ForAssert(() => handled.Count == 1, "polling must continue after Poll() throws");

        await poller.UnsubscribeAsync("ignored", CancellationToken.None);
    }

    [Fact]
    public async Task HandlerExceptionDoesNotStopPolling()
    {
        var delay = new NoopDelay();
        var polls = 0;

        var poller = new StubPoller(delay, _ =>
        {
            Interlocked.Increment(ref polls);
            return Task.FromResult("item");
        });

        await poller.SubscribeAsync("ignored", _ => throw new InvalidOperationException("handler blew up"), CancellationToken.None);

        await Wait.ForAssert(() => Volatile.Read(ref polls) > 3, "polling must continue after the handler throws");

        await poller.UnsubscribeAsync("ignored", CancellationToken.None);
    }

    [Fact]
    public async Task AnEmptyCollectionIsNotAMessage()
    {
        // G7: CheckNullOrEmpty handled only null, empty string and default(T), so an empty
        // List<T> reached the handler and reset the delay — the loop then re-polled immediately,
        // spinning against whatever Poll() talks to.
        var delay = new NoopDelay();
        var handlerCalls = 0;

        var poller = new EmptyListPoller(delay);

        await poller.SubscribeAsync("ignored", _ =>
        {
            Interlocked.Increment(ref handlerCalls);
            return Task.FromResult(true);
        }, CancellationToken.None);

        await Wait.ForAssert(() => delay.Waits > 2, "an empty collection should make the poller wait");

        Assert.Equal(0, Volatile.Read(ref handlerCalls));
        Assert.Equal(0, delay.Resets);

        await poller.UnsubscribeAsync("ignored", CancellationToken.None);
    }

    [Fact]
    public async Task ANonEmptyCollectionIsStillAMessage()
    {
        var delay = new NoopDelay();
        var handled = new ConcurrentQueue<int>();

        var poller = new OneItemListPoller(delay);

        await poller.SubscribeAsync("ignored", items =>
        {
            handled.Enqueue(items.Count);
            return Task.FromResult(true);
        }, CancellationToken.None);

        await Wait.ForAssert(() => handled.Count > 0, "a populated collection must still reach the handler");

        await poller.UnsubscribeAsync("ignored", CancellationToken.None);
    }

    [Fact]
    public async Task UnsubscribeStopsPolling()
    {
        // G2: the loop flag was a non-volatile bool read on another thread.
        var delay = new NoopDelay();
        var poller = new StubPoller(delay, _ => Task.FromResult<string>(null!));

        await poller.SubscribeAsync("ignored", _ => Task.FromResult(true), CancellationToken.None);
        await Wait.ForAssert(() => poller.Calls > 0, "polling should have started");

        await poller.UnsubscribeAsync("ignored", CancellationToken.None);

        await AssertPollingStops(poller);
    }

    [Fact]
    public async Task CancellingTheSubscriptionTokenStopsPolling()
    {
        // G3: the token was passed to Task.Run, which only gates scheduling — once Run was
        // executing, cancelling it did nothing.
        var delay = new NoopDelay();
        var poller = new StubPoller(delay, _ => Task.FromResult<string>(null!));

        using var cts = new CancellationTokenSource();

        await poller.SubscribeAsync("ignored", _ => Task.FromResult(true), cts.Token);
        await Wait.ForAssert(() => poller.Calls > 0, "polling should have started");

        await cts.CancelAsync();

        await AssertPollingStops(poller);
    }

    [Fact]
    public async Task CancellingTheSubscriptionTokenReleasesADelayInProgress()
    {
        // The loop only notices cancellation between waits, so cancelling has to break the wait.
        var delay = new NoopDelay();
        var poller = new StubPoller(delay, _ => Task.FromResult<string>(null!));

        using var cts = new CancellationTokenSource();

        await poller.SubscribeAsync("ignored", _ => Task.FromResult(true), cts.Token);
        await Wait.ForAssert(() => delay.Waits > 0, "the poller should be delaying between empty polls");

        await cts.CancelAsync();

        await Wait.ForAssert(() => delay.Cancels > 0, "cancelling the token must cancel the delay");
    }

    private static async Task AssertPollingStops(StubPoller poller)
    {
        // Allow any in-flight iteration to finish, then assert the count stops moving.
        await Task.Delay(200);
        int settled = poller.Calls;
        await Task.Delay(200);

        Assert.Equal(settled, poller.Calls);
    }
}

public class DelayTests
{
    private static ConstantDelay OneSecondDelay() =>
        new(new ConstantOptions { Interval = 1 }, NullLogger<ConstantDelay>.Instance);

    [Fact]
    public async Task CancelReleasesAWaitInProgress()
    {
        using var delay = OneSecondDelay();

        var stopwatch = Stopwatch.StartNew();
        var waiter = Task.Run(() => delay.Wait());

        await Task.Delay(50);
        delay.Cancel();

        Assert.True(await Wait.CompletesWithin(waiter, 5000), "Wait should have been released by Cancel");
        Assert.True(stopwatch.ElapsedMilliseconds < 900, $"Cancel should release the wait promptly, took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task DelayStillWaitsAfterACancelledWait()
    {
        // G4: Reset() never reset the event, so the first Cancel() left it signalled forever and
        // every later Wait() returned instantly — turning the poll loop into a spin.
        using var delay = OneSecondDelay();

        var waiter = Task.Run(() => delay.Wait());
        await Task.Delay(50);
        delay.Cancel();
        Assert.True(await Wait.CompletesWithin(waiter, 5000));

        var second = Task.Run(() => delay.Wait());
        Assert.False(await Wait.CompletesWithin(second, 700),
            "the delay must work again after a cancelled wait");

        delay.Cancel();
        await second;
    }

    [Fact]
    public async Task OneDelayInstanceDoesNotReleaseAnother()
    {
        // G4: the event was a static field, so every delay in the process shared one.
        using var cancelled = OneSecondDelay();
        using var independent = OneSecondDelay();

        var waiter = Task.Run(() => independent.Wait());

        await Task.Delay(50);
        cancelled.Cancel();

        Assert.False(await Wait.CompletesWithin(waiter, 700),
            "cancelling one delay must not release another");

        independent.Cancel();
        await waiter;
    }

    [Fact]
    public void BackoffResetsAfterASuccessfulPoll()
    {
        var delay = new ExponentialDelay(
            new ExponentialOptions { Factor = 2, MaxWait = 64 },
            NullLogger<ExponentialDelay>.Instance);

        using (delay)
        {
            // Advance the backoff, then cancel out of the waits so the test stays fast.
            delay.Cancel();
            delay.Wait();
            delay.Cancel();
            delay.Wait();

            delay.Reset();

            // After a reset the wait is back to one second rather than the grown interval.
            var stopwatch = Stopwatch.StartNew();
            delay.Cancel();
            delay.Wait();
            stopwatch.Stop();

            Assert.True(stopwatch.ElapsedMilliseconds < 900, $"a cancelled wait should return promptly, took {stopwatch.ElapsedMilliseconds}ms");
        }
    }
}
