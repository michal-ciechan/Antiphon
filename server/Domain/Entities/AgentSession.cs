using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class AgentSession
{
    public Guid Id { get; set; }

    // Nullable: a session can be cardless (a long-running, human-driven interactive terminal).
    // When the session is working a card, this points at it; otherwise null.
    public Guid? CardId { get; set; }
    public Guid? WorktreeId { get; set; }
    public Guid? TuiProfileRevisionId { get; set; }
    public string? EffectiveModelId { get; set; }
    public string DefinitionName { get; set; } = string.Empty;
    public AgentKind AgentKind { get; set; }

    /// <summary>
    /// Snapshot of the owning agent's <see cref="Agent.SessionBackend"/> at session-row creation
    /// (CARD-0160). A resume restamps it from the agent's current setting (CARD-0186) so a PATCH
    /// takes effect on the next crash-restart. The live row still governs ceilings for the session
    /// that is actually running.
    /// </summary>
    public SessionBackend SessionBackend { get; set; } = SessionBackend.PtyHost;

    public SessionStatus Status { get; set; } = SessionStatus.Created;
    public string Cwd { get; set; } = string.Empty;
    public int Cols { get; set; } = 120;
    public int Rows { get; set; } = 30;
    public DateTime CreatedAt { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? ExitCode { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>
    /// Who asked for this session to end, or whether the process exited on its own (CARD-0256).
    /// Default <see cref="SessionTerminationSource.Unknown"/> covers every pre-existing row.
    /// A later process-exit event must not overwrite <see cref="SessionTerminationSource.OperatorRequest"/>
    /// or <see cref="SessionTerminationSource.SystemRequest"/>.
    /// </summary>
    public SessionTerminationSource TerminationSource { get; set; }

    /// <summary>
    /// Why ready failed, when the adapter named it (CARD-0324). Null on every pre-existing
    /// row and on every launch that became ready. The dead-session sweep maps
    /// <see cref="SessionLaunchBlock.ProviderSignInRequired"/> to
    /// <see cref="AgentTaskFailureCode.AuthenticationRequired"/>.
    /// </summary>
    public SessionLaunchBlock? LaunchBlock { get; set; }

    /// <summary>
    /// When this process last resumed an interrupted launch on this row (CARD-0340). Null on
    /// every pre-existing row and on every launch that became ready without a restart. The
    /// delivery watchdog measures its window from <c>max(DispatchedAt, LaunchResumedAt)</c>
    /// so a resume does not hide the surviving brief by restamping <c>DispatchedAt</c>.
    /// </summary>
    public DateTime? LaunchResumedAt { get; set; }

    /// <summary>
    /// Highest transcript sequence for which compaction recovery has already run. The durable
    /// dedupe anchor: TranscriptTailer restarts at offset 0 on every runner restart/adoption and
    /// republishes ALL historical events — an incident-row check would be defeated by incident
    /// pruning, but this per-session high-water mark survives both.
    /// </summary>
    public long? CompactionRecoveryWatermark { get; set; }

    /// <summary>
    /// SHA-256 of the session-scoped delegation token injected into the session's environment at
    /// launch (as ANTIPHON_TASK_TOKEN, the name delegate.ps1 already sends). Lets a standing agent
    /// session — an always-on orchestrator — authenticate to POST /api/agent-tasks as ITSELF, so
    /// its delegates inherit ITS working directory and report back into ITS session. Without this,
    /// a shell caller was treated as the manual UI path: it inherited the server process's cwd and
    /// its reports landed on the board unseen (live miss 2026-08-09). Re-minted on every launch;
    /// never stores the raw token.
    /// </summary>
    public string? DelegationTokenHash { get; set; }

    /// <summary>
    /// The instruction bundles this session was LAUNCHED carrying, as one stamp line —
    /// <c>"board-api v1a2b3c4d, style-terse v9f8e7d6c"</c> (CARD-0058 slice 6). The ONLY composed
    /// state that is stored anywhere, and it deliberately stores the STAMPS rather than the text:
    /// the drift check is then a string match against a composition recomputed from the repo, with
    /// no second versioning scheme to keep in step with the content hashes.
    ///
    /// <para>Empty string means "this launch composed nothing", which is a real and different fact
    /// from NULL — null means no launch path recorded a composition here at all (a session that
    /// predates this column, or a card spawn, which composes no bundles). Only a non-null stamp is
    /// evidence, so a null can never raise a drift badge.</para>
    ///
    /// <para>It is never used to REBUILD a prompt. A running session keeps the bundles it started
    /// with until its next launch — bounded and deliberate — and this column exists to make that
    /// visible, not to trigger a fix.</para>
    /// </summary>
    public string? ComposedBundleStamp { get; set; }

    /// <summary>
    /// The standing instruction files this session was LAUNCHED against, as one stamp line —
    /// <c>"AGENTS.md v1a2b3c4d, docs/orchestration-loop.md v9e8d7c6b"</c> (CARD-0334 S1). Same
    /// shape and hash rule as <see cref="ComposedBundleStamp"/>. Null is no evidence (Herdr
    /// attach, card/delegate launches, pre-column rows) and never drift; empty string means
    /// the launch path ran and none of the listed files existed under cwd.
    /// </summary>
    public string? InstructionFileStamp { get; set; }

    /// <summary>
    /// Last Notify-lane drift the session was told about (CARD-0334 S3). Dedupe key: current
    /// composed stamp line + file stamp line, ≤ 4000. Null until S3 writes it.
    /// </summary>
    public string? PolicyNotifiedStamp { get; set; }

    public Card Card { get; set; } = null!;
    public Worktree? Worktree { get; set; }
    public AgentTuiProfileRevision? TuiProfileRevision { get; set; }
    public ICollection<RunAttempt> RunAttempts { get; set; } = new List<RunAttempt>();
}
