using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0097 S1: the read model behind GET /api/role-policy.
/// </summary>
public sealed class RolePolicyReadService
{
    public const string SourcePin = "pin";
    public const string SourceRolePolicy = "rolePolicy";

    private readonly DelegationSettings _settings;
    private readonly RoutingPinService _pins;

    public RolePolicyReadService(IOptions<DelegationSettings> settings, RoutingPinService pins)
    {
        _settings = settings.Value;
        _pins = pins;
    }

    /// <summary>
    /// Reads through <see cref="RoutingPinService.ListAsync"/>, which drops cleared pins
    /// (RolePolicyEndpointTests.Get_ignores_card_pins_and_cleared_pins).
    /// </summary>
    public async Task<RolePolicyDto> ReadAsync(CancellationToken ct) =>
        Compose(_settings, await _pins.ListAsync(null, null, ct));

    /// <summary>
    /// One row per <see cref="ComplexityRoutingService.RoutableRoles"/> entry. Card pins are
    /// skipped (RolePolicyReadServiceTests.Compose_effective_prefers_the_first_stage_pin_candidate_and_excludes_card_pins).
    /// </summary>
    public static RolePolicyDto Compose(DelegationSettings settings, IReadOnlyList<RoutingPinDto> pins)
    {
        var rows = ComplexityRoutingService.RoutableRoles
            .Select(role => Row(
                settings,
                role,
                pins.FirstOrDefault(p => p.CardId is null && p.Role == role)))
            .ToList();
        return new RolePolicyDto(settings.DefaultLevel, settings.DefaultTimeoutMinutes, rows);
    }

    private static RolePolicyRowDto Row(DelegationSettings settings, AgentTaskRole role, RoutingPinDto? stagePin)
    {
        var configured = settings.RolePolicy.TryGetValue(role.ToString(), out var entry) && entry is not null;
        var level = configured ? entry!.Level : settings.DefaultLevel;
        var kind = configured ? entry!.Kind : null;
        var alias = ModelLevelAliases.For(kind ?? AgentKind.ClaudeCode, level);
        return new RolePolicyRowDto(
            role,
            configured,
            level,
            kind,
            alias,
            configured ? entry!.EscalateTo : null,
            configured ? entry!.EscalateAfterMinutes : null,
            configured ? entry!.TimeoutMinutes : settings.DefaultTimeoutMinutes,
            stagePin,
            Effective(stagePin, kind, level, alias));
    }

    /// <summary>
    /// The pin's head candidate fills from the policy where it is partial; a pin with no candidate
    /// withholds the target (RolePolicyReadServiceTests.Compose_fills_a_missing_pin_level_from_the_policy,
    /// Compose_fills_a_missing_pin_kind_from_the_policy, Compose_withholds_the_target_for_a_candidate_free_pin).
    /// </summary>
    private static RolePolicyEffectiveDto Effective(
        RoutingPinDto? stagePin, AgentKind? kind, AgentModelLevel level, string alias)
    {
        if (stagePin is null)
            return new RolePolicyEffectiveDto(SourceRolePolicy, kind ?? AgentKind.ClaudeCode, level, alias, null);

        var head = stagePin.Candidates is { Count: > 0 } candidates ? candidates[0] : null;
        if (head is null)
            return new RolePolicyEffectiveDto(SourcePin, null, null, null, stagePin.Strength);

        var effectiveKind = head.AgentKind ?? kind ?? AgentKind.ClaudeCode;
        var effectiveLevel = head.ModelLevel ?? level;
        return new RolePolicyEffectiveDto(
            SourcePin,
            effectiveKind,
            effectiveLevel,
            ModelLevelAliases.For(effectiveKind, effectiveLevel),
            stagePin.Strength);
    }
}
