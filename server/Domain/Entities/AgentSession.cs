using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class AgentSession
{
    public Guid Id { get; set; }

    // Nullable: a session can be cardless (a long-running, human-driven interactive terminal).
    // When the session is working a card, this points at it; otherwise null.
    public Guid? CardId { get; set; }
    public Guid? WorktreeId { get; set; }
    public string DefinitionName { get; set; } = string.Empty;
    public AgentKind AgentKind { get; set; }
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

    public Card Card { get; set; } = null!;
    public Worktree? Worktree { get; set; }
    public ICollection<RunAttempt> RunAttempts { get; set; } = new List<RunAttempt>();
}
