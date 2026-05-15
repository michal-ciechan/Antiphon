using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class Worktree
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public string RepoPath { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string BaseRef { get; set; } = string.Empty;
    public WorktreeStatus Status { get; set; } = WorktreeStatus.Active;
    public DateTime CreatedAt { get; set; }
    public DateTime LastTouchedAt { get; set; }
    public DateTime? RemovedAt { get; set; }

    public Card Card { get; set; } = null!;
    public ICollection<AgentSession> AgentSessions { get; set; } = new List<AgentSession>();
    public ICollection<RunAttempt> RunAttempts { get; set; } = new List<RunAttempt>();
}
