using Keryhe.Messaging.Channels.Extensions;
using Keryhe.Messaging.IO.Serialization;
using Keryhe.Messaging.Polling.Delay;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Keryhe.Messaging.Tests;

public record Order(string Id, int Quantity);

/// <summary>
/// A temporary directory that deletes itself at the end of the test.
/// </summary>
public sealed class TempFolder : IDisposable
{
    public string Root { get; }
    public string Inbox { get; }
    public string Completed { get; }
    public string Error { get; }

    public TempFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), "keryhe-tests-" + Guid.NewGuid().ToString("n"));
        Inbox = Path.Combine(Root, "inbox");
        Completed = Path.Combine(Root, "completed");
        Error = Path.Combine(Root, "error");

        Directory.CreateDirectory(Inbox);
        Directory.CreateDirectory(Completed);
        Directory.CreateDirectory(Error);
    }

    public int CountRecursive(string folder, string pattern = "*") =>
        Directory.Exists(folder) ? Directory.GetFiles(folder, pattern, SearchOption.AllDirectories).Length : 0;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A test leaving a file handle open must not fail the run.
        }
    }
}

/// <summary>
/// Polls a condition rather than sleeping a fixed amount, so tests are as fast as the code allows
/// and only pay the timeout when something is actually wrong.
/// </summary>
public static class Wait
{
    public static async Task<bool> Until(Func<bool> condition, int timeoutMilliseconds = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }

    public static async Task ForAssert(Func<bool> condition, string because, int timeoutMilliseconds = 15000)
    {
        Assert.True(await Until(condition, timeoutMilliseconds), because);
    }

    /// <summary>
    /// Whether a task finished inside the window. <see cref="IDelay"/> is a synchronous API, so
    /// delay tests have to run it on another thread and observe it without blocking this one.
    /// </summary>
    public static async Task<bool> CompletesWithin(Task task, int milliseconds)
    {
        Task winner = await Task.WhenAny(task, Task.Delay(milliseconds));
        return ReferenceEquals(winner, task);
    }
}

/// <summary>
/// An <see cref="IDelay"/> that does not actually delay, so polling tests run at full speed.
/// Records its calls so tests can assert on backoff behaviour.
/// </summary>
public sealed class NoopDelay : IDelay
{
    private int _waits;
    private int _resets;
    private int _cancels;

    public int Waits => Volatile.Read(ref _waits);
    public int Resets => Volatile.Read(ref _resets);
    public int Cancels => Volatile.Read(ref _cancels);

    public void Wait() => Interlocked.Increment(ref _waits);
    public void Reset() => Interlocked.Increment(ref _resets);
    public void Cancel() => Interlocked.Increment(ref _cancels);
}

/// <summary>
/// Wraps the real JSON serializer but blocks mid-write until released, so a test can inspect the
/// destination folder while a publish is genuinely in flight.
/// </summary>
public sealed class BlockingFileSerializer<T> : IFileSerializer<T>
{
    private readonly TaskCompletionSource _writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WriteStarted => _writeStarted.Task;

    public void Release() => _release.TrySetResult();

    public async Task SerializeAsync(T src, string path)
    {
        // Write a partial payload first: this is what a listener would pick up if the publisher
        // wrote straight to the final name.
        await File.WriteAllTextAsync(path, "{\"Id\":\"partial");

        _writeStarted.TrySetResult();
        await _release.Task;

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(src));
    }

    public async Task<T> DeserializeAsync(string path)
    {
        return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path))!;
    }
}

/// <summary>
/// Captures log entries so a test can assert that a path completed cleanly. Several fixes are only
/// distinguishable from the bug by what they log: the defensive try/catch added around the move in
/// batch 1 means a double-move leaves the same files on disk, and differs only in the logged error.
/// </summary>
public sealed class CapturingLogger<TCategory> : ILogger<TCategory>
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> _entries = new();

    public IReadOnlyCollection<(LogLevel Level, string Message, Exception? Exception)> Entries => _entries;

    public IEnumerable<(LogLevel Level, string Message, Exception? Exception)> Errors =>
        _entries.Where(e => e.Level >= LogLevel.Error);

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _entries.Enqueue((logLevel, formatter(state, exception), exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>
/// An <see cref="ILoggerProvider"/> that records everything, for asserting on log volume from
/// types the test cannot construct directly — ChannelRegistry is internal.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    public int Count(LogLevel level, string containing) =>
        _entries.Count(e => e.Level == level && e.Message.Contains(containing, StringComparison.OrdinalIgnoreCase));

    public ILogger CreateLogger(string categoryName) => new Sink(_entries);

    public void Dispose() { }

    private sealed class Sink(ConcurrentQueue<(LogLevel, string)> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

public static class TestServices
{
    /// <summary>
    /// A provider with the keyed file serializers the IO listener and publisher resolve per source.
    /// </summary>
    public static ServiceProvider ForFileSystem<T>(IFileSerializer<T>? jsonOverride = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (jsonOverride == null)
        {
            services.AddKeyedTransient<IFileSerializer<T>, JsonFileSerializer<T>>("json");
        }
        else
        {
            services.AddKeyedSingleton("json", jsonOverride);
        }

        services.AddKeyedTransient<IFileSerializer<T>, XmlFileSerializer<T>>("xml");

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds a channel publisher and listener through the package's own registration extensions.
    /// The channel registry is internal, so going through DI is both necessary and a better test:
    /// it exercises the real wiring, including the shared registry and the service lifetimes.
    /// </summary>
    public static ServiceProvider ForChannels<T>(string name)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Sources:{name}:SingleReader"] = "false",
                [$"Destinations:{name}:SingleWriter"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddChannelPublisher<T>(config);
        services.AddChannelListener<T>(config);

        return services.BuildServiceProvider();
    }

    public static ILogger<T> Logger<T>() => NullLogger<T>.Instance;
}
