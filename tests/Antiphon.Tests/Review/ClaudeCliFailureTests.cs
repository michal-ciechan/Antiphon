using Antiphon.Server.Api;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Review;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
    [Arguments("Decisions")]
    [Arguments("Completions")]
    public async Task Legacy_OneShot_HTTP_entry_warns_at_startup_and_still_serves_by_dialect(string section)
    {
        var prefix = $"Review:{section}";
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{prefix}:Providers:legacy-http:Dialect"] = "backup",
            [$"{prefix}:Providers:legacy-http:Model"] = "fake",
            [$"{prefix}:Providers:legacy-http:ApiKey"] = "fixture-key-value",
            [$"{prefix}:Chains:{ChainName}:0:Provider"] = "legacy-http",
            [$"{prefix}:Chains:{ChainName}:0:Transport"] = "OneShot",
        });
        var logs = new ReviewTestLog();
        builder.Logging.AddProvider(logs);
        builder.Services.AddReviewChainOptions(builder.Configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IDecisionModel, BackupModel>();
        builder.Services.AddSingleton<ICompletionModel, BackupCompletionModel>();
        builder.Services.AddSingleton<DecisionChain>();
        builder.Services.AddSingleton<CompletionChain>();

        using var host = builder.Build();
        await host.StartAsync(CancellationToken.None);
        // Assert before resolving either chain. The warning must be emitted on host startup.
        logs.Warnings.ShouldContain(message => message.Contains(prefix)
            && message.Contains(ChainName) && message.Contains("entry 0")
            && message.Contains("legacy-http") && message.Contains("backup"));
        using var listing = new StringWriter();
        ReviewChainRegistration.WriteEffectiveReviewChains(host.Services, listing);
        listing.ToString().ShouldContain($"{prefix} chain '{ChainName}' entry 0 provider 'legacy-http' transport 'OneShot' dialect 'backup'");
        listing.ToString().ShouldNotContain("fixture-key-value");
        if (section == "Decisions")
        {
            var result = await host.Services.GetRequiredService<DecisionChain>().DecideAsync(ChainName, Decision, CancellationToken.None);
            result.Answers["applies"].ServedBy!.Provider.ShouldBe("legacy-http");
        }
        else
        {
            var result = await host.Services.GetRequiredService<CompletionChain>()
                .CompleteAsync(ChainName, new CompletionRequest("Instructions", [], "text"), CancellationToken.None);
            result.Text.ShouldBe("backup answered");
        }
        await host.StopAsync(CancellationToken.None);
    }

    [Test]
    [Arguments("Authorization: Bearer xyShort", "xyShort")]
    [Arguments("Authorization: Bearer sk-ant-synthetic-long-credential-only", "sk-ant-synthetic-long-credential-only")]
    [Arguments("Proxy-Authorization: Basic QWxhZGRpbjpvcGVuIHNlc2FtZQ==", "QWxhZGRpbjpvcGVuIHNlc2FtZQ==")]
    [Arguments("Cookie: sid=cook1e; tracking=track8", "cook1e")]
    [Arguments("Set-Cookie: sid=cook1e; tracking=track8", "track8")]
    [Arguments("""{"token":"fixture token value","password":"escaped\"quote"}""", "fixture token value")]
    [Arguments("""{"token":"fixture token value","password":"escaped\"quote"}""", "quote")]
    [Arguments("HTTP https://user:pass@host.example/result", "user:pass")]
    [Arguments("Account fixture-user@host.example", "fixture-user@host.example")]
    [Arguments("Account username: fixture-owner", "fixture-owner")]
    [Arguments("CLI raw owner reference fixture-unlabelled-owner", "fixture-unlabelled-owner")]
    [Arguments("key='padded synthetic' password=fixturePass", "padded synthetic")]
    [Arguments("Error ghp_short gho_short github_pat_short xoxb-short sk-short", "short")]
    [Arguments("Service rejected e30.e30.fakeSignature", "fakeSignature")]
    [Arguments("Open /home/fixture-owner/.config", "fixture-owner")]
    [Arguments(@"Open C:\Users\fixture-owner\config.json", "fixture-owner")]
    public void Public_stderr_removes_credentials_and_account_identifiers(string stderr, string sensitive)
    {
        var failure = Should.Throw<ClaudeCliException>(() => ClaudeCliProcess.Parse(7, "", "CLI backend overloaded\n" + stderr));
        failure.Message.ShouldContain("CLI backend overloaded");
        failure.Message.ShouldContain("exit 7");
        failure.Message.Contains(sensitive, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Test]
    public void Public_stderr_removes_the_prefix_of_a_token_crossing_the_excerpt_boundary()
    {
        const string prefix = "boundaryPrefix";
        var stderr = string.Concat(Enumerable.Repeat("detail ", 67)) + prefix + new string('a', 80) + "\nAPI Error: 529";
        var failure = Should.Throw<ClaudeCliException>(() => ClaudeCliProcess.Parse(7, "", stderr));
        failure.Message.Contains(prefix, StringComparison.Ordinal).ShouldBeFalse();
        failure.Message.ShouldContain("exit 7");
        failure.Message.ShouldContain("CLI backend overloaded");
    }

    [Test]
    public async Task Missing_executable_rejects_the_chain_and_names_the_command()
    {
        var settings = new ReviewAgentSettings();
        settings.Cli.Command = Path.Combine(Path.GetTempPath(), $"missing-claude-{Guid.NewGuid():N}", "claude-for-test.exe");
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
        var stderr = $"CLI backend overloaded\r\n{token}\u001b[0m x-api-key: {key}\n"
            + string.Concat(Enumerable.Repeat("Additional diagnostic text. ", 30)) + "TAIL";
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

    [Test]
    [Arguments("OneShot", "litellm-systemone", false)]
    [Arguments("OneShot", null, false)]
    [Arguments("oneshot", "Claude-CLI", true)]
    [Arguments("Http", "litellm-systemone", true)]
    public void OneShot_requires_a_configured_Claude_CLI_provider(string transport, string? dialect, bool valid)
    {
        new ReviewDecisionChainEntry { Transport = transport }.HasValidOneShotDialect(dialect).ShouldBe(valid);
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

    private sealed class BackupCompletionModel : ICompletionModel
    {
        public bool Serves(string dialect) => dialect == "backup";

        public Task<CompletionResult> CompleteAsync(CompletionRequest request, CompletionOptions options, CancellationToken ct) =>
            Task.FromResult(new CompletionResult("backup answered", new DecisionUsage(1, 2), 0.01m, "actual",
                new DecisionServedBy(options.ProviderId, "Http", "fake")));
    }
}
