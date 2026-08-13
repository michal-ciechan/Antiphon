using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record AgentTuiModelWriteDto(
    string Identifier,
    string DisplayName,
    string? Family = null,
    bool IsSuggestedDefault = false);

public sealed record AgentTuiProfileWriteRequest(
    string DisplayName,
    AgentKind Kind,
    bool IsEnabled,
    bool IsDefault,
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> DiscoveryArguments,
    IReadOnlyList<string> VersionArguments,
    string? WorkingDirectory,
    AgentTuiAuthenticationMode AuthenticationMode,
    IReadOnlyDictionary<string, string> NonSecretEnvironment,
    IReadOnlyList<string> SecretEnvironmentNames,
    string? ModelArgumentName,
    string Guidance,
    IReadOnlyList<AgentTuiModelWriteDto> Models,
    int? ExpectedRevision = null);

public sealed record DuplicateAgentTuiProfileRequest(string DisplayName);

public sealed record AgentTuiSecretWriteRequest(
    string Value,
    int ExpectedRevision,
    string CorrelationId);

public sealed record AgentTuiSecretClearRequest(
    int ExpectedRevision,
    string CorrelationId);

public sealed record AgentTuiSecretMetadataDto(
    string Name,
    bool Configured,
    DateTime? UpdatedAt);

public sealed record AgentTuiSecretMutationDto(
    string Name,
    bool Configured,
    DateTime UpdatedAt,
    int Revision);

public sealed record AgentTuiProfileRevisionDto(
    Guid Id,
    int Revision,
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> DiscoveryArguments,
    IReadOnlyList<string> VersionArguments,
    string? WorkingDirectory,
    AgentTuiAuthenticationMode AuthenticationMode,
    IReadOnlyDictionary<string, string> NonSecretEnvironment,
    IReadOnlyList<string> SecretEnvironmentNames,
    string? ModelArgumentName,
    string Guidance,
    DateTime CreatedAt);

public sealed record AgentTuiModelDto(
    string Identifier,
    string DisplayName,
    string? Family,
    AgentTuiModelSource Source,
    AgentTuiModelAvailability Availability,
    DateTime? DiscoveredAt,
    string? RunnerVersion,
    bool IsSuggestedDefault);

public sealed record AgentTuiCapabilityDto(
    string Name,
    AgentTuiCapabilityState State,
    string Reason);

public sealed record AgentTuiRunnerTypeDto(
    AgentKind Kind,
    string DisplayName,
    string Description,
    string? DefaultModelArgumentName,
    IReadOnlyList<AgentTuiAuthenticationMode> AuthenticationModes,
    IReadOnlyList<AgentTuiModelDto> CuratedModels,
    IReadOnlyList<AgentTuiCapabilityDto> Capabilities,
    string Guidance);

public sealed record AgentTuiProfileDto(
    Guid Id,
    string DisplayName,
    AgentKind Kind,
    bool IsEnabled,
    bool IsDefault,
    AgentTuiProfileSource Source,
    string? SourceDefinitionName,
    Guid RevisionId,
    int Revision,
    AgentTuiProfileRevisionDto RevisionDetails,
    IReadOnlyList<AgentTuiSecretMetadataDto> SecretEnvironment,
    IReadOnlyList<AgentTuiModelDto> Models,
    IReadOnlyList<AgentTuiCapabilityDto> Capabilities,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record AgentTuiImportResultDto(
    int ProfilesCreated,
    int AgentsAssigned);

public enum AgentTuiValidationStageStatus
{
    Passed = 0,
    Failed = 1,
    Skipped = 2,
    Degraded = 3
}

public sealed record AgentTuiValidationStageDto(
    string Name,
    AgentTuiValidationStageStatus Status,
    string Message);

public sealed record AgentTuiSuitabilityDto(
    bool Interactive,
    bool Queued,
    bool Delegated,
    bool Resumable);

public sealed record AgentTuiValidationRunDto(
    Guid Id,
    Guid ProfileId,
    Guid ProfileRevisionId,
    string Operation,
    AgentTuiValidationStatus Status,
    IReadOnlyList<AgentTuiValidationStageDto> Stages,
    IReadOnlyList<AgentTuiCapabilityDto> Capabilities,
    string? RunnerVersion,
    string Summary,
    AgentTuiSuitabilityDto Suitability,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);
