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
    public Guid? TuiProfileId { get; set; }
    public string? ModelId { get; set; }
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

    /// <summary>
    /// How this agent writes (CARD-0060). Composed into <c>--append-system-prompt</c> AFTER any
    /// bundles and BEFORE <see cref="SystemPromptAppend"/>, so the agent's own hand-written contract
    /// keeps the final word over a style picked from a dropdown.
    ///
    /// <para><see cref="AgentReplyStyle.Normal"/> — the default and the migration backfill — composes
    /// to nothing at all, so this column changes no existing agent's launch arguments.</para>
    /// </summary>
    public AgentReplyStyle ReplyStyle { get; set; } = AgentReplyStyle.Normal;

    public string? PersistentSessionId { get; set; }
    public Guid? CurrentCardId { get; set; }
    /// <summary>The board automatically created for this agent when it was added.</summary>
    public Guid? BoardId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// WHICH AGENT PROGRAM this delegate's session runs (CARD-0084 S3). Stored on the row rather
    /// than derived from the latest session for two reasons the pool depends on: derivation is racy
    /// against a session that is still starting, and a pool row can legitimately have no session at
    /// all for a moment. A warm delegate is claimable only by a task of the SAME kind — a Claude
    /// process cannot run a Grok task's brief, and reusing one for it would look like a successful
    /// dispatch right up until the report never came.
    ///
    /// <para>Defaults to <see cref="AgentKind.ClaudeCode"/>, which every agent row that existed
    /// before this column really was; only the dispatcher writes it today, so a user-created agent
    /// keeps the default and the pool keeps ignoring it (<see cref="IsPoolDelegate"/> is what makes
    /// a row pool furniture, not this).</para>
    /// </summary>
    public AgentKind Kind { get; set; } = AgentKind.ClaudeCode;

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
    public AgentTuiProfile? TuiProfile { get; set; }
    public Card? CurrentCard { get; set; }
    public Board? Board { get; set; }
    public ICollection<Card> QueueCards { get; set; } = new List<Card>();
    public ICollection<CardWorkflowRun> WorkflowRuns { get; set; } = new List<CardWorkflowRun>();

    /// <summary>
    /// Optional instruction bundles attached to THIS agent (CARD-0058 slice 6), on top of whatever
    /// its role implies. Deliberately never read through this navigation on a launch path: an agent
    /// loaded without the include would compose silently without its bundles, so the launch paths
    /// query <c>AgentBundleAttachments.LoadAsync</c> explicitly instead.
    /// </summary>
    public ICollection<AgentBundleAttachment> BundleAttachments { get; set; } = new List<AgentBundleAttachment>();
}
