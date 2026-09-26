using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Microsoft.Extensions.Logging;

namespace Antiphon.Tests.TestHelpers;

internal sealed class FakeEligibilitySource : IRunnerEligibilitySnapshotSource
{
    public List<RunnerEligibilitySnapshot> Rows { get; } = [];
    public int Calls;
    public int ThrowOnCall;
    public bool IneligibleAfterThrow;

    public IReadOnlyList<RunnerEligibilitySnapshot> Snapshots()
    {
        Calls++;
        if (ThrowOnCall != 0 && Calls == ThrowOnCall)
            throw new InvalidOperationException("eligibility source failed once");
        if (IneligibleAfterThrow && ThrowOnCall != 0 && Calls > ThrowOnCall)
            return Rows.Select(row => row with { Eligible = false, DisconnectReason = row.DisconnectReason ?? "transport_abort" }).ToList();
        return Rows.ToList();
    }
}

internal sealed class FakeExclusion : IRunnerAlarmExclusion
{
    public Dictionary<string, string?> Reasons { get; } = new(StringComparer.Ordinal);
    public string? ThrowFor { get; set; }

    public string? Excluded(string runnerId)
    {
        if (ThrowFor == runnerId)
            throw new InvalidOperationException("exclusion failed for " + runnerId);
        return Reasons.TryGetValue(runnerId, out var reason) ? reason : null;
    }
}

internal sealed class RecordingNotifier : IRunnerAlarmNotifier
{
    private readonly HashSet<Guid> _throwOnce = [];
    public List<Note> Notes { get; } = [];

    public void ThrowOnce(Guid sessionId) => _throwOnce.Add(sessionId);

    public Task<Guid> NotifyAsync(Guid sessionId, string header, string body, CancellationToken ct)
    {
        if (_throwOnce.Remove(sessionId))
            throw new InvalidOperationException("note failed for " + sessionId);
        Notes.Add(new Note(sessionId, header, body));
        return Task.FromResult(Guid.NewGuid());
    }

    internal sealed record Note(Guid SessionId, string Header, string Body);
}

internal sealed class RecordingEligibilityObserver : IRunnerEligibilityObserver
{
    public List<string> Ids { get; } = [];
    public void Changed(string runnerId) => Ids.Add(runnerId);
}

internal sealed class RecordingFenceObserver : IRepositoryFenceObserver
{
    public List<string> Commons { get; } = [];
    public void Fenced(string commonDirectory) => Commons.Add(commonDirectory);
}

/// <summary>Drops one 1-based <see cref="TryEnqueue"/> call and records every call.</summary>
internal sealed class DropNthFlushQueue : CompletionNoteFlushQueue
{
    private readonly int _dropAt;
    private int _calls;
    public List<Guid> Calls { get; } = [];

    public DropNthFlushQueue(int dropAt) => _dropAt = dropAt;

    public override bool TryEnqueue(Guid sessionId)
    {
        Calls.Add(sessionId);
        if (++_calls == _dropAt)
            return true;
        return base.TryEnqueue(sessionId);
    }
}

internal sealed class DroppingFlushQueue : CompletionNoteFlushQueue
{
    private int _remaining;
    public List<Guid> Calls { get; } = [];

    public DroppingFlushQueue(int dropFirst) => _remaining = dropFirst;

    public override bool TryEnqueue(Guid sessionId)
    {
        Calls.Add(sessionId);
        if (_remaining > 0)
        {
            _remaining--;
            return true;
        }

        return base.TryEnqueue(sessionId);
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<Entry> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, formatter(state, exception), exception));

    internal sealed record Entry(LogLevel Level, string Message, Exception? Exception);
}
