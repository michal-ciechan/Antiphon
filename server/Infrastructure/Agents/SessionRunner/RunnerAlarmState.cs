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

public sealed record JournalFinding(
    string Repository,
    string CommonDirectory,
    int StaleCount,
    DateTimeOffset InspectedAt);

public sealed record RunnerAlarmSnapshot(
    IReadOnlyList<RunnerOutageEpisode> Episodes,
    IReadOnlyDictionary<string, DateTimeOffset> LastResolvedAt,
    IReadOnlyList<JournalFinding> Journals)
{
    public static RunnerAlarmSnapshot Empty { get; } = new(
        [], new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal), []);
}

/// <summary>CARD-0726 D-3: the in-memory episodes and journal findings the feed will project.</summary>
public sealed class RunnerAlarmState
{
    private RunnerAlarmSnapshot _current = RunnerAlarmSnapshot.Empty;

    public RunnerAlarmSnapshot Current => Volatile.Read(ref _current);

    public void Publish(RunnerAlarmSnapshot snapshot) => Interlocked.Exchange(ref _current, snapshot);
}
