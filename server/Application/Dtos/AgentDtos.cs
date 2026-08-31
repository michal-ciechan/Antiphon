using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record AgentRegistryDto(
    string DefaultDefinition,
    IReadOnlyList<AgentDefinitionDto> Definitions);

public sealed record AgentDefinitionDto(
    string Name,
    AgentKind Kind,
    bool IsDefault);

public sealed record AgentSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    string WorkingDirectory,
    /// <summary>Standing job (CLAUDE.md). Not a start prompt — see <see cref="CreateAgentRequest.Details"/>.</summary>
    string Details,
    Guid? DefaultWorkflowTemplateId,
    string? DefaultWorkflowTemplateName,
    AgentAssignmentPolicy AssignmentPolicy,
    AgentStatus Status,
    string? PersistentSessionId,
    Guid? CurrentCardId,
    Guid? BoardId,
    string? BoardName,
    int QueueLength,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // The agent's persistent session when it is currently live (Starting/Running/Stopping),
    // otherwise null. Lets the UI open the running terminal without a separate lookup.
    AgentSessionSummaryDto? LiveSession = null,
    bool AlwaysOn = false,
    bool RemoteControlEnabled = false,
    // Present only for AlwaysOn agents with supervision history (countdowns, suspend badge).
    AgentSupervisionDto? Supervision = null,
    string? SystemPromptAppend = null,
    // Generic model capability level (mapped per agent kind to a family alias at launch). Default High.
    AgentModelLevel ModelLevel = AgentModelLevel.High,
    // Transcript-derived "mid-turn right now" (SessionMessageQueueService.IsWorkingAsync) for the
    // live session. Distinct from Status=Running, which only means the agent was started.
    bool Working = false,
    Guid? TuiProfileId = null,
    string? ModelId = null,
    AgentTuiConfiguredSelectionDto? ConfiguredSelection = null,
    AgentTuiLiveSessionSelectionDto? LiveSessionSelection = null,
    // How the agent writes (CARD-0060). Normal composes to nothing; the list renders a chip for
    // anything else.
    AgentReplyStyle ReplyStyle = AgentReplyStyle.Normal,
    // Which lane hosts the interactive child (CARD-0160). PtyHost is the default; Herdr is opt-in.
    SessionBackend SessionBackend = SessionBackend.PtyHost,
    // The live session was launched with instruction bundles the repo has since moved on from —
    // an edited bundle file, an attachment added or removed, a changed reply style (CARD-0058).
    // Informational ONLY: it restarts with the new ones at its next launch and nothing here forces
    // that. False whenever there is no live session, or no recorded stamp to compare.
    bool BundlesOutOfDate = false,
    // CARD-0082 S2. Null = this agent uses the global ContextCompactionSettings value.
    bool? AutoCompactEnabled = null,
    int? AutoCompactIdleMinutes = null,
    int? AutoCompactContextPercent = null,
    // CARD-0106 S2. The agent's own launch environment. Values may reference a stored API key as
    // {{key:NAME}}; project keys (via the agent's board) override global ones at launch. Never
    // resolved here — this is what the operator typed, and a resolved value must never reach a DTO.
    IReadOnlyDictionary<string, string>? LaunchEnv = null,
    // CARD-0139. WHICH AGENT PROGRAM this row is. Read-only on the wire: with a TuiProfileId
    // attached this equals that profile's Kind (CARD-0138 D1); without one it is the row's own
    // truth. Default-valued so existing construction sites need no change.
    AgentKind Kind = AgentKind.ClaudeCode);

public sealed record AgentDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string WorkingDirectory,
    /// <summary>Standing job (CLAUDE.md). Not a start prompt — see <see cref="CreateAgentRequest.Details"/>.</summary>
    string Details,
    Guid? DefaultWorkflowTemplateId,
    string? DefaultWorkflowTemplateName,
    AgentAssignmentPolicy AssignmentPolicy,
    AgentStatus Status,
    string? PersistentSessionId,
    Guid? CurrentCardId,
    Guid? BoardId,
    string? BoardName,
    IReadOnlyList<AgentQueueCardDto> Queue,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // See AgentSummaryDto.LiveSession.
    AgentSessionSummaryDto? LiveSession = null,
    bool AlwaysOn = false,
    bool RemoteControlEnabled = false,
    AgentSupervisionDto? Supervision = null,
    string? SystemPromptAppend = null,
    AgentModelLevel ModelLevel = AgentModelLevel.High,
    // See AgentSummaryDto.Working.
    bool Working = false,
    Guid? TuiProfileId = null,
    string? ModelId = null,
    AgentTuiConfiguredSelectionDto? ConfiguredSelection = null,
    AgentTuiLiveSessionSelectionDto? LiveSessionSelection = null,
    AgentReplyStyle ReplyStyle = AgentReplyStyle.Normal,
    // Which lane hosts the interactive child (CARD-0160). PtyHost is the default; Herdr is opt-in.
    SessionBackend SessionBackend = SessionBackend.PtyHost,
    // The bundle stamps this agent's NEXT launch will carry — "style-caveman v1a2b3c4d". Read-only
    // and recomputed per request: there is no stored composition anywhere, which is the point.
    IReadOnlyList<string>? ComposedBundles = null,
    // See AgentSummaryDto.BundlesOutOfDate.
    bool BundlesOutOfDate = false,
    // The bundle KEYS attached to this agent, in composition order — what the settings modal's
    // multi-select round-trips. Distinct from ComposedBundles, which is the whole composition
    // (attachments AND the reply-style block) stamped with versions.
    IReadOnlyList<string>? AttachedBundleKeys = null,
    // CARD-0082 S2. Null = this agent uses the global ContextCompactionSettings value.
    bool? AutoCompactEnabled = null,
    int? AutoCompactIdleMinutes = null,
    int? AutoCompactContextPercent = null,
    // CARD-0106 S2. The agent's own launch environment. Values may reference a stored API key as
    // {{key:NAME}}; project keys (via the agent's board) override global ones at launch. Never
    // resolved here — this is what the operator typed, and a resolved value must never reach a DTO.
    IReadOnlyDictionary<string, string>? LaunchEnv = null,
    // CARD-0139. See AgentSummaryDto.Kind.
    AgentKind Kind = AgentKind.ClaudeCode);

/// <summary>
/// One attachable bundle from the catalog (CARD-0058). The catalog is CODE — markdown files in the
/// repo — so this is read-only everywhere; the only thing an operator chooses is which agent carries
/// which key.
/// </summary>
public sealed record InstructionBundleDto(
    string Key,
    // Content hash of the bundle file. Changes when the file changes; there is nothing to bump.
    string Version,
    // "board-api v1a2b3c4d" — the same string that rides the composed output and the drift stamp.
    string Stamp,
    // First line of the file, for the picker. The bundles lead with a title line by convention.
    string Summary,
    int Chars);

public sealed record AgentTuiConfiguredSelectionDto(
    Guid? TuiProfileId,
    string? ModelId,
    string? ProfileDisplayName,
    int? ProfileRevision);

public sealed record AgentTuiLiveSessionSelectionDto(
    Guid? TuiProfileRevisionId,
    string? EffectiveModelId,
    bool PendingRestart);

/// <summary>Supervision snapshot for an always-on agent (see AgentSupervisionState).</summary>
public sealed record AgentSupervisionDto(
    bool Suspended,
    int ConsecutiveFailures,
    DateTime? NextRestartAt,
    int LastEscalationTier);

public sealed record AgentIncidentDto(
    Guid Id,
    Guid AgentId,
    Guid? SessionId,
    AgentIncidentKind Kind,
    AlertSeverity Severity,
    string Message,
    int? ExitCode,
    string? FailureReason,
    DateTime CreatedAt);

public sealed record AgentQueueCardDto(
    Guid CardId,
    Guid BoardId,
    string BoardName,
    string Identifier,
    string Title,
    int Priority,
    int QueuePosition,
    Guid? ActiveWorkflowRunId,
    CardWorkflowRunStatus? WorkflowStatus,
    string? CurrentStageName);

public sealed record CreateAgentRequest(
    string Name,
    string WorkingDirectory,
    /// <summary>
    /// Standing job for this agent (CLAUDE.md "## Your job"). Not delivered as a first prompt on
    /// start. To give a cardless agent work, pass <see cref="StartAgentRequest.Prompt"/> or
    /// <c>POST /api/sessions/{id}/messages</c>. Card work uses the card description, not this field.
    /// </summary>
    string? Details = null,
    Guid? DefaultWorkflowTemplateId = null,
    AgentAssignmentPolicy AssignmentPolicy = AgentAssignmentPolicy.AutoPick,
    bool CreateWorkingDirectory = false,
    // Null = High (the default level - the Opus tier - unless picked otherwise).
    AgentModelLevel? ModelLevel = null,
    // Null/omitted = installation default profile.
    Guid? TuiProfileId = null,
    // Null/omitted = runner default model (no exact --model argument).
    string? ModelId = null,
    // CARD-0060 / CARD-0032. Create carries ReplyStyle, SystemPromptAppend and BundleKeys so a
    // standing orchestrator exists with its contract from the first write, not after a PATCH.
    AgentReplyStyle ReplyStyle = AgentReplyStyle.Normal,
    // CARD-0160. Null = PtyHost (the only lane that existed before this field).
    SessionBackend? SessionBackend = null,
    // CARD-0008: supervision is part of the agent's identity, not an afterthought — an agent
    // meant to be always-on must never exist unsupervised between a create and a PATCH.
    bool AlwaysOn = false,
    bool RemoteControlEnabled = false,
    // CARD-0082 S2. Null = use the global ContextCompactionSettings value.
    bool? AutoCompactEnabled = null,
    int? AutoCompactIdleMinutes = null,
    int? AutoCompactContextPercent = null,
    // Null/omitted = inherit the project's only board, or create the first board for a new project.
    Guid? BoardId = null,
    // CARD-0032. Null = none. Applied the same way UpdateAsync applies them.
    IReadOnlyList<string>? BundleKeys = null,
    string? SystemPromptAppend = null);

public sealed record DraftAgentRequest(string Description);

public sealed record DraftAgentResponse(
    string Name,
    string WorkingDirectory,
    string Details,
    AgentAssignmentPolicy AssignmentPolicy,
    bool UsedAi);

public sealed record UpdateAgentRequest(
    string Name,
    string WorkingDirectory,
    /// <summary>
    /// Standing job for this agent (CLAUDE.md "## Your job"). Not delivered as a first prompt on
    /// start. See <see cref="CreateAgentRequest.Details"/>.
    /// </summary>
    string? Details,
    Guid? DefaultWorkflowTemplateId,
    AgentAssignmentPolicy AssignmentPolicy,
    // Null = leave unchanged. Every agent keeps a default board — an update can move it to
    // another board, never clear the link.
    Guid? BoardId = null,
    // Null = leave unchanged (keeps older callers working).
    bool? AlwaysOn = null,
    bool? RemoteControlEnabled = null,
    // Null = leave unchanged; empty/whitespace = clear.
    string? SystemPromptAppend = null,
    // Null = leave unchanged.
    AgentModelLevel? ModelLevel = null,
    // Null = leave profile selection unchanged. When set, ModelId is applied too (null clears exact model).
    Guid? TuiProfileId = null,
    string? ModelId = null,
    // Null = leave unchanged (CARD-0060), so an older caller cannot reset a chosen style to Normal.
    AgentReplyStyle? ReplyStyle = null,
    // CARD-0160. Null = leave unchanged, so an older caller cannot silently reset a chosen backend
    // to PtyHost. Applied only after the Kind refusal check (CARD-0186 lifted AlwaysOn / channel-bound).
    SessionBackend? SessionBackend = null,
    // The bundles this agent carries on top of what its role implies (CARD-0058 slice 6). Null =
    // leave unchanged, same reason as ReplyStyle: an older caller must not silently detach
    // everything. An EMPTY list is the explicit "detach all". Order is composition order; unknown or
    // style keys are rejected 422 rather than dropped, because a key an operator typed that names
    // nothing is a mistake worth hearing about.
    IReadOnlyList<string>? BundleKeys = null,
    // CARD-0082 S2. Unlike AlwaysOn (null = leave unchanged), these three ARE applied even when
    // null: null on the entity means "use the global ContextCompactionSettings value", and the
    // settings modal round-trips the empty/use-default state as JSON null. An older caller that
    // omits them therefore resets any override to the installation default — the safe direction,
    // and these columns are new.
    bool? AutoCompactEnabled = null,
    int? AutoCompactIdleMinutes = null,
    int? AutoCompactContextPercent = null,
    // CARD-0106 S2. Null = leave unchanged (same contract as ReplyStyle/BundleKeys: an older caller
    // must not silently wipe an environment somebody configured). An EMPTY dictionary is the
    // explicit "clear it". Values may carry {{key:NAME}}; a placeholder in a NAME is rejected 422.
    IReadOnlyDictionary<string, string>? LaunchEnv = null,
    // CARD-0139. Null = leave unchanged (the convention every optional field on this record follows).
    // ASSERT-OR-SET, not a free setter: with a TuiProfileId attached — the agent's existing one, or
    // one this same request supplies — Kind is DERIVED from that profile (CARD-0138 D1) and this
    // value is only checked against it; a disagreement is refused rather than written. It is applied
    // as a value only for an agent with no profile at all, and never for a pool delegate.
    AgentKind? Kind = null);

// Fresh forces a brand-new conversation; by default a cardless (interactive) start resumes the
// agent's previous Claude session so the terminal picks up where it left off.
// RemoteControl: null = use the agent's persisted RemoteControlEnabled setting (the normal case);
// true/false override for this start only.
public sealed record StartAgentRequest(
    bool? RemoteControl = null,
    bool Fresh = false,
    /// <summary>
    /// Bypass the CARD-0136 subscription-quota launch gate. Default false: a fresh low
    /// reading refuses the start with 409 <c>subscription_quota_low</c>. Internal
    /// callers that cannot choose a provider pass true.
    /// </summary>
    bool IgnoreSubscriptionQuota = false,
    /// <summary>
    /// Overlay on this launch only (CARD-0106). Not persisted; an AlwaysOn restart or
    /// resume-recovery rebuilds from the agent's stored <c>launchEnv</c>. ANTIPHON_* names
    /// are refused 422. Does not cascade to child tasks — that is what a project default is for.
    /// </summary>
    IReadOnlyDictionary<string, string>? LaunchEnvOverride = null,
    /// <summary>
    /// Optional first work prompt for a <c>cardless</c> start (CARD-0283). Enqueued WhenIdle after
    /// boot (after remote-control setup and any launch note) and typed once the composer is idle.
    /// <see cref="CreateAgentRequest.Details"/> is standing-job metadata and is never used as this
    /// body. Omit to leave the session idle at the composer — the designed empty-shell path
    /// (AlwaysOn, channel-bound, UI Start, "task later via POST /api/sessions/{id}/messages").
    /// 422 when this agent has spawnable card work: the card description is already the prompt.
    /// Ignored when Start is a no-op because a live session already exists; send a session
    /// message instead. Running does not mean a prompt was delivered.
    /// </summary>
    string? Prompt = null);

/// <summary>CARD-0213: bind a standing Herdr agent to an existing operator pane.</summary>
public sealed record AttachHerdrPaneRequest(string PaneId);

/// <summary>
/// CARD-0214: result of <c>POST /api/agents/{id}/ensure-directory</c>. Creates the agent's
/// already-configured working directory; never takes a path from the caller.
/// </summary>
public sealed record EnsureWorkingDirectoryResultDto(Guid AgentId, string WorkingDirectory);

public sealed record AssignAgentCardRequest(Guid CardId);

public sealed record ReorderAgentQueueRequest(IReadOnlyList<Guid> CardIds);

public sealed record AgentChangedEventDto(Guid AgentId);

public sealed record PreamblePresetDto(string Template);

public sealed record AgentQueueChangedEventDto(
    Guid AgentId,
    Guid? CardId = null,
    IReadOnlyList<Guid>? CardIds = null,
    Guid? BoardId = null);
