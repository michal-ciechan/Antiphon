namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Idle auto-compaction for Claude sessions (CARD-0082). Global defaults; three per-agent
/// columns on <c>Agent</c> override Enabled / IdleMinutes / ContextPercent when non-null.
/// The context-window ceiling (<c>DefaultContextTokens</c> / <c>ModelOverrides</c>) is NOT
/// duplicated here — it lives on <see cref="ContextWindowSettings"/> (S1), which
/// <see cref="Services.SessionContextUsage"/> already reads for fullness computation. S3's
/// sweep should read the ceiling from there too, not reinvent it on this class.
/// </summary>
public sealed class ContextCompactionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Minutes since the newest transcript row before an idle session is eligible. 480 = 8 h.</summary>
    public int IdleMinutes { get; set; } = 480;

    /// <summary>Compact when computed context fullness is at least this percent (1–100).</summary>
    public int ContextPercent { get; set; } = 50;

    /// <summary>Do not fire again when a /compact or CompactBoundary exists inside this window.</summary>
    public int CooldownHours { get; set; } = 24;

    /// <summary>How long after a confirmed /compact the (manual) CompactBoundary may take to appear.</summary>
    public int BoundaryTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// Per-agent nulls fall through to these globals; a non-null value wins. The three
    /// overridable knobs only — cooldown / timeout / ceiling stay installation-wide.
    /// </summary>
    public ResolvedContextCompaction Resolve(bool? enabled, int? idleMinutes, int? contextPercent) =>
        new(
            enabled ?? Enabled,
            idleMinutes ?? IdleMinutes,
            contextPercent ?? ContextPercent);
}

/// <summary>The three knobs an agent may override, after falling through to the global defaults.</summary>
public sealed record ResolvedContextCompaction(
    bool Enabled,
    int IdleMinutes,
    int ContextPercent);
