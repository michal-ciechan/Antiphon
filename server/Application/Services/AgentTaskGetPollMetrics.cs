using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1972 / CARD-2004. In-memory counts of <c>GET /api/agent-tasks/{id}</c> for this process.
/// The route passes the header and the caller it already resolved. This type does not
/// query Postgres and does not retain the raw header, tokens, task ids, or SQL.
/// </summary>
public sealed class AgentTaskGetPollMetrics
{
    public const string CallerHeader = "X-Antiphon-Caller";
    public const string DiagnosticsPath = "/agent-task-polls";
    public const string UnknownLabel = "unknown";
    public const int MaxLabels = 64;

    /// <summary>
    /// CARD-2004. The only client-supplied <see cref="CallerHeader"/> values that are stored.
    /// Match is trim then lowercase. The raw header is never copied into a label, a log, or an error.
    /// Scripts: delegate.ps1, status-snapshot.ps1, checkpoint-task.ps1, card.ps1, board-watch.
    /// Client: useAgentTask, stored as useagenttask.
    /// </summary>
    public static readonly IReadOnlySet<string> RegisteredLabels = new HashSet<string>(StringComparer.Ordinal)
    {
        "board-watch",
        "card.ps1",
        "checkpoint-task.ps1",
        "delegate.ps1",
        "status-snapshot.ps1",
        "useagenttask",
    };

    private static readonly HashSet<string> KindLabels = new(
        Enum.GetNames<AgentKind>().Select(static name => "kind:" + name),
        StringComparer.Ordinal);

    private readonly object _gate = new();
    private readonly Dictionary<string, long> _server = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _capabilityNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _client = new(StringComparer.Ordinal);
    private long _total;
    private long _unknown;
    private long _overflow;

    public AgentTaskGetPollMetrics()
    {
        StartedAt = DateTimeOffset.UtcNow;
    }

    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// A registered <paramref name="callerHeader"/> wins. Otherwise a resolved task token is
    /// <c>kind:&lt;AgentKind&gt;</c>, a capability token is <c>capability</c> or
    /// <c>capability:&lt;name&gt;</c>, and a session token with no task is <c>session</c>.
    /// Anything else is <see cref="UnknownLabel"/>. The raw header is not returned.
    /// </summary>
    public static string Label(string? callerHeader, AgentTaskService.Caller? caller)
    {
        if (TryRegistered(callerHeader, out var script)) return script;
        return ServerLabel(caller);
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

            if (IsClosedServer(label))
            {
                Admit(_server, label, capped: false);
                return;
            }

            if (label.StartsWith("capability:", StringComparison.Ordinal))
            {
                Admit(_capabilityNames, label, capped: true);
                return;
            }

            Admit(_client, label, capped: true);
        }
    }

    public Report Read()
    {
        lock (_gate)
        {
            var callers = _server
                .Concat(_capabilityNames)
                .Concat(_client)
                .Select(pair => new CallerRow(pair.Key, pair.Value))
                .OrderByDescending(row => row.Count)
                .ThenBy(row => row.Label, StringComparer.Ordinal)
                .ToArray();
            return new Report(StartedAt, _total, _unknown, _overflow, callers);
        }
    }

    private void Admit(Dictionary<string, long> store, string label, bool capped)
    {
        if (store.TryGetValue(label, out var count))
        {
            store[label] = count + 1;
            return;
        }

        if (capped && store.Count >= MaxLabels)
        {
            _overflow++;
            return;
        }

        store[label] = 1;
    }

    private static bool IsClosedServer(string label) =>
        label is "session" or "capability" || KindLabels.Contains(label);

    private static string ServerLabel(AgentTaskService.Caller? caller)
    {
        if (caller?.Task is { } task) return "kind:" + task.AgentKind.ToString();
        if (caller?.CapabilityId is not null)
        {
            if (TryCapabilityName(caller.CapabilityName, out var name)) return "capability:" + name;
            return "capability";
        }

        if (caller?.SessionId is not null) return "session";
        return UnknownLabel;
    }

    /// <summary>Registered header only. A miss returns false and does not echo <paramref name="raw"/>.</summary>
    private static bool TryRegistered(string? raw, out string label)
    {
        label = "";
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 256) return false;
        var text = raw.Trim().ToLowerInvariant();
        if (!RegisteredLabels.Contains(text)) return false;
        label = text;
        return true;
    }

    /// <summary>
    /// Server-derived capability name: an ASCII letter, then up to 63 letters, digits, <c>.</c>,
    /// <c>_</c>, or <c>-</c>. A guid, a 32-character-or-longer hex string, and the word <c>unknown</c>
    /// stay the bare <c>capability</c> label.
    /// </summary>
    private static bool TryCapabilityName(string? raw, out string label)
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
