using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Review;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// The <c>claude-cli</c> decision model against the real <c>claude</c> CLI on this machine's Claude Code
/// sign-in: one haiku call. It spends subscription quota, so it runs only with
/// <c>ANTIPHON_LIVE_CLAUDE_CLI=1</c> and is skipped otherwise.
/// </summary>
[Category("Integration")]
[Category("LiveClaudeCli")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeCliLiveTests
{
    public const string Variable = "ANTIPHON_LIVE_CLAUDE_CLI";

    [Test]
    public async Task Haiku_answers_a_noul_a_choice_and_a_score_question_through_the_real_cli()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            Skip.Test($"Set {Variable}=1 to run claude -p (haiku) once on this machine's Claude Code sign-in.");
            return;
        }

        var settings = Options.Create(new ReviewDecisionSettings
        {
            Providers = { ["haiku-cli"] = new ReviewDecisionProvider { Dialect = "claude-cli", Model = "haiku", TimeoutSeconds = 180 } },
        });
        var cli = new ClaudeCliProcess(Options.Create(new ReviewAgentSettings()), NullLogger<ClaudeCliProcess>.Instance);
        var request = new DecisionRequest(
            "PR #12 renames the local variable `cnt` to `count` inside one private method of src/Counter.cs. Nothing else changes: " +
            "no other file, no behaviour, no stored data.",
            [
                DecisionQuestion.Noul("changes_stored_data", "Does this change alter data stored in a database or on disk?"),
                new DecisionQuestion("change_kind", DecisionQuestionKind.Choice, "What kind of change is this?",
                    [new("feature"), new("bugfix"), new("refactor")]),
                new DecisionQuestion("blast_radius", DecisionQuestionKind.Score, "How much of the system could this change affect?",
                    [new("One place"), new("A few components"), new("System-wide")]),
            ]);

        var result = await new ClaudeCliDecisionModel(cli, settings)
            .DecideAsync(request, new DecisionOptions("haiku-cli", "haiku", TimeSpan.FromMinutes(3)), CancellationToken.None);

        result.ServedBy.Transport.ShouldBe("OneShot");
        result.ServedBy.ResolvedModel.ShouldContain("haiku");
        result.CostBasis.ShouldBe("api-equivalent");
        result.Usage.OutputTokens.ShouldBeGreaterThan(0);
        result.Answers.Values.ShouldAllBe(a => !a.Refused);
        result.Answers["changes_stored_data"].Value.ShouldBe("no");
        result.Answers["changes_stored_data"].Probabilities!.Keys.ShouldBe(["yes", "no"], ignoreOrder: true);
        result.Answers["change_kind"].Value.ShouldBe("refactor");
        result.Answers["blast_radius"].Value.ShouldBe("One place");
        Console.WriteLine($"claude-cli live: {result.ServedBy.ResolvedModel}, {result.Usage.InputTokens} in, {result.Usage.OutputTokens} out, " +
            $"${result.CostUsd} ({result.CostBasis}); " +
            string.Join(", ", result.Answers.Values.Select(a => $"{a.Name}={a.Value} ({a.Confidence:0.00})")));
    }
}
