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
    /// uses 4). Clamped to at least 1, because the verifier builds with no cap for a count of 0.
    /// </summary>
    public int BuildSlotMemoryFloorFallbackMaxCpuCount { get; set; } = 2;
}
