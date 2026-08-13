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
    AgentTuiLiveSessionSelectionDto? LiveSessionSelection = null);

public sealed record AgentDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string WorkingDirectory,
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
    AgentTuiLiveSessionSelectionDto? LiveSessionSelection = null);

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
    string? Details = null,
    Guid? DefaultWorkflowTemplateId = null,
    AgentAssignmentPolicy AssignmentPolicy = AgentAssignmentPolicy.AutoPick,
    bool CreateWorkingDirectory = false,
    // Null = High (the default level - the Opus tier - unless picked otherwise).
    AgentModelLevel? ModelLevel = null,
    // Null/omitted = installation default profile.
    Guid? TuiProfileId = null,
    // Null/omitted = runner default model (no exact --model argument).
    string? ModelId = null);

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
    string? ModelId = null);

// Fresh forces a brand-new conversation; by default a cardless (interactive) start resumes the
// agent's previous Claude session so the terminal picks up where it left off.
// RemoteControl: null = use the agent's persisted RemoteControlEnabled setting (the normal case);
// true/false override for this start only.
public sealed record StartAgentRequest(bool? RemoteControl = null, bool Fresh = false);

public sealed record AssignAgentCardRequest(Guid CardId);

public sealed record ReorderAgentQueueRequest(IReadOnlyList<Guid> CardIds);

public sealed record AgentChangedEventDto(Guid AgentId);

public sealed record PreamblePresetDto(string Template);

public sealed record AgentQueueChangedEventDto(
    Guid AgentId,
    Guid? CardId = null,
    IReadOnlyList<Guid>? CardIds = null,
    Guid? BoardId = null);
