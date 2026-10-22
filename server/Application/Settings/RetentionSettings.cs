namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Per-table data-retention windows (CARD-0044). A value <c>&lt;= 0</c> disables that table's
/// pass — an explicit off-switch, unlike the audit knob that used to be read by nothing.
/// Session/task/audit windows are bound here so later slices share one settings object; slices
/// 1-3 act on transcripts, queued messages, sessions, and task trees.
/// </summary>
public sealed class RetentionSettings
{
    /// <summary>Hours after a complete recipient UserPrompt before clearing the serialized inbound envelope. Nonpositive disables the pass.</summary>
    public int ChannelInboundPayloadGraceHours { get; set; } = 24;

    public int TranscriptRetentionDays { get; set; } = 7;

    public int QueuedMessageRetentionDays { get; set; } = 30;

    public int SessionRetentionDays { get; set; } = 90;

    public int TaskRetentionDays { get; set; } = 180;

    /// <summary>
    /// Append-only subscription-usage samples (CARD-0143). Independent of session liveness —
    /// the quota fact outlives the session that collected it.
    /// </summary>
    public int SubscriptionUsageRetentionDays { get; set; } = 30;

    /// <summary>
    /// CARD-1446 D-5. <c>WorktreeResidueRunCandidates</c> rows whose Outcome is <c>Held</c> or <c>Preview</c> and
    /// whose <c>EvaluatedAt</c> is older than this many days are deleted; run headers and action rows
    /// (<c>Queued</c>, <c>Refused</c>, <c>Partial</c>, <c>Removed</c>) are kept so every queued cleanup keeps its
    /// provenance. Nonpositive disables the pass.
    /// </summary>
    public int WorktreeResidueHeldRowRetentionDays { get; set; } = 30;

    /// <summary>How often <c>DataRetentionHostedService</c> runs. <c>&lt;= 0</c> disables the hosted sweep.</summary>
    public int SweepHours { get; set; } = 6;
}
