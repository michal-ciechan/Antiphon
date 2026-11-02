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

    /// <summary>
    /// CARD-1654. Candidate task-tree roots examined per prune batch, in RootTaskId order. Each
    /// batch is its own eligibility query and delete transaction. Nonpositive uses 100.
    /// </summary>
    public int TaskPruneBatchSize { get; set; } = 100;

    /// <summary>
    /// CARD-1654. Command timeout for each task-prune batch statement. Sized for
    /// <see cref="TaskPruneBatchSize"/> roots, not the whole table. Nonpositive uses 60.
    /// </summary>
    public int TaskPruneBatchCommandTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// CARD-1654. Batches one sweep may run before it stops. The scan cursor carries to the next
    /// sweep, so a backlog is pruned across sweeps. Nonpositive uses 50.
    /// </summary>
    public int TaskPruneMaxBatchesPerSweep { get; set; } = 50;

    /// <summary>
    /// CARD-1662. Failed task-prune batch attempts one sweep may spend. When a root range ends with
    /// the sweep at or past this many failed attempts, the Tasks pass stops for this sweep, reports
    /// Failed and the later passes run; the scan resumes after that range next sweep. Nonpositive uses 6.
    /// </summary>
    public int TaskPruneMaxFailedAttemptsPerSweep { get; set; } = 6;

    /// <summary>
    /// CARD-1658. Rows per batch for the row passes after Tasks (subscription-usage samples,
    /// worktree-residue Held/Preview rows, the audit FullContent archive). Each batch is one bounded
    /// statement. Nonpositive uses 1000.
    /// </summary>
    public int RowPruneBatchSize { get; set; } = 1000;

    /// <summary>
    /// CARD-1658. Command timeout for each row-pass batch statement, sized for
    /// <see cref="RowPruneBatchSize"/> rows. Nonpositive uses 60.
    /// </summary>
    public int RowPruneBatchCommandTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// CARD-1658. Batches each row pass may run per sweep; the rest waits for the next sweep.
    /// Nonpositive uses 50.
    /// </summary>
    public int RowPruneMaxBatchesPerSweep { get; set; } = 50;
}
