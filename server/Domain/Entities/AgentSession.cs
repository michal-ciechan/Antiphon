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

    public Card Card { get; set; } = null!;
    public Worktree? Worktree { get; set; }
    public ICollection<RunAttempt> RunAttempts { get; set; } = new List<RunAttempt>();
}
