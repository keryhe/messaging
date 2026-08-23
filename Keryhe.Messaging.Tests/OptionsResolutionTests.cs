using Keryhe.Messaging.AWS;
using Keryhe.Messaging.Azure;
using Keryhe.Messaging.RabbitMQ;
using Microsoft.Extensions.Options;

namespace Keryhe.Messaging.Tests;

/// <summary>
/// Every provider resolves a source/destination name before it touches its transport, so these
/// assert the "unknown name" diagnostic without needing a broker. The message must name both the
/// key that was asked for and the keys that are configured — that is the whole point of it.
/// </summary>
public class OptionsResolutionTests
{
    // Parseable but entirely fictional: nothing here is contacted, because Resolve throws first.
    private const string FakeServiceBusConnection =
        "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=fake;SharedAccessKey=Zm9vYmFyYmF6cXV1eA==";

    private static void AssertNamesReported(KeyNotFoundException ex, string configured)
    {
        Assert.Contains("nope", ex.Message);
        Assert.Contains(configured, ex.Message);
    }

    [Fact]
    public async Task RabbitMQListenerReportsConfiguredSources()
    {
        var options = new RabbitMQListenerOptions
        {
            Factory = new FactoryOptions(),
            Sources = new() { ["orders"] = new RabbitMQListenerSourceOptions() }
        };

        await using var listener = new RabbitMQListener<Order>(
            Options.Create(options), TestServices.Logger<RabbitMQListener<Order>>());

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => listener.SubscribeAsync("nope", _ => Task.FromResult(true), CancellationToken.None));

        AssertNamesReported(ex, "orders");
    }

    [Fact]
    public async Task RabbitMQPublisherReportsConfiguredDestinations()
    {
        var options = new RabbitMQPublisherOptions
        {
            Factory = new FactoryOptions(),
            Destinations = new() { ["audit"] = new RabbitMQDestinationOptions() }
        };

        await using var publisher = new RabbitMQPublisher<Order>(
            Options.Create(options), TestServices.Logger<RabbitMQPublisher<Order>>());

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => publisher.SendAsync(new Order("one", 1), "nope"));

        AssertNamesReported(ex, "audit");
    }

    [Fact]
    public async Task SqsListenerReportsConfiguredSources()
    {
        var options = new SQSListenerOptions
        {
            Region = "us-east-1",
            AccessKey = "fake",
            SecretKey = "fake",
            Sources = new() { ["orders"] = new SQSSourceOptions { QueueUrl = "https://example.invalid/q" } }
        };

        await using var listener = new SQSListener<Order>(options, TestServices.Logger<SQSListener<Order>>());

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => listener.SubscribeAsync("nope", _ => Task.FromResult(true), CancellationToken.None));

        AssertNamesReported(ex, "orders");
    }

    [Fact]
    public async Task SqsPublisherReportsConfiguredDestinations()
    {
        var options = new SQSPublisherOptions
        {
            Region = "us-east-1",
            AccessKey = "fake",
            SecretKey = "fake",
            Destinations = new() { ["audit"] = "https://example.invalid/q" }
        };

        await using var publisher = new SQSPublisher<Order>(options, TestServices.Logger<SQSPublisher<Order>>());

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => publisher.SendAsync(new Order("one", 1), "nope"));

        AssertNamesReported(ex, "audit");
    }

    [Fact]
    public async Task ServiceBusListenerReportsConfiguredSources()
    {
        var options = new ServiceBusListenerOptions
        {
            ConnectionString = FakeServiceBusConnection,
            Sources = new() { ["orders"] = new ServiceBusSourceOptions { QueueName = "orders" } }
        };

        await using var listener = new ServiceBusListener<Order>(options, TestServices.Logger<ServiceBusListener<Order>>());

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => listener.SubscribeAsync("nope", _ => Task.FromResult(true), CancellationToken.None));

        AssertNamesReported(ex, "orders");
    }

    [Fact]
    public async Task ServiceBusPublisherReportsConfiguredDestinations()
    {
        var options = new ServiceBusPublisherOptions
        {
            ConnectionString = FakeServiceBusConnection,
            Destinations = new() { ["audit"] = "audit-queue" }
        };

        await using var publisher = new ServiceBusPublisher<Order>(options, TestServices.Logger<ServiceBusPublisher<Order>>());

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => publisher.SendAsync(new Order("one", 1), "nope"));

        AssertNamesReported(ex, "audit");
    }
}
