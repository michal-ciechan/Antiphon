using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Task 8333e231 — <c>Delegation:ClaudeEffortByRole</c>, the per-role Claude <c>--effort</c>
/// override (operator request 2026-10-22: "all apart from review on opus xhigh"). The pure half:
/// <see cref="ClaudeLaunchArgs.Effort(AgentModelLevel, AgentTaskRole, IReadOnlyDictionary{string, string})"/>
/// and the <see cref="DelegationSettingsValidator"/> rules. Expected messages are literals.
/// </summary>
[Category("Unit")]
public sealed class ClaudeEffortByRoleSettingsTests
{
    private const string NotAnEffort = "which is not a Claude effort. Use low, medium, high, xhigh or max.";

    private static readonly IReadOnlyDictionary<AgentModelLevel, string> TierTable = new Dictionary<AgentModelLevel, string>
    {
        [AgentModelLevel.Frontier] = "xhigh",
        [AgentModelLevel.High] = "high",
        [AgentModelLevel.Medium] = "medium",
        [AgentModelLevel.Low] = "low",
    };

    private static Dictionary<string, string> Map(params (string Role, string Effort)[] entries)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, effort) in entries)
            map[role] = effort;
        return map;
    }

    // ---- resolution ----------------------------------------------------------------------------

    [Test]
    public void the_role_override_wins_and_absent_roles_keep_the_tier()
    {
        var map = Map(("Code", "xhigh"));

        ClaudeLaunchArgs.Effort(AgentModelLevel.High, AgentTaskRole.Code, map).ShouldBe("xhigh");
        ClaudeLaunchArgs.Effort(AgentModelLevel.Low, AgentTaskRole.Code, map)
            .ShouldBe("xhigh", "the override is per role, whatever the tier");
        ClaudeLaunchArgs.Effort(AgentModelLevel.High, AgentTaskRole.Review, map).ShouldBe("high");
        ClaudeLaunchArgs.Effort(AgentModelLevel.Frontier, AgentTaskRole.Review, map).ShouldBe("xhigh");
    }

    [Test]
    public void an_empty_or_null_map_is_the_tier_table_for_every_level_and_role()
    {
        foreach (var level in Enum.GetValues<AgentModelLevel>())
        {
            foreach (var role in Enum.GetValues<AgentTaskRole>())
            {
                ClaudeLaunchArgs.Effort(level, role, new Dictionary<string, string>()).ShouldBe(TierTable[level], $"{level}/{role} (empty)");
                ClaudeLaunchArgs.Effort(level, role, null).ShouldBe(TierTable[level], $"{level}/{role} (null)");
            }
        }
    }

    [Test]
    [Arguments("XHIGH", "xhigh")]
    [Arguments(" max ", "max")]
    [Arguments("Low", "low")]
    public void the_value_is_case_insensitive_and_the_catalog_constant_reaches_argv(string configured, string expected)
    {
        var effort = ClaudeLaunchArgs.Effort(AgentModelLevel.High, AgentTaskRole.Docs, Map(("Docs", configured)));

        effort.ShouldBe(expected);
        ReferenceEquals(effort, ClaudeLaunchArgs.CliEfforts.Single(e => e == expected))
            .ShouldBeTrue("the catalog constant, never a normalised copy of the operator's string, reaches argv");
    }

    [Test]
    [Arguments("ultra")]
    [Arguments("")]
    [Arguments("x high")]
    public void an_unknown_value_throws_naming_the_setting_and_never_reaches_argv(string configured)
    {
        var ex = Should.Throw<InvalidOperationException>(
            () => ClaudeLaunchArgs.Effort(AgentModelLevel.High, AgentTaskRole.Code, Map(("Code", configured))));

        ex.Message.ShouldBe($"Delegation:ClaudeEffortByRole:Code is '{configured}', {NotAnEffort}");
    }

    [Test]
    public void the_catalog_is_what_the_pty_effort_gate_accepts()
    {
        ClaudeLaunchArgs.CliEfforts.ShouldBe(new[] { "low", "medium", "high", "xhigh", "max" });
        foreach (var effort in ClaudeLaunchArgs.CliEfforts)
            ClaudeEffortIntent.IsEffort(effort).ShouldBeTrue(effort);
        ClaudeEffortIntent.IsEffort("ultra").ShouldBeFalse();
        ClaudeLaunchArgs.Canonical("ultra").ShouldBeNull();
        ClaudeLaunchArgs.Canonical(null).ShouldBeNull();
    }

    [Test]
    public void a_role_override_differs_from_the_tier_only_when_the_values_differ()
    {
        var map = Map(("Code", "xhigh"), ("Review", "high"), ("Docs", "bogus"));

        ClaudeLaunchArgs.RoleOverrideDiffersFromTier(AgentModelLevel.High, AgentTaskRole.Code, map).ShouldBeTrue();
        ClaudeLaunchArgs.RoleOverrideDiffersFromTier(AgentModelLevel.Frontier, AgentTaskRole.Code, map)
            .ShouldBeFalse("xhigh is Frontier's own effort");
        ClaudeLaunchArgs.RoleOverrideDiffersFromTier(AgentModelLevel.High, AgentTaskRole.Review, map).ShouldBeFalse();
        ClaudeLaunchArgs.RoleOverrideDiffersFromTier(AgentModelLevel.High, AgentTaskRole.Plan, map).ShouldBeFalse("no entry");
        ClaudeLaunchArgs.RoleOverrideDiffersFromTier(AgentModelLevel.High, AgentTaskRole.Docs, map)
            .ShouldBeFalse("an invalid entry is the validator's to refuse, not a reason to decline reuse");
        ClaudeLaunchArgs.RoleOverrideDiffersFromTier(AgentModelLevel.High, AgentTaskRole.Code, null).ShouldBeFalse();
    }

    // ---- validator -----------------------------------------------------------------------------

    public sealed record InvalidCase(string Name, string Role, string Effort, string Expected)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<Func<InvalidCase>> InvalidCases()
    {
        var roles = string.Join(", ", Enum.GetNames<AgentTaskRole>());
        yield return () => new("unknown-role", "Reviewer", "high",
            $"Delegation:ClaudeEffortByRole has unknown role 'Reviewer'. Use {roles}.");
        yield return () => new("undefined-numeric-role", "999", "high",
            $"Delegation:ClaudeEffortByRole has unknown role '999'. Use {roles}.");
        yield return () => new("unknown-value", "Code", "ultra",
            $"Delegation:ClaudeEffortByRole:Code is 'ultra', {NotAnEffort}");
        yield return () => new("empty-value", "Review", "",
            $"Delegation:ClaudeEffortByRole:Review is '', {NotAnEffort}");
    }

    [Test]
    [MethodDataSource(nameof(InvalidCases))]
    public void An_unknown_effort_or_role_fails_validation_naming_the_setting(InvalidCase c)
    {
        var settings = new DelegationSettings();
        settings.ClaudeEffortByRole[c.Role] = c.Effort;

        var result = new DelegationSettingsValidator().Validate(null, settings);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldNotBeNull().ShouldBe(new[] { c.Expected });
    }

    [Test]
    public void the_shipped_default_is_empty_and_valid_maps_validate()
    {
        new DelegationSettings().ClaudeEffortByRole.ShouldBeEmpty("the default is byte-identical to the tier table");

        var settings = new DelegationSettings();
        settings.ClaudeEffortByRole["Code"] = "xhigh";
        settings.ClaudeEffortByRole["review"] = "HIGH";
        settings.ClaudeEffortByRole["Mutation"] = " max ";

        new DelegationSettingsValidator().Validate(null, settings).Succeeded.ShouldBeTrue();
    }

    [Test]
    public void binding_the_overlay_shape_validates_and_an_unknown_value_fails_the_options_read()
    {
        var good = Bind(new Dictionary<string, string?>
        {
            ["Delegation:ClaudeEffortByRole:Code"] = "xhigh",
            ["Delegation:ClaudeEffortByRole:Debug"] = "xhigh",
        });

        good.Value.ClaudeEffortByRole.Count.ShouldBe(2);
        good.Value.ClaudeEffortByRole["code"].ShouldBe("xhigh", "keys stay case-insensitive after binding");
        ClaudeLaunchArgs.Effort(AgentModelLevel.High, AgentTaskRole.Code, good.Value.ClaudeEffortByRole).ShouldBe("xhigh");
        ClaudeLaunchArgs.Effort(AgentModelLevel.High, AgentTaskRole.Review, good.Value.ClaudeEffortByRole).ShouldBe("high");

        var bad = Bind(new Dictionary<string, string?> { ["Delegation:ClaudeEffortByRole:Code"] = "ultra" });

        Should.Throw<OptionsValidationException>(() => _ = bad.Value).Failures
            .ShouldBe(new[] { $"Delegation:ClaudeEffortByRole:Code is 'ultra', {NotAnEffort}" });
    }

    private static IOptions<DelegationSettings> Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<DelegationSettings>, DelegationSettingsValidator>();
        services.AddOptions<DelegationSettings>().Bind(configuration.GetSection("Delegation"));
        return services.BuildServiceProvider().GetRequiredService<IOptions<DelegationSettings>>();
    }
}
