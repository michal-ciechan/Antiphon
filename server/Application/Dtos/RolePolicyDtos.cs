using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// GET /api/role-policy (CARD-0097). One row per <c>ComplexityRoutingService.RoutableRoles</c>
/// entry, in that order. Read-only.
/// </summary>
public sealed record RolePolicyDto(
    /// <summary>Null when the configured value is not a defined level.</summary>
    AgentModelLevel? DefaultLevel,
    int DefaultTimeoutMinutes,
    IReadOnlyList<RolePolicyRowDto> Roles);

/// <summary>
/// One role's <c>RolePolicy</c> entry beside its active stage-wide pin. An unconfigured role
/// reports <c>DefaultLevel</c>/<c>DefaultTimeoutMinutes</c> (RolePolicyReadServiceTests
/// .Compose_lists_RoutableRoles_in_order_and_marks_Custom_unconfigured).
/// </summary>
public sealed record RolePolicyRowDto(
    AgentTaskRole Role,
    bool Configured,
    /// <summary>Null when the configured value is not a defined level.</summary>
    AgentModelLevel? Level,
    /// <summary>Null means ClaudeCode.</summary>
    AgentKind? Kind,
    string Alias,
    AgentModelLevel? EscalateTo,
    int? EscalateAfterMinutes,
    int TimeoutMinutes,
    RoutingPinDto? StagePin,
    RolePolicyEffectiveDto Effective);

/// <summary>The next create's target for the role: <c>pin</c> or <c>rolePolicy</c>.</summary>
public sealed record RolePolicyEffectiveDto(
    string Source,
    AgentKind? AgentKind,
    AgentModelLevel? ModelLevel,
    string? Alias,
    RoutingPinStrength? Strength);
