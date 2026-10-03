using System.Reflection;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class CodexCliAdmissionTests
{
    [Test]
    public void C959_Ladder_and_exact_models_share_floor()
    {
        var metadata = typeof(ModelLevelAliases).GetMethod("MinimumCodexCliVersion", BindingFlags.Public | BindingFlags.Static);
        string? Floor(AgentKind kind, string? model) => metadata?.Invoke(null, [kind, model])?.ToString();
        foreach (var (level, model, floor) in new[]
        {
            (AgentModelLevel.High, "gpt-6.1-sol", "0.159.1"),
            (AgentModelLevel.Medium, "gpt-6.1-sol", "0.159.1"),
            ((AgentModelLevel)999, "gpt-6.1-sol", "0.159.1"),
            (AgentModelLevel.Frontier, "gpt-6-astra", (string?)null),
            (AgentModelLevel.Low, "gpt-5.6-luna", (string?)null),
        })
        {
            ModelLevelAliases.ForCodex(level).ShouldBe(model, "C959-v14-existing-alias " + level);
            Floor(AgentKind.Codex, model).ShouldBe(floor, "C959-v14-floor " + level);
        }
        Floor(AgentKind.Codex, "GPT-6.1-SOL").ShouldBe("0.159.1", "C959-v14-exact-canonical");
        foreach (var model in new[] { "gpt-6-sol", "gpt-5.6-terra", "gpt-5.6-sol", "gpt-6-astra",
                     "gpt-5.6-luna", "wrapper-owned", "", null })
            Floor(AgentKind.Codex, model).ShouldBeNull("C959-v14-unrelated " + model);
        foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Grok, AgentKind.Raw })
            Floor(kind, "gpt-6.1-sol").ShouldBeNull("C959-v14-other-kind " + kind);
    }
}
