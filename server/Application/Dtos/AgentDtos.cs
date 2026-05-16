using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record AgentRegistryDto(
    string DefaultDefinition,
    IReadOnlyList<AgentDefinitionDto> Definitions);

public sealed record AgentDefinitionDto(
    string Name,
    AgentKind Kind,
    bool IsDefault);
