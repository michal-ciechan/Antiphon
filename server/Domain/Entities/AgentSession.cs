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
    /// (CARD-0160). Reconciliation and relaunch must know how THIS session was launched even if the
    /// agent setting changes later — same rationale as <see cref="AgentKind"/>.
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

    public Card Card { get; set; } = null!;
    public Worktree? Worktree { get; set; }
    public AgentTuiProfileRevision? TuiProfileRevision { get; set; }
    public ICollection<RunAttempt> RunAttempts { get; set; } = new List<RunAttempt>();
}
