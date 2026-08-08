using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class Agent
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public Guid? DefaultWorkflowTemplateId { get; set; }
    public AgentAssignmentPolicy AssignmentPolicy { get; set; } = AgentAssignmentPolicy.AutoPick;
    public AgentStatus Status { get; set; } = AgentStatus.Idle;

    /// <summary>
    /// Generic capability level for the agent's sessions — mapped per agent kind to a provider
    /// family alias at launch (Claude: Frontier→fable, High→opus, Medium→sonnet, Low→haiku).
    /// Defaults to High (the Opus tier).
    /// </summary>
    public AgentModelLevel ModelLevel { get; set; } = AgentModelLevel.High;

    /// <summary>Supervised: auto-started at boot and auto-restarted on crash (never-give-up backoff ladder).</summary>
    public bool AlwaysOn { get; set; }

    /// <summary>
    /// Remote control is part of this agent's normal setup: every start path (manual, channel
    /// bridge, supervised) arms /remote-control when true and the request doesn't override.
    /// </summary>
    public bool RemoteControlEnabled { get; set; }

    /// <summary>
    /// Channel preamble template rendered into <c>--append-system-prompt</c> on every interactive
    /// ClaudeCode launch (fresh AND resume — args are per-invocation, so the contract survives
    /// compaction and re-arms on resume). Null = launch args unchanged; also gates the bootstrap /
    /// restart / compaction-recovery notes (agents without a preamble get none of them).
    /// </summary>
    public string? SystemPromptAppend { get; set; }

    public string? PersistentSessionId { get; set; }
    public Guid? CurrentCardId { get; set; }
    /// <summary>The board automatically created for this agent when it was added.</summary>
    public Guid? BoardId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// A delegate spawned by the task dispatcher, eligible for the warm pool: after its task
    /// settles it stays alive for follow-up work in its directory instead of dying, until the
    /// pool janitor retires it. Never true for user-created agents — the pool must not adopt them.
    /// </summary>
    public bool IsPoolDelegate { get; set; }

    /// <summary>Set while the delegate sits warm in the pool; null while it is working (or not pooled).</summary>
    public DateTime? PoolIdleSince { get; set; }

    /// <summary>
    /// For a reservation window after each task, the warm delegate answers only to the run that
    /// just used it — follow-ups keep their context. The window expiring releases it to anyone;
    /// that is a pure time comparison, so release needs no state change.
    /// </summary>
    public Guid? PoolReservedForRootTaskId { get; set; }

    public WorkflowTemplate? DefaultWorkflowTemplate { get; set; }
    public Card? CurrentCard { get; set; }
    public Board? Board { get; set; }
    public ICollection<Card> QueueCards { get; set; } = new List<Card>();
    public ICollection<CardWorkflowRun> WorkflowRuns { get; set; } = new List<CardWorkflowRun>();
}
