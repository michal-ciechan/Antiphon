using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Configuration for delegated agent tasks. The role→tier ladder lives here rather than in code so
/// the cost profile can be retuned without a deploy.
/// </summary>
public sealed class DelegationSettings
{
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>How many tasks may be Dispatched/Working at once across all roots.</summary>
    public int MaxConcurrentTasks { get; set; } = 6;

    /// <summary>
    /// Backstop only. Nesting is INTENDED (orchestrator → sub-orchestrator → worker is depth 2 and
    /// ordinary), so depth is a poor runaway guard — <see cref="MaxCostUsdPerRoot"/> is the real one.
    /// </summary>
    public int MaxDepth { get; set; } = 5;

    public int MaxTasksPerRoot { get; set; } = 40;

    /// <summary>
    /// The real ceiling on a recursive tree: it can only run away by spending. Crossing it stops
    /// further dispatch for that root; work already in flight is left alone and still reports.
    /// </summary>
    public decimal MaxCostUsdPerRoot { get; set; } = 5.00m;

    /// <summary>
    /// A report at or under this size is forwarded whole — the report IS the deliverable. Above it
    /// the delegate is told to spill to a file (its own judgement about what matters), and the
    /// server backstops with a head+tail excerpt if it didn't.
    /// </summary>
    public int ReplyInlineMaxChars { get; set; } = 20_000;

    public int ReplyExcerptHeadChars { get; set; } = 6_000;
    public int ReplyExcerptTailChars { get; set; } = 6_000;

    /// <summary>
    /// Directory prefixes a task may run in. A SECURITY BOUNDARY: without it an agent that can
    /// delegate could point a task at any path the server user can read. Empty = only the parent's
    /// own working directory tree is allowed.
    /// </summary>
    public List<string> AllowedRoots { get; set; } = [];

    public int DefaultCols { get; set; } = 120;
    public int DefaultRows { get; set; } = 30;

    /// <summary>What ANTIPHON_API is set to in a delegate's environment — where it calls back.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:17202";

    /// <summary>Role → tier and per-role timeouts. Missing roles fall back to <see cref="DefaultLevel"/>.</summary>
    public Dictionary<string, RolePolicyEntry> RolePolicy { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Plan"] = new() { Level = AgentModelLevel.Frontier },
        ["Code"] = new() { Level = AgentModelLevel.Frontier },
        ["Review"] = new() { Level = AgentModelLevel.Frontier },
        ["Debug"] = new() { Level = AgentModelLevel.High, EscalateTo = AgentModelLevel.Frontier, EscalateAfterMinutes = 25 },
        ["Coverage"] = new() { Level = AgentModelLevel.High },
        ["Merge"] = new() { Level = AgentModelLevel.High },
        ["Docs"] = new() { Level = AgentModelLevel.Medium },
        ["Commit"] = new() { Level = AgentModelLevel.Medium },
        // Low tier is safe for Test/Deploy because these RUN things and report what happened —
        // INTERPRETING a failure is a separate Debug task at High.
        ["Test"] = new() { Level = AgentModelLevel.Low, EscalateTo = AgentModelLevel.Medium },
        ["Deploy"] = new() { Level = AgentModelLevel.Low },
    };

    public AgentModelLevel DefaultLevel { get; set; } = AgentModelLevel.High;

    /// <summary>A sub-orchestrator decomposes, which is expensive thinking — never below this.</summary>
    public AgentModelLevel MinOrchestratorLevel { get; set; } = AgentModelLevel.High;

    public sealed class RolePolicyEntry
    {
        public AgentModelLevel Level { get; set; } = AgentModelLevel.High;
        public AgentModelLevel? EscalateTo { get; set; }
        public int? EscalateAfterMinutes { get; set; }
        public int TimeoutMinutes { get; set; } = 60;
    }
}
