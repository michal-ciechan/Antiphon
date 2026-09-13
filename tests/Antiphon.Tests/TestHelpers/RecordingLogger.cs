using Microsoft.Extensions.Logging;

namespace Antiphon.Tests.TestHelpers;

internal sealed record RecordingLogEntry(LogLevel Level, string Message, Exception? Exception,
    IReadOnlyDictionary<string, object?> State);

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<RecordingLogEntry> Entries { get; }
    public RecordingLogger() : this([]) { }
    public RecordingLogger(List<RecordingLogEntry> entries) => Entries = entries;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var pairs = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (state is IEnumerable<KeyValuePair<string, object?>> kv)
        {
            foreach (var item in kv)
                pairs[item.Key] = item.Value;
        }
        Entries.Add(new(logLevel, formatter(state, exception), exception, pairs));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

internal sealed class PauseBarrier
{
    public TaskCompletionSource Paused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitPausedAsync() => Paused.Task.WaitAsync(TimeSpan.FromSeconds(30));
    public Task WaitReleaseAsync() => Release.Task.WaitAsync(TimeSpan.FromSeconds(30));
    public void SignalPaused() => Paused.TrySetResult();
    public void SignalRelease() => Release.TrySetResult();
}
