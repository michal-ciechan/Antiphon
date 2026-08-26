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

    /// <summary>
    /// Which lane hosts this agent's interactive child (CARD-0160 / herdr S2). Default
    /// <see cref="SessionBackend.PtyHost"/> — herdr is opt-in and dark unless explicitly selected.
    /// Only <see cref="AgentKind.ClaudeCode"/> is spiked (CARD-0187); AlwaysOn and channel-bound
    /// refusals were lifted (CARD-0186).
    /// </summary>
    public SessionBackend SessionBackend { get; set; } = SessionBackend.PtyHost;

    /// <summary>
    /// Per-agent override of <c>ContextCompactionSettings.Enabled</c> (CARD-0082).
    /// Null = use the installation default. The first override an operator wants is "off for this one".
    /// </summary>
    public bool? AutoCompactEnabled { get; set; }

    /// <summary>
    /// Per-agent override of <c>ContextCompactionSettings.IdleMinutes</c>. Null = use the installation default.
    /// </summary>
    public int? AutoCompactIdleMinutes { get; set; }

    /// <summary>
    /// Per-agent override of <c>ContextCompactionSettings.ContextPercent</c>. Null = use the installation default.
    /// </summary>
    public int? AutoCompactContextPercent { get; set; }

    /// <summary>
    /// Per-agent launch environment (CARD-0106 S2), stored as a JSON <c>Dictionary&lt;string,string&gt;</c>;
    /// <c>{}</c> for every agent that existed before the column. This is where "per-agent API key"
    /// actually lives — a value may reference a stored key as <c>{{key:NAME}}</c>, resolved at launch
    /// with the agent's project (via <see cref="BoardId"/> → <c>Board.ProjectId</c>) overriding global.
    ///
    /// <para>Merged into the launch environment BEFORE <c>AgentLaunchOptions.ExtraEnv</c>, and that
    /// ordering is load-bearing: <c>ExtraEnv</c> is Antiphon's own orchestration block
    /// (<c>ANTIPHON_SESSION_ID</c>, <c>ANTIPHON_TASK_TOKEN</c>, ...), and a per-agent override of
    /// those would be a self-inflicted CARD-0006 — an agent tailing somebody else's transcript.</para>
    /// </summary>
    public string LaunchEnvJson { get; set; } = "{}";

    public string? PersistentSessionId { get; set; }
    public Guid? CurrentCardId { get; set; }
    /// <summary>The standing agent's default board; always null for a pool delegate (<see cref="IsPoolDelegate"/>).</summary>
    public Guid? BoardId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// WHICH AGENT PROGRAM this row is (CARD-0084 S3, CARD-0138). Invariant: if
    /// <see cref="TuiProfileId"/> is set, this equals that profile's
    /// <see cref="AgentTuiProfile.Kind"/>. If it is null, this is the row's own truth and
    /// nothing derives it — a pool delegate is born with no profile, and this column is then
    /// the only fact standing between a Grok task and a warm Claude process.
    ///
    /// <para>Stored on the row rather than derived from the latest session for two reasons the
    /// pool depends on: derivation is racy against a session that is still starting, and a pool
    /// row can legitimately have no session at all for a moment. A warm delegate is claimable
    /// only by a task of the SAME kind — a Claude process cannot run a Grok task's brief, and
    /// reusing one for it would look like a successful dispatch right up until the report never
    /// came.</para>
    ///
    /// <para>Defaults to <see cref="AgentKind.ClaudeCode"/>, which every agent row that existed
    /// before this column really was. <see cref="IsPoolDelegate"/> is what makes a row pool
    /// furniture, not this.</para>
    ///
    /// <para>In-place correction (CARD-0139 D6): re-PATCHing the agent's existing
    /// <see cref="TuiProfileId"/> re-runs <c>ApplyTuiSelectionAsync</c> and re-syncs this from
    /// the profile. There is no dedicated resync endpoint — a third writer of this column is
    /// the wrong direction.</para>
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
    /// The project scope under which this pool delegate's LIVE environment was resolved. Null
    /// means global-only. It is stamped when a pool session launches (and restamped only when that
    /// row is relaunched), then fences warm reuse: a live process cannot safely change environment
    /// scope between tasks. Meaningless for agents that are not pool delegates (CARD-0115 S3).
    /// </summary>
    public Guid? PoolProjectId { get; set; }

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
