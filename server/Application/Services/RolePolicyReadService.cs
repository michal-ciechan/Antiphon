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
    /// A defined but non-delegatable kind (Raw, OpenCode) stays in the row's kind field and gets no alias
    /// (RolePolicyReadServiceTests.Compose_withholds_the_target_for_unsupported_or_undefined_values row policy-kind-Raw).
    /// An undefined kind or level is written as null
    /// (RolePolicyEndpointTests.Get_serialises_every_row_when_one_role_has_unsupported_values).
    /// </summary>
    public static RolePolicyDto Compose(DelegationSettings settings, IReadOnlyList<RoutingPinDto> pins)
    {
        var rows = ComplexityRoutingService.RoutableRoles
            .Select(role => Row(
                settings,
                role,
                pins.FirstOrDefault(p => p.CardId is null && p.Role == role)))
            .ToList();
        return new RolePolicyDto(Defined<AgentModelLevel>(settings.DefaultLevel), settings.DefaultTimeoutMinutes, rows);
    }

    private static RolePolicyRowDto Row(DelegationSettings settings, AgentTaskRole role, RoutingPinDto? stagePin)
    {
        var configured = settings.RolePolicy.TryGetValue(role.ToString(), out var entry) && entry is not null;
        var level = configured ? entry!.Level : settings.DefaultLevel;
        var kind = configured ? entry!.Kind : null;
        var supported = Supported(kind) && Enum.IsDefined(level);
        var alias = supported ? ModelLevelAliases.For(kind ?? AgentKind.ClaudeCode, level) : null;
        var escalateTo = configured ? entry!.EscalateTo : null;
        return new RolePolicyRowDto(
            role,
            configured,
            Defined<AgentModelLevel>(level),
            Defined(kind),
            alias ?? NoAlias,
            Defined(escalateTo),
            configured ? entry!.EscalateAfterMinutes : null,
            configured ? entry!.TimeoutMinutes : settings.DefaultTimeoutMinutes,
            stagePin is null ? null : Writable(stagePin),
            Effective(stagePin, kind, level, alias));
    }

    /// <summary>
    /// The pin's head candidate fills from the policy where it is partial; a pin with no candidate
    /// or an unsupported head withholds the target (RolePolicyReadServiceTests.Compose_fills_a_missing_pin_level_from_the_policy,
    /// Compose_fills_a_missing_pin_kind_from_the_policy, Compose_withholds_the_target_for_a_candidate_free_pin,
    /// Compose_withholds_the_target_for_unsupported_or_undefined_values).
    /// </summary>
    private static RolePolicyEffectiveDto Effective(
        RoutingPinDto? stagePin, AgentKind? kind, AgentModelLevel level, string? alias)
    {
        if (stagePin is null)
            return alias is null
                ? new RolePolicyEffectiveDto(SourceRolePolicy, null, null, null, null)
                : new RolePolicyEffectiveDto(SourceRolePolicy, kind ?? AgentKind.ClaudeCode, level, alias, null);

        var head = stagePin.Candidates is { Count: > 0 } candidates ? candidates[0] : null;
        var effectiveKind = head?.AgentKind ?? kind ?? AgentKind.ClaudeCode;
        var effectiveLevel = head?.ModelLevel ?? level;
        if (head is null || !Supported(effectiveKind) || !Enum.IsDefined(effectiveLevel))
            return new RolePolicyEffectiveDto(SourcePin, null, null, null, stagePin.Strength);

        return new RolePolicyEffectiveDto(
            SourcePin,
            effectiveKind,
            effectiveLevel,
            ModelLevelAliases.For(effectiveKind, effectiveLevel),
            stagePin.Strength);
    }

    /// <summary>A row whose policy kind or level is unsupported names no alias.</summary>
    public const string NoAlias = "";

    /// <summary>Null (no configured kind) or a kind delegation launches (<see cref="AgentTaskService.DelegatableKinds"/>).</summary>
    private static bool Supported(AgentKind? kind) =>
        kind is null || AgentTaskService.DelegatableKinds.Contains(kind.Value);

    /// <summary>
    /// Undefined kinds and levels in the pin become null, and an alias beside an unsupported kind or
    /// level is dropped (RolePolicyReadServiceTests.Compose_withholds_the_target_for_unsupported_or_undefined_values
    /// rows pin-head-kind-Raw, pin-head-kind-999, pin-head-level-999).
    /// </summary>
    private static RoutingPinDto Writable(RoutingPinDto pin)
    {
        var candidates = pin.Candidates?.Select(c => c with
        {
            AgentKind = Defined(c.AgentKind),
            ModelLevel = Defined(c.ModelLevel),
            Alias = Usable(c.AgentKind, c.ModelLevel) ? c.Alias : null,
        }).ToList();
        var unchanged = Usable(pin.AgentKind, pin.ModelLevel)
            && (pin.Candidates is null || pin.Candidates.All(c => Usable(c.AgentKind, c.ModelLevel)));
        return unchanged
            ? pin
            : pin with
            {
                AgentKind = Defined(pin.AgentKind),
                ModelLevel = Defined(pin.ModelLevel),
                ModelAlias = Usable(pin.AgentKind, pin.ModelLevel) ? pin.ModelAlias : null,
                Candidates = candidates,
            };
    }

    private static bool Usable(AgentKind? kind, AgentModelLevel? level) =>
        Supported(kind) && (level is null || Enum.IsDefined(level.Value));

    private static T? Defined<T>(T? value) where T : struct, Enum =>
        value is { } v && Enum.IsDefined(v) ? v : null;
}
