using Keryhe.Messaging.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Keryhe.Messaging.Tests;

/// <summary>
/// The IO provider needs nothing but a temp directory, so F1/F2/F3 are all directly testable.
/// </summary>
public class FileSystemTests
{
    private const string Source = "orders";

    private static FileSystemListener<Order> BuildListener(TempFolder folder, ServiceProvider provider, string? errorFolder = null)
        => BuildListener(folder, provider, new CapturingLogger<FileSystemListener<Order>>(), errorFolder);

    private static FileSystemListener<Order> BuildListener(TempFolder folder, ServiceProvider provider, ILogger<FileSystemListener<Order>> logger, string? errorFolder = null)
    {
        var options = new FileSystemListenerOptions
        {
            Sources = new()
            {
                [Source] = new FileSystemListenerSourceOptions
                {
                    Folder = folder.Inbox,
                    FileType = "json",
                    CompletedFolder = folder.Completed,
                    ErrorFolder = errorFolder ?? folder.Error,
                    Interval = 1
                }
            }
        };

        return new FileSystemListener<Order>(Options.Create(options), provider, logger);
    }

    private static void WriteInboxFile(TempFolder folder, string name, string contents) =>
        File.WriteAllText(Path.Combine(folder.Inbox, name + ".json"), contents);

    [Fact]
    public async Task FailedFileGoesToErrorFolderOnly()
    {
        // F1: the missing else moved the file to ErrorFolder and then tried to move it again from
        // a path that no longer existed, throwing out of Run and killing the listener.
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();

        var logger = new CapturingLogger<FileSystemListener<Order>>();
        await using var listener = BuildListener(folder, provider, logger);

        WriteInboxFile(folder, "one", """{"Id":"one","Quantity":1}""");

        await listener.SubscribeAsync(Source, _ => Task.FromResult(false), CancellationToken.None);

        await Wait.ForAssert(() => folder.CountRecursive(folder.Error) == 1, "the failed file should land in ErrorFolder");

        Assert.Equal(0, folder.CountRecursive(folder.Completed));
        Assert.Equal(0, folder.CountRecursive(folder.Inbox));

        // The missing `else` moved the file to ErrorFolder and then tried to move it a second time
        // from a path that no longer existed. The files end up in the same place either way, so the
        // only thing that distinguishes the bug is the failed second move.
        await Task.Delay(100);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task SuccessfulFileGoesToCompletedFolderOnly()
    {
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();
        await using var listener = BuildListener(folder, provider);

        WriteInboxFile(folder, "one", """{"Id":"one","Quantity":1}""");

        await listener.SubscribeAsync(Source, _ => Task.FromResult(true), CancellationToken.None);

        await Wait.ForAssert(() => folder.CountRecursive(folder.Completed) == 1, "the handled file should land in CompletedFolder");

        Assert.Equal(0, folder.CountRecursive(folder.Error));
    }

    [Fact]
    public async Task MalformedFileDoesNotStopTheListener()
    {
        // F2: a malformed file threw out of Run, and because the file stayed in the folder the
        // listener died again immediately on every restart.
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();
        await using var listener = BuildListener(folder, provider);

        var handled = new ConcurrentQueue<string>();

        WriteInboxFile(folder, "bad", "this is not json at all");

        await listener.SubscribeAsync(Source, order =>
        {
            handled.Enqueue(order.Id);
            return Task.FromResult(true);
        }, CancellationToken.None);

        await Wait.ForAssert(() => folder.CountRecursive(folder.Error) == 1, "the malformed file should land in ErrorFolder");

        // The listener must still be alive: a good file arriving afterwards is processed.
        WriteInboxFile(folder, "good", """{"Id":"good","Quantity":2}""");

        await Wait.ForAssert(() => handled.Contains("good"), "the listener must keep running after a malformed file");
        Assert.Equal(1, folder.CountRecursive(folder.Completed));
    }

    [Fact]
    public async Task NullPayloadIsTreatedAsAFailureNotAsAMessage()
    {
        // X2: "null" deserializes to null, which used to be handed to the handler as a message.
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();
        await using var listener = BuildListener(folder, provider);

        var handlerCalls = 0;

        WriteInboxFile(folder, "nothing", "null");

        await listener.SubscribeAsync(Source, _ =>
        {
            Interlocked.Increment(ref handlerCalls);
            return Task.FromResult(true);
        }, CancellationToken.None);

        await Wait.ForAssert(() => folder.CountRecursive(folder.Error) == 1, "a null payload should be routed to ErrorFolder");

        Assert.Equal(0, Volatile.Read(ref handlerCalls));
        Assert.Equal(0, folder.CountRecursive(folder.Completed));
    }

    [Fact]
    public async Task FailedFileIsDeletedWhenNoErrorFolderIsConfigured()
    {
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();
        await using var listener = BuildListener(folder, provider, errorFolder: "");

        WriteInboxFile(folder, "one", """{"Id":"one","Quantity":1}""");

        await listener.SubscribeAsync(Source, _ => Task.FromResult(false), CancellationToken.None);

        await Wait.ForAssert(() => folder.CountRecursive(folder.Inbox) == 0, "the failed file should be removed from the inbox");

        Assert.Equal(0, folder.CountRecursive(folder.Completed));
        Assert.Equal(0, folder.CountRecursive(folder.Error));
    }

    [Fact]
    public async Task PayloadIsNotVisibleUntilItIsCompleteAndItsSidecarHasLanded()
    {
        // F3: the publisher used to write the payload straight to its final name, so the listener's
        // glob could pick it up mid-write (truncated JSON) or before the trace sidecar existed.
        using var folder = new TempFolder();

        var blocking = new BlockingFileSerializer<Order>();
        await using var provider = TestServices.ForFileSystem<Order>(blocking);

        var options = new FileSystemPublisherOptions
        {
            Destinations = new()
            {
                ["orders"] = new FileSystemDestinationOptions { Folder = folder.Inbox, FileType = "json" }
            }
        };

        await using var publisher = new FileSystemPublisher<Order>(Options.Create(options), provider);

        Task send = publisher.SendAsync(new Order("one", 1), "orders");

        // Mid-write: the payload exists on disk under a temp name, but must be invisible to the
        // listener's "*.json" glob.
        await blocking.WriteStarted;
        Assert.Empty(Directory.GetFiles(folder.Inbox, "*.json"));

        blocking.Release();
        await send;

        string[] payloads = Directory.GetFiles(folder.Inbox, "*.json");
        Assert.Single(payloads);

        // No temp file left behind, and the payload is complete.
        Assert.Empty(Directory.GetFiles(folder.Inbox, "*.tmp"));
        Assert.Contains("\"Id\":\"one\"", await File.ReadAllTextAsync(payloads[0]));
    }

    [Fact]
    public async Task AFileNameAlreadyUsedInTheDestinationDoesNotLivelockTheListener()
    {
        // A name collision made File.Move throw on every pass, leaving the file in the inbox to be
        // handled again and again — the handler would be re-invoked forever.
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();

        var logger = new CapturingLogger<FileSystemListener<Order>>();
        await using var listener = BuildListener(folder, provider, logger);

        // Pre-place a file of the same name in today's completed subfolder. Both today's and
        // tomorrow's are seeded so the test cannot fail if the run straddles midnight.
        foreach (DateTime day in new[] { DateTime.Now, DateTime.Now.AddDays(1) })
        {
            string dated = Path.Combine(folder.Completed, day.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dated);
            File.WriteAllText(Path.Combine(dated, "one.json"), "an earlier file with the same name");
        }

        WriteInboxFile(folder, "one", """{"Id":"one","Quantity":1}""");

        var handlerCalls = 0;
        await listener.SubscribeAsync(Source, _ =>
        {
            Interlocked.Increment(ref handlerCalls);
            return Task.FromResult(true);
        }, CancellationToken.None);

        await Wait.ForAssert(() => folder.CountRecursive(folder.Inbox) == 0, "the file must leave the inbox even when its name is taken");

        // The two seeded files plus the moved one: nothing was overwritten.
        Assert.Equal(3, folder.CountRecursive(folder.Completed));

        await Task.Delay(1500);
        Assert.Equal(1, Volatile.Read(ref handlerCalls));
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task SubscribingTwiceDoesNotProcessTheSameFileTwice()
    {
        // The second subscribe overwrote the first source's CancellationTokenSource without
        // cancelling it, leaving two loops scanning the same folder — so one file was handled
        // twice, with whatever side effects the handler has.
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();
        await using var listener = BuildListener(folder, provider);

        var handlerCalls = 0;
        Task<bool> Handler(Order _)
        {
            Interlocked.Increment(ref handlerCalls);
            return Task.FromResult(true);
        }

        await listener.SubscribeAsync(Source, Handler, CancellationToken.None);
        await listener.SubscribeAsync(Source, Handler, CancellationToken.None);

        WriteInboxFile(folder, "one", """{"Id":"one","Quantity":1}""");

        await Wait.ForAssert(() => folder.CountRecursive(folder.Completed) == 1, "the file should be handled and moved");

        // Give an orphaned second loop time to pick the file up as well.
        await Task.Delay(1500);
        Assert.Equal(1, Volatile.Read(ref handlerCalls));
    }

    [Fact]
    public async Task AMissingFolderDoesNotKillTheListener()
    {
        // Directory.GetFiles was unguarded, so a folder deleted at runtime ended the loop for the
        // life of the process.
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();
        await using var listener = BuildListener(folder, provider);

        var handled = new ConcurrentQueue<string>();

        await listener.SubscribeAsync(Source, order =>
        {
            handled.Enqueue(order.Id);
            return Task.FromResult(true);
        }, CancellationToken.None);

        // Delete it out from under the running loop — SubscribeAsync recreates missing folders, so
        // removing it beforehand would prove nothing.
        WriteInboxFile(folder, "first", """{"Id":"first","Quantity":1}""");
        await Wait.ForAssert(() => handled.Contains("first"), "the listener should be running");

        Directory.Delete(folder.Inbox, recursive: true);

        // Let the loop hit the missing folder at least once, then restore it.
        await Task.Delay(1500);
        Directory.CreateDirectory(folder.Inbox);
        WriteInboxFile(folder, "one", """{"Id":"one","Quantity":1}""");

        await Wait.ForAssert(() => handled.Contains("one"), "the listener must recover once the folder is back");
    }

    [Fact]
    public async Task CorruptTraceSidecarDoesNotFailAGoodPayload()
    {
        // R7's IO face: the sidecar was read outside any guard, so a corrupt trace file sent a
        // perfectly good message to the error folder.
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();
        await using var listener = BuildListener(folder, provider);

        var handled = new ConcurrentQueue<string>();

        WriteInboxFile(folder, "one", """{"Id":"one","Quantity":1}""");
        File.WriteAllText(Path.Combine(folder.Inbox, "one.json.trace"), "{ this is not valid json");

        await listener.SubscribeAsync(Source, order =>
        {
            handled.Enqueue(order.Id);
            return Task.FromResult(true);
        }, CancellationToken.None);

        await Wait.ForAssert(() => handled.Contains("one"), "a corrupt sidecar must not stop the payload being handled");

        Assert.Equal(1, folder.CountRecursive(folder.Completed));
        Assert.Equal(0, folder.CountRecursive(folder.Error));
    }

    [Fact]
    public async Task MalformedTraceParentInSidecarDoesNotFailAGoodPayload()
    {
        // The sidecar parses as JSON and the traceparent has the right number of dash-separated
        // parts, so it reaches the parser — but the ids are not valid hex of the right length,
        // which is what makes ActivityTraceId.CreateFromString throw.
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();
        await using var listener = BuildListener(folder, provider);

        var handled = new ConcurrentQueue<string>();

        WriteInboxFile(folder, "one", """{"Id":"one","Quantity":1}""");
        File.WriteAllText(Path.Combine(folder.Inbox, "one.json.trace"), """{"TraceParent":"00-abc-def-01"}""");

        await listener.SubscribeAsync(Source, order =>
        {
            handled.Enqueue(order.Id);
            return Task.FromResult(true);
        }, CancellationToken.None);

        await Wait.ForAssert(() => handled.Contains("one"), "a malformed traceparent must not stop the payload being handled");

        Assert.Equal(0, folder.CountRecursive(folder.Error));
    }

    [Fact]
    public async Task AnExplicitZeroIntervalStillThrottlesTheScanLoop()
    {
        // F9: the default alone does not help a configuration that sets Interval to 0 explicitly —
        // Task.Delay(TimeSpan.Zero) is a completed task, so the loop would spin between scans.
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();

        var options = new FileSystemListenerOptions
        {
            Sources = new()
            {
                [Source] = new FileSystemListenerSourceOptions
                {
                    Folder = folder.Inbox,
                    FileType = "json",
                    CompletedFolder = folder.Completed,
                    ErrorFolder = folder.Error,
                    Interval = 0
                }
            }
        };

        await using var listener = new FileSystemListener<Order>(Options.Create(options), provider, TestServices.Logger<FileSystemListener<Order>>());

        var handled = new List<DateTime>();
        await listener.SubscribeAsync(Source, _ =>
        {
            handled.Add(DateTime.UtcNow);
            return Task.FromResult(true);
        }, CancellationToken.None);

        WriteInboxFile(folder, "first", """{"Id":"first","Quantity":1}""");
        await Wait.ForAssert(() => handled.Count == 1, "the first file should be handled");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        WriteInboxFile(folder, "second", """{"Id":"second","Quantity":1}""");
        await Wait.ForAssert(() => handled.Count == 2, "the second file should be handled");
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 800,
            $"an Interval of 0 must still floor at roughly one second between scans, took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task SubscribeToUnknownSourceThrowsWithConfiguredNames()
    {
        using var folder = new TempFolder();
        await using var provider = TestServices.ForFileSystem<Order>();
        await using var listener = BuildListener(folder, provider);

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => listener.SubscribeAsync("nope", _ => Task.FromResult(true), CancellationToken.None));

        Assert.Contains("nope", ex.Message);
        Assert.Contains(Source, ex.Message);
    }
}
