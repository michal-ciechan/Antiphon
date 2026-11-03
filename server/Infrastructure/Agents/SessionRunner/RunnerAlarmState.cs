using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed record RunnerOutageEpisode(
    string RunnerId,
    string DisplayName,
    DateTimeOffset DownSince,
    string? LastReason,
    DateTimeOffset? RaisedAt,
    int PinnedOpenTasks,
    int LiveSessions,
    IReadOnlyList<Guid> NotifiedSessionIds,
    IReadOnlyList<NotedAlarmRow> NotedRows);

public sealed record NotedAlarmRow(Guid SessionId, Guid RowId);

/// <summary>
/// A recovery note that has not reached <c>Sent</c>. <see cref="RowId"/> is null until the insert
/// succeeds. The episode is already gone; this list is what the next wake retries (DP-2 H1/H2).
/// </summary>
public sealed record PendingRecoveryNote(
    string RunnerId,
    Guid SessionId,
    Guid? RowId,
    string Header,
    string Body);

/// <summary>One inspected child-journal record. Round 2's feed row reads <see cref="WrittenAt"/>.</summary>
public sealed record JournalAlarmRecord(
    string File,
    JournalRecordState State,
    TimeSpan Age,
    int? ProcessId,
    DateTimeOffset WrittenAt)
{
    /// <summary>The inspector's age and state verdict; the feed must not recalculate it.</summary>
    public bool Stale { get; init; }
}

public sealed record JournalFinding(
    string Repository,
    string CommonDirectory,
    int StaleCount,
    DateTimeOffset InspectedAt)
{
    public IReadOnlyList<JournalAlarmRecord> Records { get; init; } = [];
}

public sealed record RunnerAlarmSnapshot(
    IReadOnlyList<RunnerOutageEpisode> Episodes,
    IReadOnlyDictionary<string, DateTimeOffset> LastResolvedAt,
    IReadOnlyList<JournalFinding> Journals,
    IReadOnlyList<PendingRecoveryNote> PendingRecoveries)
{
    /// <summary>
    /// CARD-1671: when each drain that still hides a runner reaches <c>Alarms:RunnerDrainMaxMinutes</c>.
    /// The alarm loop wakes just past the earliest one so an offline drain raises at its bound.
    /// </summary>
    public IReadOnlyDictionary<string, DateTimeOffset> DrainExpiresAt { get; init; } =
        new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

    public static RunnerAlarmSnapshot Empty { get; } = new(
        [], new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal), [], []);
}

/// <summary>CARD-0726 D-3: the in-memory episodes and journal findings the feed will project.</summary>
public sealed class RunnerAlarmState
{
    private RunnerAlarmSnapshot _current = RunnerAlarmSnapshot.Empty;

    public RunnerAlarmSnapshot Current => Volatile.Read(ref _current);

    public void Publish(RunnerAlarmSnapshot snapshot) => Interlocked.Exchange(ref _current, snapshot);
}
