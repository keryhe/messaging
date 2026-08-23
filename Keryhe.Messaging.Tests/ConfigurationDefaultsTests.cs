using Keryhe.Messaging.AWS;
using Keryhe.Messaging.IO;
using Keryhe.Messaging.Polling.Delay;
using Keryhe.Messaging.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Keryhe.Messaging.Tests;

/// <summary>
/// Options classes whose int/reference defaults were not usable values: the "obvious" configuration
/// either crashed at construction or produced a request SQS rejects / a delay that does not delay.
/// </summary>
public class ConfigurationDefaultsTests
{
    [Fact]
    public void RabbitMQListenerConstructsWithoutAFactorySection()
    {
        // R8: RabbitMQOptions.Factory had no initializer, so options bound from a configuration
        // with no "Factory" section — or the registration overloads that take no IConfiguration —
        // left it null and the constructor dereferenced it.
        var listener = new RabbitMQListener<Order>(
            Options.Create(new RabbitMQListenerOptions()),
            NullLogger<RabbitMQListener<Order>>.Instance);

        Assert.NotNull(listener);
    }

    [Fact]
    public void RabbitMQPublisherConstructsWithoutAFactorySection()
    {
        var publisher = new RabbitMQPublisher<Order>(
            Options.Create(new RabbitMQPublisherOptions()),
            NullLogger<RabbitMQPublisher<Order>>.Instance);

        Assert.NotNull(publisher);
    }

    [Fact]
    public void RabbitMQOptionsBindWithAnExplicitlyNullFactory()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Factory"] = null })
            .Build();

        var options = new RabbitMQListenerOptions();
        config.Bind(options);

        // Whatever binding leaves behind, construction must survive it.
        var listener = new RabbitMQListener<Order>(Options.Create(options), NullLogger<RabbitMQListener<Order>>.Instance);
        Assert.NotNull(listener);
    }

    [Fact]
    public void RabbitMQFactoryDefaultsAreUsableAsPublished()
    {
        // The README documents these values; they are also what an omitted section should give.
        var factory = new FactoryOptions();

        Assert.Equal("guest", factory.UserName);
        Assert.Equal("/", factory.VirtualHost);
        Assert.Equal("localhost", factory.HostName);
        Assert.Equal(5672, factory.Port);
    }

    [Fact]
    public void SqsSourceDefaultsAreValidForSqs()
    {
        // S7: both were plain ints defaulting to 0. MaxNumberOfMessages=0 is rejected by SQS
        // outright, and WaitTimeSeconds=0 is short polling — a billed request per empty poll.
        var options = new SQSSourceOptions();

        Assert.InRange(options.MaxNumberOfMessages, 1, 10);
        Assert.InRange(options.WaitTimeSeconds, 1, 20);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task SqsListenerRejectsAnOutOfRangeMaxNumberOfMessages(int maxNumberOfMessages)
    {
        var options = new SQSListenerOptions
        {
            Region = "us-east-1",
            AccessKey = "fake",
            SecretKey = "fake",
            Sources = new()
            {
                ["orders"] = new SQSSourceOptions
                {
                    QueueUrl = "https://example.invalid/q",
                    MaxNumberOfMessages = maxNumberOfMessages
                }
            }
        };

        await using var listener = new SQSListener<Order>(options, NullLogger<SQSListener<Order>>.Instance);

        // Fails at subscribe with a readable message rather than on every receive with an opaque
        // InvalidParameterValue from SQS.
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => listener.SubscribeAsync("orders", _ => Task.FromResult(true), CancellationToken.None));

        Assert.Contains("orders", ex.Message);
        Assert.Contains("1 to 10", ex.Message);
    }

    [Fact]
    public async Task SqsListenerRejectsAnOutOfRangeWaitTime()
    {
        var options = new SQSListenerOptions
        {
            Region = "us-east-1",
            AccessKey = "fake",
            SecretKey = "fake",
            Sources = new()
            {
                ["orders"] = new SQSSourceOptions { QueueUrl = "https://example.invalid/q", WaitTimeSeconds = 21 }
            }
        };

        await using var listener = new SQSListener<Order>(options, NullLogger<SQSListener<Order>>.Instance);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => listener.SubscribeAsync("orders", _ => Task.FromResult(true), CancellationToken.None));

        Assert.Contains("0 to 20", ex.Message);
    }

    [Fact]
    public void FileSystemListenerIntervalDefaultsToAUsableValue()
    {
        // F9: a plain int Interval left at 0 makes Task.Delay(TimeSpan.Zero) a completed task, so
        // the scan loop never throttles at all — the same failure as S7 and G5, on a third provider.
        Assert.True(new FileSystemListenerSourceOptions().Interval > 0);
    }

    [Fact]
    public void DelayOptionDefaultsProduceARealDelay()
    {
        // G5: every one of these defaulted to 0, which makes Wait() a no-op and the poll loop a
        // spin — the same production symptom as G4, from a different cause.
        Assert.True(new ConstantOptions().Interval > 0);
        Assert.True(new ExponentialOptions().Factor > 1);
        Assert.True(new ExponentialOptions().MaxWait > 0);
        Assert.True(new LinearOptions().Increment > 0);
        Assert.True(new LinearOptions().MaxWait > 0);
        Assert.True(new FibonacciOptions().MaxWait > 0);
    }

    [Fact]
    public async Task AZeroIntervalStillWaits()
    {
        // Defaults do not help a configuration that sets 0 explicitly, so Wait() floors it.
        using var delay = new ConstantDelay(new ConstantOptions { Interval = 0 }, NullLogger<ConstantDelay>.Instance);

        var waiter = Task.Run(() => delay.Wait());

        Assert.False(await Wait.CompletesWithin(waiter, 400), "a zero interval must not spin");

        delay.Cancel();
        await waiter;
    }

    [Fact]
    public async Task AZeroFactorDoesNotCollapseTheExponentialBackoff()
    {
        using var delay = new ExponentialDelay(
            new ExponentialOptions { Factor = 0, MaxWait = 60 },
            NullLogger<ExponentialDelay>.Instance);

        // Drive the backoff through a couple of iterations; with Factor 0 the wait multiplies to
        // zero and never recovers.
        delay.Cancel();
        delay.Wait();
        delay.Cancel();
        delay.Wait();

        var waiter = Task.Run(() => delay.Wait());

        Assert.False(await Wait.CompletesWithin(waiter, 400), "a zero factor must not turn the backoff into a spin");

        delay.Cancel();
        await waiter;
    }
}
