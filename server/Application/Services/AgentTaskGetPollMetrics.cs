namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1972. In-memory counts of <c>GET /api/agent-tasks/{id}</c> for this process.
/// The route passes the header and the caller it already resolved. This type does not
/// query Postgres and does not retain tokens, task ids, or SQL.
/// </summary>
public sealed class AgentTaskGetPollMetrics
{
    public const string CallerHeader = "X-Antiphon-Caller";
    public const string DiagnosticsPath = "/agent-task-polls";
    public const string UnknownLabel = "unknown";
    public const int MaxLabels = 64;

    private readonly object _gate = new();
    private readonly Dictionary<string, long> _labels = new(StringComparer.Ordinal);
    private long _total;
    private long _unknown;
    private long _overflow;

    public AgentTaskGetPollMetrics()
    {
        StartedAt = DateTimeOffset.UtcNow;
    }

    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// A usable <paramref name="callerHeader"/> wins. Otherwise a resolved task token is
    /// <c>kind:&lt;AgentKind&gt;</c>, a capability token is <c>capability</c> or
    /// <c>capability:&lt;name&gt;</c>, and a session token with no task is <c>session</c>.
    /// Anything else is <see cref="UnknownLabel"/>.
    /// </summary>
    public static string Label(string? callerHeader, AgentTaskService.Caller? caller)
    {
        if (TryScript(callerHeader, out var script)) return script;
        if (caller?.Task is { } task) return "kind:" + task.AgentKind.ToString();
        if (caller?.CapabilityId is not null)
        {
            if (TryScript(caller.CapabilityName, out var name)) return "capability:" + name;
            return "capability";
        }
        if (caller?.SessionId is not null) return "session";
        return UnknownLabel;
    }

    public void Record(string? callerHeader, AgentTaskService.Caller? caller)
    {
        var label = Label(callerHeader, caller);
        lock (_gate)
        {
            _total++;
            if (label == UnknownLabel)
            {
                _unknown++;
                return;
            }
            if (_labels.TryGetValue(label, out var count))
            {
                _labels[label] = count + 1;
                return;
            }
            if (_labels.Count >= MaxLabels)
            {
                _overflow++;
                return;
            }
            _labels[label] = 1;
        }
    }

    public Report Read()
    {
        lock (_gate)
        {
            var callers = _labels
                .Select(pair => new CallerRow(pair.Key, pair.Value))
                .OrderByDescending(row => row.Count)
                .ThenBy(row => row.Label, StringComparer.Ordinal)
                .ToArray();
            return new Report(StartedAt, _total, _unknown, _overflow, callers);
        }
    }

    /// <summary>
    /// Script or client name: an ASCII letter, then up to 63 letters, digits, <c>.</c>, <c>_</c>,
    /// or <c>-</c>. A guid, a 32-character-or-longer hex string, and the word <c>unknown</c> are refused.
    /// </summary>
    private static bool TryScript(string? raw, out string label)
    {
        label = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var text = raw.Trim().ToLowerInvariant();
        if (text.Length is < 1 or > 64 || text == UnknownLabel) return false;
        if (!char.IsAsciiLetter(text[0])) return false;
        foreach (var c in text)
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-') continue;
            return false;
        }
        if (Guid.TryParseExact(text, "D", out _)) return false;
        if (text.Length >= 32 && text.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        label = text;
        return true;
    }

    public sealed record Report(
        DateTimeOffset StartedAt,
        long Total,
        long Unknown,
        long Overflow,
        IReadOnlyList<CallerRow> Callers);

    public sealed record CallerRow(string Label, long Count);
}
