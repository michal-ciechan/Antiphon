namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1972 red stub. The real counter replaces this: until then every read is empty,
/// so the witnesses fail on the counts.
/// </summary>
public sealed class AgentTaskGetPollMetrics
{
    public const string CallerHeader = "X-Antiphon-Caller";
    public const string DiagnosticsPath = "/agent-task-polls";
    public const string UnknownLabel = "unknown";
    public const int MaxLabels = 64;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.MinValue;

    public static string Label(string? callerHeader, AgentTaskService.Caller? caller) => UnknownLabel;

    public void Record(string? callerHeader, AgentTaskService.Caller? caller)
    {
    }

    public Report Read() => new(StartedAt, 0, 0, 0, []);

    public sealed record Report(
        DateTimeOffset StartedAt,
        long Total,
        long Unknown,
        long Overflow,
        IReadOnlyList<CallerRow> Callers);

    public sealed record CallerRow(string Label, long Count);
}
