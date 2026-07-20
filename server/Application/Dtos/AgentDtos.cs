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
    AgentSessionSummaryDto? LiveSession = null);

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
    AgentSessionSummaryDto? LiveSession = null);

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
    bool CreateWorkingDirectory = false);

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
    Guid? BoardId = null);

// Fresh forces a brand-new conversation; by default a cardless (interactive) start resumes the
// agent's previous Claude session so the terminal picks up where it left off.
// RemoteControl: null = use the agent's persisted RemoteControlEnabled setting (the normal case);
// true/false override for this start only.
public sealed record StartAgentRequest(bool? RemoteControl = null, bool Fresh = false);

public sealed record AssignAgentCardRequest(Guid CardId);

public sealed record ReorderAgentQueueRequest(IReadOnlyList<Guid> CardIds);

public sealed record AgentChangedEventDto(Guid AgentId);

public sealed record AgentQueueChangedEventDto(
    Guid AgentId,
    Guid? CardId = null,
    IReadOnlyList<Guid>? CardIds = null,
    Guid? BoardId = null);
