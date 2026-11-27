using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Review;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ClaudeCliFailureTests
{
    private const string ChainName = "decisions-default";
    private static readonly DecisionRequest Decision = new("State", [DecisionQuestion.Noul("applies", "Does it apply?")]);

    [Test]
    public async Task Missing_executable_rejects_the_chain_and_names_the_command()
    {
        var settings = new ReviewAgentSettings();
        settings.Cli.Command = Path.Combine(Path.GetTempPath(), $"missing-claude-{Guid.NewGuid():N}.exe");
        var cli = new ClaudeCliProcess(Options.Create(settings), NullLogger<ClaudeCliProcess>.Instance);

        await RejectsAsync(cli, Path.GetFileName(settings.Cli.Command), "could not be started");
    }

    [Test]
    [Arguments("""{"type":"result","is_error":true,"result":"Not logged in · Please run /login","api_error_status":null}""", "")]
    [Arguments("", "Error: Not logged in. Please run /login")]
    [Arguments("""{"type":"result","is_error":true,"result":"Permission denied","api_error_status":403}""", "")]
    public async Task Authentication_failure_rejects_the_chain_and_names_the_sign_in_fix(string stdout, string stderr)
    {
        await RejectsAsync(FromOutput(1, stdout, stderr), "sign in", "exit 1");
    }

    [Test]
    public async Task Other_exit_falls_through_and_the_exhausted_chain_shows_its_exit_code_and_stderr()
    {
        var cli = FromOutput(7, "", "CLI backend overloaded");
        await FallsThroughAsync(cli, "unavailable");

        var failure = await Should.ThrowAsync<DecisionChainException>(() =>
            Chain(cli, withFallback: false).DecideAsync(ChainName, Decision, CancellationToken.None));
        failure.Message.ShouldContain("unavailable");
        failure.Message.ShouldContain("exit 7");
        failure.Message.ShouldContain("CLI backend overloaded");
    }

    [Test]
    public async Task Timeout_falls_through_and_the_exhausted_chain_shows_the_timeout()
    {
        var cli = new FakeCli(() => throw new ClaudeCliException("claude -p did not finish within 1s.", timedOut: true));
        await FallsThroughAsync(cli, "timeout");

        var failure = await Should.ThrowAsync<DecisionChainException>(() =>
            Chain(cli, withFallback: false).DecideAsync(ChainName, Decision, CancellationToken.None));
        failure.Message.ShouldContain("did not finish within 1s");
    }

    [Test]
    public async Task Stderr_in_the_exhausted_chain_is_redacted_and_bounded()
    {
        // All credentials in the fixture are invented.
        var token = "sk-ant-" + new string('z', 530);
        const string key = "fixtureValueOnly";
        var stderr = $"CLI backend overloaded\r\n{token}\u001b[0m x-api-key: {key}\n" + new string('D', 700) + "TAIL";
        var cliFailure = Should.Throw<ClaudeCliException>(() => ClaudeCliProcess.Parse(7, "", stderr));
        IsSafeExcerpt(cliFailure.Message, key);

        var failure = await Should.ThrowAsync<DecisionChainException>(() =>
            Chain(FromOutput(7, "", stderr), withFallback: false).DecideAsync(ChainName, Decision, CancellationToken.None));
        IsSafeExcerpt(failure.Message, key);
    }

    [Test]
    public async Task Completion_chain_surfaces_authentication_and_other_exit_diagnostics()
    {
        var settings = Options.Create(new ReviewCompletionSettings
        {
            Providers = { ["cli"] = new ReviewCompletionProvider { Dialect = "claude-cli", Model = "haiku" } },
            Chains = { [ChainName] = [new() { Provider = "cli", Transport = "OneShot" }] },
        });
        var request = new CompletionRequest("Instructions", [], "text");
        var auth = await FailureAsync(FromOutput(1, "", "Not logged in. Please run /login"));
        auth.Kind.ShouldBe(DecisionFailureKind.Rejected);
        auth.Message.ShouldContain("sign in");

        var exit = await FailureAsync(FromOutput(7, "", "CLI backend overloaded"));
        exit.Calls.ShouldHaveSingleItem().Outcome.ShouldBe(nameof(DecisionFailureKind.Unavailable));
        exit.Message.ShouldContain("exit 7");
        exit.Message.ShouldContain("CLI backend overloaded");

        Task<DecisionChainException> FailureAsync(IClaudeCli cli) => Should.ThrowAsync<DecisionChainException>(() =>
            new CompletionChain([new ClaudeCliCompletionModel(cli, settings)], settings, NullLogger<CompletionChain>.Instance)
                .CompleteAsync(ChainName, request, CancellationToken.None));
    }

    private static async Task RejectsAsync(IClaudeCli cli, string reason, string detail)
    {
        var failure = await Should.ThrowAsync<DecisionChainException>(() => Chain(cli).DecideAsync(ChainName, Decision, CancellationToken.None));
        failure.Kind.ShouldBe(DecisionFailureKind.Rejected);
        failure.Calls.ShouldHaveSingleItem().Outcome.ShouldBe("rejected");
        failure.Message.ShouldContain(reason);
        failure.Message.ShouldContain(detail);
    }

    private static async Task FallsThroughAsync(IClaudeCli cli, string outcome)
    {
        var result = await Chain(cli).DecideAsync(ChainName, Decision, CancellationToken.None);
        result.Calls.Select(c => (c.Provider, c.Outcome)).ShouldBe([("cli", outcome), ("backup", "ok")]);
        var failed = result.Calls[0];
        failed.ResolvedModel.ShouldBeNull();
        (failed.InputTokens, failed.OutputTokens, failed.CostUsd).ShouldBe((0L, 0L, 0m));
        result.Answers["applies"].ServedBy!.Provider.ShouldBe("backup");
    }

    private static void IsSafeExcerpt(string message, string key)
    {
        message.ShouldContain("CLI backend overloaded");
        message.Contains("zzzz", StringComparison.Ordinal).ShouldBeFalse();
        message.Contains(key, StringComparison.Ordinal).ShouldBeFalse();
        message.Contains('\u001b').ShouldBeFalse();
        message.Contains('\n').ShouldBeFalse();
        message.ShouldNotContain("TAIL");
        message.Length.ShouldBeLessThan(650);
    }

    private static DecisionChain Chain(IClaudeCli cli, bool withFallback = true)
    {
        var settings = Options.Create(new ReviewDecisionSettings
        {
            Providers =
            {
                ["cli"] = new ReviewDecisionProvider { Dialect = "claude-cli", Model = "haiku" },
                ["backup"] = new ReviewDecisionProvider { Dialect = "backup", Model = "fake" },
            },
            Chains = { [ChainName] = [new() { Provider = "cli", Transport = "OneShot" }] },
        });
        if (withFallback)
            settings.Value.Chains[ChainName].Add(new ReviewDecisionChainEntry { Provider = "backup" });
        return new DecisionChain([new ClaudeCliDecisionModel(cli, settings), new BackupModel()], settings,
            TimeProvider.System, NullLogger<DecisionChain>.Instance);
    }

    private static IClaudeCli FromOutput(int exitCode, string stdout, string stderr) =>
        new FakeCli(() => ClaudeCliProcess.Parse(exitCode, stdout, stderr));

    private sealed class FakeCli(Func<ClaudeCliResult> run) : IClaudeCli
    {
        public Task<ClaudeCliResult> RunAsync(ClaudeCliRequest request, CancellationToken ct) => Task.FromResult(run());
    }

    private sealed class BackupModel : IDecisionModel
    {
        public bool Serves(string dialect) => dialect == "backup";

        public Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken ct)
        {
            var answers = request.Questions.ToDictionary(q => q.Name, q => new DecisionAnswer(q.Name, q.Kind, "yes", 1, null, false));
            return Task.FromResult(new DecisionResult(answers, new DecisionUsage(1, 2), 0.01m, "actual",
                new DecisionServedBy(options.ProviderId, "Http", "fake")));
        }
    }
}
