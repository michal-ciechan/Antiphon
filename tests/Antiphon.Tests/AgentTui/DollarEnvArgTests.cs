using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

/// <summary>
/// CARD-0345 — whole-token <c>$env:NAME</c> / <c>${env:NAME}</c> expansion for TUI profile args.
/// Matcher is a copy of <c>HerdrLaunchScript.TryResolveEnvToken</c> (CARD-0341); do not share a type.
/// </summary>
[Category("Unit")]
public sealed class DollarEnvArgTests
{
    private static readonly Dictionary<string, string> ProjectEnv = new()
    {
        ["X_LLM_PROJECT"] = "PredictionMarkets",
    };

    [Test]
    public void pin_bare_token_expands_to_merged_env_value()
        => DollarEnvArg.Expand("$env:X_LLM_PROJECT", ProjectEnv).ShouldBe("PredictionMarkets");

    [Test]
    public void braced_token_expands()
        => DollarEnvArg.Expand("${env:X_LLM_PROJECT}", ProjectEnv).ShouldBe("PredictionMarkets");

    [Test]
    public void env_prefix_is_case_insensitive()
        => DollarEnvArg.Expand("$ENV:X_LLM_PROJECT", ProjectEnv).ShouldBe("PredictionMarkets");

    [Test]
    public void name_lookup_falls_back_to_ordinal_ignore_case()
        => DollarEnvArg.Expand("$env:x_llm_project", ProjectEnv).ShouldBe("PredictionMarkets");

    [Test]
    public void unknown_name_stays_literal()
        => DollarEnvArg.Expand("$env:MISSING", ProjectEnv).ShouldBe("$env:MISSING");

    [Test]
    public void substring_token_is_not_expanded()
        => DollarEnvArg.Expand("--project=$env:X_LLM_PROJECT", ProjectEnv)
            .ShouldBe("--project=$env:X_LLM_PROJECT");

    [Test]
    public void suffix_after_token_is_not_expanded()
        => DollarEnvArg.Expand("$env:X_LLM_PROJECT/sub", ProjectEnv)
            .ShouldBe("$env:X_LLM_PROJECT/sub");

    [Test]
    [Arguments("$env:")]
    [Arguments("${env:}")]
    public void empty_name_stays_literal(string argument)
        => DollarEnvArg.Expand(argument, ProjectEnv).ShouldBe(argument);

    [Test]
    public void present_empty_value_becomes_empty_string()
    {
        var env = new Dictionary<string, string> { ["X_LLM_PROJECT"] = "" };
        DollarEnvArg.Expand("$env:X_LLM_PROJECT", env).ShouldBe("");
    }

    [Test]
    public void non_token_is_unchanged()
        => DollarEnvArg.Expand("--project", ProjectEnv).ShouldBe("--project");
}
