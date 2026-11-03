namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0589 S4: <c>Landing</c> — how the land verifier waits for a host build slot.</summary>
public sealed class LandingSettings
{
    /// <summary>Disable pre-publication build skips. AlreadyPresent still performs no new publication.</summary>
    public bool ForceVerificationBuild { get; set; }

    /// <summary>Retain owned outputs across lands with the same repository/toolchain.</summary>
    public bool ReuseVerificationArtifacts { get; set; } = true;

    /// <summary>
    /// The longest the verifier waits for a slot before refusing to build (step <c>build-slot</c>),
    /// inside the land's own time budget. The delegate wrappers wait 45.
    /// </summary>
    public int BuildSlotWaitMinutes { get; set; } = 30;

    /// <summary>
    /// How long an unanswering runner (or one without <c>/build-slots</c>) is retried before the
    /// verifier builds unleased at <c>-maxcpucount:4</c>. Never a land failure.
    /// </summary>
    public int BuildSlotUnreachableGraceSeconds { get; set; } = 60;

    /// <summary>
    /// CARD-1461 option C. Once the runner has refused the verifier for this many minutes with
    /// <c>build_slot_memory_floor</c> (measured from the last answer that was not that refusal: a
    /// busy answer or a missed answer restart the window), the verifier builds unleased at
    /// <see cref="BuildSlotMemoryFloorFallbackMaxCpuCount"/> instead of waiting for the deadline.
    /// Default 0: off, today's behaviour (the floor counts toward <see cref="BuildSlotWaitMinutes"/>
    /// and a timeout fails the land). A window at or above the wait never fires.
    /// </summary>
    public int BuildSlotMemoryFloorFallbackMinutes { get; set; }

    /// <summary>
    /// The <c>-maxcpucount</c> of a memory-floor fallback build (default 2; the unreachable path
    /// uses 4). Clamped to at least 1, because the verifier builds with no cap for a count of 0, and
    /// (CARD-1470) to at most the host's logical cpu count (<see cref="Environment.ProcessorCount"/>).
    /// The global bound: each land verification takes at most one fallback build at this count, so
    /// concurrent fallbacks (lands in other repositories) use at most this count each.
    /// </summary>
    public int BuildSlotMemoryFloorFallbackMaxCpuCount { get; set; } = 2;

    /// <summary>
    /// CARD-0824 S2. Delete the task's origin branch after a confirmed publication. Off by default; enabled per
    /// runner volume only after its legacy mirrors are drained (docs/orchestration-loop.md). Re-read before every
    /// deletion, so switching it off stops an in-flight run (the kill switch).
    /// </summary>
    public bool DeleteSourceBranchAfterPublication { get; set; }

    /// <summary>CARD-0824 S2 (H-1). Grace after <c>RemoteConfirmedAt</c> before a branch is eligible. Default 24 h.</summary>
    public int SourceBranchDeleteDelayMinutes { get; set; } = 1440;

    /// <summary>
    /// CARD-0824 S2 (Q-3). Backlog cutoff: only publications confirmed strictly after this UTC instant are eligible.
    /// Null means none is, so a first enable cannot sweep the historical backlog.
    /// </summary>
    public DateTimeOffset? SourceBranchDeletionAfterUtc { get; set; }

    /// <summary>CARD-0824 S2. Origin deletions (push attempts) started per run. Default 25.</summary>
    public int MaxSourceBranchDeletionsPerRun { get; set; } = 25;

    /// <summary>
    /// CARD-0824 S2 (Q-5). Runner volumes whose legacy mirrors are drained. A branch any of whose tasks ran on an
    /// unlisted runner is deferred (<c>runner_volume_not_enabled</c>). Desktop-only tasks need only the flag.
    /// </summary>
    public List<string> SourceBranchDeletionRunnerIds { get; set; } = [];

    /// <summary>CARD-0824 S2. Classify and recheck only: no runner call, no push, no row write.</summary>
    public bool SourceBranchDeletionDryRun { get; set; }

    /// <summary>CARD-0824 S2 (Q-6). Recurring job id and cron of the deletion sweep.</summary>
    public string SourceBranchDeletionJobId { get; set; } = "antiphon:published-branch-deletion";
    public string SourceBranchDeletionCron { get; set; } = "*/15 * * * *";
}
