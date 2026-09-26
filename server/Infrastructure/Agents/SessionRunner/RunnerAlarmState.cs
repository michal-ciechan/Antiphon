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

public sealed record JournalFinding(
    string Repository,
    string CommonDirectory,
    int StaleCount,
    DateTimeOffset InspectedAt);

public sealed record RunnerAlarmSnapshot(
    IReadOnlyList<RunnerOutageEpisode> Episodes,
    IReadOnlyDictionary<string, DateTimeOffset> LastResolvedAt,
    IReadOnlyList<JournalFinding> Journals,
    IReadOnlyList<PendingRecoveryNote> PendingRecoveries)
{
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
