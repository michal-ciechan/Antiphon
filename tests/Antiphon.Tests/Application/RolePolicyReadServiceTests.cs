using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0097 S1: <see cref="RolePolicyReadService.Compose"/> is pure — settings plus a pin list,
/// no DB. Expected aliases are literals (F-1 in
/// docs/superpowers/plans/2026-10-13-card-0097-test-design.md).
/// </summary>
[Category("Unit")]
public sealed class RolePolicyReadServiceTests
{
    [Test]
    public void Compose_resolves_alias_from_kind()
    {
        var settings = new DelegationSettings();
        settings.RolePolicy["Docs"].Kind = AgentKind.Grok;
        settings.RolePolicy["Commit"].Kind = AgentKind.Codex;
        settings.RolePolicy["Debug"].EscalateAfterMinutes = 7;

        var dto = RolePolicyReadService.Compose(settings, []);

        var docs = Row(dto, AgentTaskRole.Docs);
        docs.Kind.ShouldBe(AgentKind.Grok);
        docs.Alias.ShouldBe("grok-4.7");
        docs.Effective.AgentKind.ShouldBe(AgentKind.Grok);
        docs.Effective.Alias.ShouldBe("grok-4.7");
        Row(dto, AgentTaskRole.Commit).Alias.ShouldBe("gpt-6.1-sol");
        var code = Row(dto, AgentTaskRole.Code);
        code.Kind.ShouldBeNull();
        code.Alias.ShouldBe("fable");
        code.Effective.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        Row(dto, AgentTaskRole.Debug).EscalateAfterMinutes.ShouldBe(7);
    }

    [Test]
    public void Compose_lists_RoutableRoles_in_order_and_marks_Custom_unconfigured()
    {
        var settings = new DelegationSettings
        {
            DefaultLevel = AgentModelLevel.Medium,
            DefaultTimeoutMinutes = 90,
        };

        var dto = RolePolicyReadService.Compose(settings, []);

        dto.DefaultLevel.ShouldBe(AgentModelLevel.Medium);
        dto.DefaultTimeoutMinutes.ShouldBe(90);
        dto.Roles.Select(r => r.Role).ShouldBe(
        [
            AgentTaskRole.Investigate, AgentTaskRole.Plan, AgentTaskRole.TestDesign, AgentTaskRole.Code,
            AgentTaskRole.Mutation, AgentTaskRole.Review, AgentTaskRole.Debug, AgentTaskRole.Coverage,
            AgentTaskRole.Docs, AgentTaskRole.Commit, AgentTaskRole.Test, AgentTaskRole.Deploy,
            AgentTaskRole.Merge, AgentTaskRole.Custom,
        ]);
        var custom = Row(dto, AgentTaskRole.Custom);
        custom.Configured.ShouldBeFalse();
        custom.Level.ShouldBe(AgentModelLevel.Medium);
        custom.Alias.ShouldBe("sonnet");
        custom.TimeoutMinutes.ShouldBe(90);
        custom.EscalateTo.ShouldBeNull();
        custom.Kind.ShouldBeNull();
        var plan = Row(dto, AgentTaskRole.Plan);
        plan.Configured.ShouldBeTrue();
        plan.Level.ShouldBe(AgentModelLevel.Frontier);
        plan.TimeoutMinutes.ShouldBe(240);
    }

    [Test]
    public void Compose_effective_prefers_the_first_stage_pin_candidate_and_excludes_card_pins()
    {
        var planPin = Pin(
            AgentTaskRole.Plan,
            RoutingPinStrength.Preferred,
            cardId: null,
            Candidate(AgentKind.Codex, AgentModelLevel.Frontier, "gpt-6-astra"),
            Candidate(AgentKind.ClaudeCode, AgentModelLevel.High, "opus"));
        var codeCardPin = Pin(
            AgentTaskRole.Code,
            RoutingPinStrength.Required,
            cardId: Guid.NewGuid(),
            Candidate(AgentKind.Grok, AgentModelLevel.Frontier, "grok-4.7"));

        var dto = RolePolicyReadService.Compose(new DelegationSettings(), [planPin, codeCardPin]);

        var plan = Row(dto, AgentTaskRole.Plan);
        plan.StagePin.ShouldBe(planPin);
        plan.Effective.ShouldBe(new RolePolicyEffectiveDto(
            "pin", AgentKind.Codex, AgentModelLevel.Frontier, "gpt-6-astra", RoutingPinStrength.Preferred));
        var code = Row(dto, AgentTaskRole.Code);
        code.StagePin.ShouldBeNull();
        code.Effective.Source.ShouldBe("rolePolicy");
    }

    [Test]
    public void Compose_fills_a_missing_pin_level_from_the_policy()
    {
        var pin = Pin(
            AgentTaskRole.Docs,
            RoutingPinStrength.Preferred,
            cardId: null,
            Candidate(AgentKind.Grok, null, null));

        var dto = RolePolicyReadService.Compose(new DelegationSettings(), [pin]);

        Row(dto, AgentTaskRole.Docs).Effective.ShouldBe(new RolePolicyEffectiveDto(
            "pin", AgentKind.Grok, AgentModelLevel.Medium, "grok-4.7", RoutingPinStrength.Preferred));
    }

    [Test]
    public void Compose_fills_a_missing_pin_kind_from_the_policy()
    {
        var pin = Pin(
            AgentTaskRole.Docs,
            RoutingPinStrength.Required,
            cardId: null,
            Candidate(null, AgentModelLevel.High, null));

        var claude = RolePolicyReadService.Compose(new DelegationSettings(), [pin]);

        Row(claude, AgentTaskRole.Docs).Effective.ShouldBe(new RolePolicyEffectiveDto(
            "pin", AgentKind.ClaudeCode, AgentModelLevel.High, "opus", RoutingPinStrength.Required));

        var settings = new DelegationSettings();
        settings.RolePolicy["Docs"].Kind = AgentKind.Grok;
        var grok = RolePolicyReadService.Compose(settings, [pin]);

        Row(grok, AgentTaskRole.Docs).Effective.ShouldBe(new RolePolicyEffectiveDto(
            "pin", AgentKind.Grok, AgentModelLevel.High, "grok-4.7", RoutingPinStrength.Required));
    }

    [Test]
    public void Compose_withholds_the_target_for_a_candidate_free_pin()
    {
        var pin = Pin(AgentTaskRole.Docs, RoutingPinStrength.Required, cardId: null);

        var dto = RolePolicyReadService.Compose(new DelegationSettings(), [pin]);

        var docs = Row(dto, AgentTaskRole.Docs);
        docs.StagePin.ShouldBe(pin);
        docs.Effective.ShouldBe(new RolePolicyEffectiveDto(
            "pin", null, null, null, RoutingPinStrength.Required));
    }

    /// <summary>One configured or pinned value per case; the expected wire values are literals.</summary>
    public sealed record UnsupportedCase(
        string Name,
        Action<DelegationSettings> Configure,
        RoutingPinCandidateDto[]? PinCandidates,
        string Role,
        string? Kind,
        string Level,
        string Alias,
        string? EscalateTo,
        string Source,
        string? EffectiveKind,
        string? EffectiveLevel,
        string? EffectiveAlias,
        string? Strength,
        string? HeadKind,
        string? HeadLevel,
        string? HeadAlias,
        string DefaultLevel)
    {
        public override string ToString() => Name;
    }

    private static readonly AgentKind UndefinedKind = (AgentKind)999;
    private static readonly AgentModelLevel UndefinedLevel = (AgentModelLevel)999;

    public static IEnumerable<Func<UnsupportedCase>> UnsupportedCases()
    {
        static void None(DelegationSettings _) { }

        yield return () => new("valid-control", None, null, "Docs",
            null, "Medium", "sonnet", null, "rolePolicy", "ClaudeCode", "Medium", "sonnet", null, null, null, null, "High");
        yield return () => new("policy-kind-Raw", s => s.RolePolicy["Docs"].Kind = AgentKind.Raw, null, "Docs",
            "Raw", "Medium", "", null, "rolePolicy", null, null, null, null, null, null, null, "High");
        yield return () => new("policy-kind-OpenCode", s => s.RolePolicy["Docs"].Kind = AgentKind.OpenCode, null, "Docs",
            "OpenCode", "Medium", "", null, "rolePolicy", null, null, null, null, null, null, null, "High");
        yield return () => new("policy-kind-999", s => s.RolePolicy["Docs"].Kind = UndefinedKind, null, "Docs",
            null, "Medium", "", null, "rolePolicy", null, null, null, null, null, null, null, "High");
        yield return () => new("policy-level-999", s => s.RolePolicy["Docs"].Level = UndefinedLevel, null, "Docs",
            null, "High", "", null, "rolePolicy", null, null, null, null, null, null, null, "High");
        yield return () => new("policy-escalateTo-999", s => s.RolePolicy["Docs"].EscalateTo = UndefinedLevel, null, "Docs",
            null, "Medium", "sonnet", null, "rolePolicy", "ClaudeCode", "Medium", "sonnet", null, null, null, null, "High");
        yield return () => new("default-level-999", s => s.DefaultLevel = UndefinedLevel, null, "Custom",
            null, "High", "", null, "rolePolicy", null, null, null, null, null, null, null, "High");
        yield return () => new("pin-valid-control", None,
            [Candidate(AgentKind.Codex, AgentModelLevel.Low, "gpt-5.6-luna")], "Docs",
            null, "Medium", "sonnet", null, "pin", "Codex", "Low", "gpt-5.6-luna", "Required", "Codex", "Low", "gpt-5.6-luna", "High");
        yield return () => new("pin-head-kind-Raw", None,
            [Candidate(AgentKind.Raw, AgentModelLevel.High, "opus"), Candidate(AgentKind.ClaudeCode, AgentModelLevel.Low, "haiku")], "Docs",
            null, "Medium", "sonnet", null, "pin", null, null, null, "Required", "Raw", "High", null, "High");
        yield return () => new("pin-head-kind-999", None,
            [Candidate(UndefinedKind, AgentModelLevel.High, "opus"), Candidate(AgentKind.ClaudeCode, AgentModelLevel.Low, "haiku")], "Docs",
            null, "Medium", "sonnet", null, "pin", null, null, null, "Required", null, "High", null, "High");
        yield return () => new("pin-head-level-999", None,
            [Candidate(AgentKind.ClaudeCode, UndefinedLevel, "opus"), Candidate(AgentKind.Codex, AgentModelLevel.Low, "gpt-5.6-luna")], "Docs",
            null, "Medium", "sonnet", null, "pin", null, null, null, "Required", "ClaudeCode", null, null, "High");
        yield return () => new("pin-grok-level-999", None,
            [Candidate(AgentKind.Grok, UndefinedLevel, "grok-4.7")], "Docs",
            null, "Medium", "sonnet", null, "pin", null, null, null, "Required", "Grok", null, null, "High");
        yield return () => new("pin-grok-inherits-policy-level-999", s => s.RolePolicy["Docs"].Level = UndefinedLevel,
            [Candidate(AgentKind.Grok, null, null)], "Docs",
            null, "High", "", null, "pin", null, null, null, "Required", "Grok", null, null, "High");
        yield return () => new("pin-level-inherits-policy-kind-Raw", s => s.RolePolicy["Docs"].Kind = AgentKind.Raw,
            [Candidate(null, AgentModelLevel.High, null)], "Docs",
            "Raw", "Medium", "", null, "pin", null, null, null, "Required", null, "High", null, "High");
        yield return () => new("prototype-role-names", s =>
            {
                foreach (var name in new[] { "__proto__", "constructor", "toString" })
                    s.RolePolicy[name] = new DelegationSettings.RolePolicyEntry { Kind = UndefinedKind, Level = UndefinedLevel };
            }, null, "Docs",
            null, "Medium", "sonnet", null, "rolePolicy", "ClaudeCode", "Medium", "sonnet", null, null, null, null, "High");
    }

    /// <summary>
    /// Unsupported (Raw, OpenCode) or undefined (999) kinds and levels withhold the target instead of
    /// inventing an alias, and the whole DTO still serialises with the API's strict enum converter.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(UnsupportedCases))]
    public void Compose_withholds_the_target_for_unsupported_or_undefined_values(UnsupportedCase c)
    {
        var settings = new DelegationSettings();
        c.Configure(settings);
        var role = Enum.Parse<AgentTaskRole>(c.Role);
        RoutingPinDto[] pins = c.PinCandidates is null
            ? []
            : [Pin(role, RoutingPinStrength.Required, cardId: null, c.PinCandidates)];

        var json = JsonSerializer.SerializeToElement(RolePolicyReadService.Compose(settings, pins), ApiJson);

        json.GetProperty("defaultLevel").GetString().ShouldBe(c.DefaultLevel);
        var roles = json.GetProperty("roles").EnumerateArray().ToList();
        roles.Count.ShouldBe(14);
        roles[0].GetProperty("role").GetString().ShouldBe("Investigate");
        roles[13].GetProperty("role").GetString().ShouldBe("Custom");

        var row = roles.Single(r => r.GetProperty("role").GetString() == c.Role);
        Str(row, "kind").ShouldBe(c.Kind);
        Str(row, "level").ShouldBe(c.Level);
        Str(row, "alias").ShouldBe(c.Alias);
        Str(row, "escalateTo").ShouldBe(c.EscalateTo);
        var effective = row.GetProperty("effective");
        Str(effective, "source").ShouldBe(c.Source);
        Str(effective, "agentKind").ShouldBe(c.EffectiveKind);
        Str(effective, "modelLevel").ShouldBe(c.EffectiveLevel);
        Str(effective, "alias").ShouldBe(c.EffectiveAlias);
        Str(effective, "strength").ShouldBe(c.Strength);

        var stagePin = row.GetProperty("stagePin");
        if (c.PinCandidates is null)
        {
            stagePin.ValueKind.ShouldBe(JsonValueKind.Null);
        }
        else
        {
            Str(stagePin, "agentKind").ShouldBe(c.HeadKind);
            Str(stagePin, "modelLevel").ShouldBe(c.HeadLevel);
            Str(stagePin, "modelAlias").ShouldBe(c.HeadAlias);
            var candidates = stagePin.GetProperty("candidates").EnumerateArray().ToList();
            candidates.Count.ShouldBe(c.PinCandidates.Length);
            Str(candidates[0], "agentKind").ShouldBe(c.HeadKind);
            Str(candidates[0], "modelLevel").ShouldBe(c.HeadLevel);
            Str(candidates[0], "alias").ShouldBe(c.HeadAlias);
        }

        // The other 13 rows keep their shipped targets.
        var mutation = roles.Single(r => r.GetProperty("role").GetString() == "Mutation").GetProperty("effective");
        Str(mutation, "source").ShouldBe("rolePolicy");
        Str(mutation, "agentKind").ShouldBe("ClaudeCode");
        Str(mutation, "modelLevel").ShouldBe("Low");
        Str(mutation, "alias").ShouldBe("haiku");
        Str(roles.Single(r => r.GetProperty("role").GetString() == "Code"), "alias").ShouldBe("fable");
        foreach (var other in roles.Where(r => r.GetProperty("role").GetString() != c.Role))
            Str(other.GetProperty("effective"), "alias").ShouldNotBeNullOrEmpty();
    }

    /// <summary>The API's options: web defaults plus Program.cs's strict string enum converter.</summary>
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    private static string? Str(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static RolePolicyRowDto Row(RolePolicyDto dto, AgentTaskRole role) =>
        dto.Roles.Single(r => r.Role == role);

    private static RoutingPinCandidateDto Candidate(AgentKind? kind, AgentModelLevel? level, string? alias) =>
        new(kind, level, alias, true, null);

    private static RoutingPinDto Pin(
        AgentTaskRole role,
        RoutingPinStrength strength,
        Guid? cardId,
        params RoutingPinCandidateDto[] candidates)
    {
        var head = candidates.Length > 0 ? candidates[0] : null;
        var now = new DateTime(2026, 10, 13, 16, 47, 0, DateTimeKind.Utc);
        return new RoutingPinDto(
            Guid.NewGuid(),
            cardId,
            cardId is null ? null : "CARD-9097",
            role,
            RoutingPinProvenance.Human,
            strength,
            head?.AgentKind,
            head?.ModelLevel,
            head?.Alias,
            null,
            [],
            null,
            null,
            "C097 compose",
            null,
            now,
            now,
            candidates,
            candidates.Length);
    }
}
