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
    int QueueLength,
    DateTime CreatedAt,
    DateTime UpdatedAt);

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
    IReadOnlyList<AgentQueueCardDto> Queue,
    DateTime CreatedAt,
    DateTime UpdatedAt);

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
    AgentAssignmentPolicy AssignmentPolicy = AgentAssignmentPolicy.AutoPick);

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
    AgentAssignmentPolicy AssignmentPolicy);

public sealed record AssignAgentCardRequest(Guid CardId);

public sealed record ReorderAgentQueueRequest(IReadOnlyList<Guid> CardIds);

public sealed record AgentChangedEventDto(Guid AgentId);

public sealed record AgentQueueChangedEventDto(
    Guid AgentId,
    Guid? CardId = null,
    IReadOnlyList<Guid>? CardIds = null,
    Guid? BoardId = null);
