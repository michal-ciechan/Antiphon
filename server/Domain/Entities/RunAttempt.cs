using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class RunAttempt
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public Guid? AgentSessionId { get; set; }
    public Guid? WorktreeId { get; set; }
    public Guid? BoardWorkflowDefinitionId { get; set; }
    public int AttemptNumber { get; set; }
    public RunPhase Phase { get; set; } = RunPhase.PreparingWorkspace;
    public DateTime CreatedAt { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastEventAt { get; set; }
    public DateTime PhaseStartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string PhaseDurationsJson { get; set; } = "{}";
    public string Prompt { get; set; } = string.Empty;
    public string? ErrorDetails { get; set; }
    public int? ExitCode { get; set; }

    public Card Card { get; set; } = null!;
    public AgentSession? AgentSession { get; set; }
    public Worktree? Worktree { get; set; }
    public BoardWorkflowDefinition? BoardWorkflowDefinition { get; set; }
    public TokenUsage? TokenUsage { get; set; }
}
