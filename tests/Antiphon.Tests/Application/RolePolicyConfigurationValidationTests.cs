using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1234 (1): <see cref="DelegationSettingsValidator"/> rejects a role-policy kind delegation
/// cannot launch and a model level that is not defined, naming the role, the field and the value.
/// Expected messages are literals.
/// </summary>
[Category("Unit")]
public sealed class RolePolicyConfigurationValidationTests
{
    private static readonly AgentKind UndefinedKind = (AgentKind)999;
    private static readonly AgentModelLevel UndefinedLevel = (AgentModelLevel)999;

    public sealed record InvalidCase(string Name, Action<DelegationSettings> Configure, string Expected)
    {
        public override string ToString() => Name;
    }

    public sealed record ValidCase(string Name, Action<DelegationSettings> Configure)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<Func<InvalidCase>> InvalidCases()
    {
        yield return () => new("Code-Kind-Raw", s => s.RolePolicy["Code"].Kind = AgentKind.Raw,
            "Delegation:RolePolicy:Code:Kind is Raw, which delegation cannot launch. Use ClaudeCode, Grok or Codex, or leave it unset.");
        yield return () => new("Code-Kind-OpenCode", s => s.RolePolicy["Code"].Kind = AgentKind.OpenCode,
            "Delegation:RolePolicy:Code:Kind is OpenCode, which delegation cannot launch. Use ClaudeCode, Grok or Codex, or leave it unset.");
        yield return () => new("Docs-Kind-999", s => s.RolePolicy["Docs"].Kind = UndefinedKind,
            "Delegation:RolePolicy:Docs:Kind is 999, which delegation cannot launch. Use ClaudeCode, Grok or Codex, or leave it unset.");
        yield return () => new("Review-Level-999", s => s.RolePolicy["Review"].Level = UndefinedLevel,
            "Delegation:RolePolicy:Review:Level is 999, which is not a model level. Use Frontier, High, Medium or Low.");
        yield return () => new("Mutation-Level-minus-1", s => s.RolePolicy["Mutation"].Level = (AgentModelLevel)(-1),
            "Delegation:RolePolicy:Mutation:Level is -1, which is not a model level. Use Frontier, High, Medium or Low.");
        yield return () => new("Test-EscalateTo-999", s => s.RolePolicy["Test"].EscalateTo = UndefinedLevel,
            "Delegation:RolePolicy:Test:EscalateTo is 999, which is not a model level. Use Frontier, High, Medium or Low.");
        yield return () => new("Investigate-EscalateTo-4", s => s.RolePolicy["Investigate"].EscalateTo = (AgentModelLevel)4,
            "Delegation:RolePolicy:Investigate:EscalateTo is 4, which is not a model level. Use Frontier, High, Medium or Low.");
        yield return () => new("DefaultLevel-999", s => s.DefaultLevel = UndefinedLevel,
            "Delegation:DefaultLevel is 999, which is not a model level. Use Frontier, High, Medium or Low.");
    }

    public static IEnumerable<Func<ValidCase>> ValidCases()
    {
        yield return () => new("shipped", _ => { });
        yield return () => new("Kind-null", s => s.RolePolicy["Code"].Kind = null);
        yield return () => new("Kind-ClaudeCode", s => s.RolePolicy["Code"].Kind = AgentKind.ClaudeCode);
        yield return () => new("Kind-Grok", s => s.RolePolicy["Code"].Kind = AgentKind.Grok);
        yield return () => new("Kind-Codex", s => s.RolePolicy["Code"].Kind = AgentKind.Codex);
        yield return () => new("Level-Frontier", s => s.RolePolicy["Docs"].Level = AgentModelLevel.Frontier);
        yield return () => new("Level-High", s => s.RolePolicy["Docs"].Level = AgentModelLevel.High);
        yield return () => new("Level-Medium", s => s.RolePolicy["Docs"].Level = AgentModelLevel.Medium);
        yield return () => new("Level-Low", s => s.RolePolicy["Docs"].Level = AgentModelLevel.Low);
        yield return () => new("EscalateTo-null", s => s.RolePolicy["Test"].EscalateTo = null);
        yield return () => new("EscalateTo-Frontier", s => s.RolePolicy["Test"].EscalateTo = AgentModelLevel.Frontier);
        yield return () => new("DefaultLevel-Low", s => s.DefaultLevel = AgentModelLevel.Low);
    }

    [Test]
    [MethodDataSource(nameof(InvalidCases))]
    public void An_unlaunchable_kind_or_undefined_level_fails_validation_naming_role_and_value(InvalidCase c)
    {
        var settings = new DelegationSettings();
        c.Configure(settings);

        var result = new DelegationSettingsValidator().Validate(null, settings);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldNotBeNull().ShouldBe(new[] { c.Expected });
    }

    [Test]
    [MethodDataSource(nameof(ValidCases))]
    public void A_launchable_kind_and_defined_levels_validate(ValidCase c)
    {
        var settings = new DelegationSettings();
        c.Configure(settings);

        new DelegationSettingsValidator().Validate(null, settings).Succeeded.ShouldBeTrue();
    }

    [Test]
    public void Binding_unlaunchable_or_undefined_configuration_fails_the_options_read()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Delegation:RolePolicy:Docs:Kind"] = "OpenCode",
            ["Delegation:RolePolicy:Docs:Level"] = "999",
            ["Delegation:RolePolicy:Plan:EscalateTo"] = "999",
            ["Delegation:RolePolicy:Commit:Kind"] = "Raw",
        });

        var ex = Should.Throw<OptionsValidationException>(() => _ = options.Value);

        ex.Failures.OrderBy(f => f, StringComparer.Ordinal).ShouldBe(new[]
        {
            "Delegation:RolePolicy:Commit:Kind is Raw, which delegation cannot launch. Use ClaudeCode, Grok or Codex, or leave it unset.",
            "Delegation:RolePolicy:Docs:Kind is OpenCode, which delegation cannot launch. Use ClaudeCode, Grok or Codex, or leave it unset.",
            "Delegation:RolePolicy:Docs:Level is 999, which is not a model level. Use Frontier, High, Medium or Low.",
            "Delegation:RolePolicy:Plan:EscalateTo is 999, which is not a model level. Use Frontier, High, Medium or Low.",
        });
    }

    [Test]
    public void Binding_named_valid_values_and_absent_fields_validates()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Delegation:RolePolicy:Docs:Kind"] = "Grok",
            ["Delegation:RolePolicy:Docs:Level"] = "Low",
            ["Delegation:RolePolicy:Plan:EscalateTo"] = "Frontier",
            ["Delegation:RolePolicy:Investigate:Level"] = "Medium",
        });

        var value = options.Value;

        value.RolePolicy["Docs"].Kind.ShouldBe(AgentKind.Grok);
        value.RolePolicy["Docs"].Level.ShouldBe(AgentModelLevel.Low);
        value.RolePolicy["Plan"].EscalateTo.ShouldBe(AgentModelLevel.Frontier);
        value.RolePolicy["Code"].Kind.ShouldBeNull();
        value.RolePolicy["Code"].EscalateTo.ShouldBeNull();
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
