using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class Card
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public Guid BoardColumnId { get; set; }
    public Guid? OwnerSessionId { get; set; }
    public Guid? CurrentWorktreeId { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string LabelsJson { get; set; } = "[]";
    public CardStatus Status { get; set; } = CardStatus.Backlog;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? TerminalReason { get; set; }

    public Board Board { get; set; } = null!;
    public BoardColumn BoardColumn { get; set; } = null!;
    public AgentSession? OwnerSession { get; set; }
    public Worktree? CurrentWorktree { get; set; }
    public ExternalIssueRef? ExternalIssueRef { get; set; }
    public RetrySchedule? RetrySchedule { get; set; }
    public ICollection<AgentSession> AgentSessions { get; set; } = new List<AgentSession>();
    public ICollection<RunAttempt> RunAttempts { get; set; } = new List<RunAttempt>();
    public ICollection<Worktree> Worktrees { get; set; } = new List<Worktree>();
}
