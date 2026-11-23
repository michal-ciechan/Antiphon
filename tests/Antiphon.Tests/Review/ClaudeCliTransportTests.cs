using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Review;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// The one-shot <c>claude -p</c> transport (design 7.2) over a fake <see cref="IClaudeCli"/>: decision
/// answers mapped as Jev's are, refusals per question, the CLI's JSON result, and the background reviewer
/// runner going Pending then Completed with its usage. No process starts.
/// </summary>
[Category("Unit")]
public class ClaudeCliTransportTests
{
    private static readonly DecisionRequest Decision = new("PR #812: release the lock after the save.",
    [
        DecisionQuestion.Noul("persistent_data", "Does this change stored data?"),
        new DecisionQuestion("change_kind", DecisionQuestionKind.Choice, "What kind of change is this?",
            [new("feature"), new("bugfix", "Fixes a defect")]),
        new DecisionQuestion("reach", DecisionQuestionKind.Score, "How many components does this affect?",
            [new("One place"), new("A few"), new("System-wide")]),
    ]);

    private const string Findings = """
        @abf 1 review-findings/1.0
        === checks
        SEED-0001 pass
        === end
        """;

    [Test]
    public async Task Decision_answers_map_as_jev_maps_them_through_a_chain_whose_provider_has_no_key()
    {
        var cli = new FakeClaudeCli(_ => Task.FromResult(Answer("""
            {"answers":[{"name":"persistent_data","value":"no","p":0.8},{"name":"change_kind","value":"bugfix","p":0.9},
                        {"name":"reach","value":"a few","p":0.7}]}
            """)));
        var settings = Options.Create(new ReviewDecisionSettings
        {
            Providers = { ["haiku-cli"] = new ReviewDecisionProvider { Dialect = "claude-cli", Model = "haiku", TimeoutSeconds = 120 } },
            Chains = { ["decisions-default"] = [new ReviewDecisionChainEntry { Provider = "haiku-cli", Transport = "OneShot" }] },
        });
        var chain = new DecisionChain([new ClaudeCliDecisionModel(cli, settings)], settings, TimeProvider.System, NullLogger<DecisionChain>.Instance);

        var result = await chain.DecideAsync("decisions-default", Decision, CancellationToken.None);

        var call = result.Calls.ShouldHaveSingleItem();
        call.Outcome.ShouldBe("ok");
        call.Transport.ShouldBe("OneShot");
        call.ResolvedModel.ShouldBe("claude-haiku-5-5");
        call.CostBasis.ShouldBe("api-equivalent");
        call.CostUsd.ShouldBe(0.0012m);
        call.InputTokens.ShouldBe(900);

        var noul = result.Answers["persistent_data"];
        noul.Value.ShouldBe("no");
        noul.Confidence!.Value.ShouldBe(0.8, 1e-9);
        noul.Probabilities!["yes"].ShouldBe(0.2, 1e-9);
        noul.Probabilities["no"].ShouldBe(0.8, 1e-9);
        var choice = result.Answers["change_kind"];
        choice.Value.ShouldBe("bugfix");
        choice.Confidence.ShouldBe(0.9);
        var score = result.Answers["reach"];
        score.Value.ShouldBe("A few");
        score.Confidence.ShouldBe(0.7);
        score.ServedBy.ShouldBe(new DecisionServedBy("haiku-cli", "OneShot", "claude-haiku-5-5"));

        var sent = cli.Requests.ShouldHaveSingleItem();
        sent.Model.ShouldBe("haiku");
        sent.Tools.ShouldBeEmpty();
        sent.Timeout.ShouldBe(TimeSpan.FromSeconds(120));
        sent.Prompt.ShouldContain("PR #812: release the lock after the save.");
        sent.Prompt.ShouldContain("name: persistent_data");
        sent.Prompt.ShouldContain("\"bugfix\": Fixes a defect");
        sent.Prompt.ShouldContain("\"System-wide\"");
        sent.Prompt.ShouldContain("""{"answers":[{"name":"<question name>","value":"<your answer>","p":0.0}]}""");
        cli.DirectoryExisted.ShouldBe([true]);
        Directory.Exists(sent.WorkingDirectory).ShouldBeFalse();
    }

    [Test]
    public async Task A_fenced_answer_is_read_and_a_missing_or_invalid_answer_is_refused_for_that_question_only()
    {
        var cli = new FakeClaudeCli(_ => Task.FromResult(Answer("""
            Here are the answers:
            ```json
            {"answers":[{"name":"persistent_data","value":"yes","p":0.9},{"name":"change_kind","value":"refactor","p":0.8}]}
            ```
            """)));
        var settings = Options.Create(new ReviewDecisionSettings
        {
            Providers = { ["haiku-cli"] = new ReviewDecisionProvider { Dialect = "claude-cli", Model = "haiku" } },
        });

        var result = await new ClaudeCliDecisionModel(cli, settings).DecideAsync(Decision, new DecisionOptions("haiku-cli", "haiku"),
            CancellationToken.None);

        result.Answers["persistent_data"].Value.ShouldBe("yes");
        result.Answers["persistent_data"].Probabilities!["yes"].ShouldBe(0.9);
        result.Answers["change_kind"].Refused.ShouldBeTrue("refactor is not an allowed value");
        result.Answers["reach"].Refused.ShouldBeTrue("no answer was given");
        result.Answers["reach"].ServedBy!.Transport.ShouldBe("OneShot");

        var none = Should.Throw<DecisionProviderException>(() =>
            ClaudeCliDecisionModel.ParseAnswers(Decision, "I cannot answer that.", result.ServedBy));
        none.Kind.ShouldBe(DecisionFailureKind.SchemaFailure);
    }

    [Test]
    public void The_cli_result_is_parsed_and_an_error_carries_the_cli_text()
    {
        const string ok = """
            {"type":"result","subtype":"success","is_error":false,"result":"pong","total_cost_usd":0.0006598900000000001,
             "usage":{"input_tokens":2,"cache_creation_input_tokens":3256,"cache_read_input_tokens":649,"output_tokens":4},
             "modelUsage":{"claude-haiku-5-5":{"inputTokens":2,"outputTokens":4,"costUSD":0.00065989}}}
            """;
        var parsed = ClaudeCliProcess.Parse(0, ok.ReplaceLineEndings(" "), "");
        parsed.ShouldBe(new ClaudeCliResult("pong", "claude-haiku-5-5", 3907, 4, 0.0006598900000000001m));

        var failed = Should.Throw<ClaudeCliException>(() => ClaudeCliProcess.Parse(1,
            """{"type":"result","subtype":"success","is_error":true,"result":"Not logged in · Please run /login","api_error_status":null}""", ""));
        failed.Message.ShouldContain("Not logged in");
        failed.Message.ShouldContain("exit 1");
        Should.Throw<ClaudeCliException>(() => ClaudeCliProcess.Parse(2, "", "error: unknown option '--bogus'"))
            .Message.ShouldContain("unknown option '--bogus'");

        ClaudeCliProcess.Arguments(new ClaudeCliRequest("p", "haiku", [], ".", TimeSpan.FromMinutes(1)))
            .ShouldBe(["-p", "--model", "haiku", "--output-format", "json", "--tools", "", "--setting-sources", "", "--no-session-persistence",
                "--strict-mcp-config"]);
        ClaudeCliProcess.Arguments(new ClaudeCliRequest("p", "opus", CliReviewAgentRunner.ReviewerTools, ".", TimeSpan.FromMinutes(1)))
            .ShouldBe(["-p", "--model", "opus", "--output-format", "json", "--tools", "Read,Grep,Glob", "--allowedTools", "Read,Grep,Glob",
                "--setting-sources", "", "--no-session-persistence", "--strict-mcp-config"]);
    }

    [Test]
    public async Task A_reviewer_runs_in_the_background_pending_then_completed_with_its_usage()
    {
        var root = NewRoot();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cli = new FakeClaudeCli(async _ =>
        {
            await release.Task;
            return new ClaudeCliResult(Findings, "claude-opus-5-5", 12_000, 300, 0.42m);
        });
        var checkout = new FakeCheckout();
        using var runner = new CliReviewAgentRunner(cli, checkout, Options.Create(new ReviewAgentSettings()), TimeProvider.System,
            NullLogger<CliReviewAgentRunner>.Instance, root);
        var pack = "## Diff\n" + new string('x', 25_000);
        var request = Request(pack, tier: "deep");

        var started = await runner.RunAsync(request, CancellationToken.None);
        started.Status.ShouldBe(ReviewAgentStatus.Pending);
        started.Handle.ShouldStartWith(CliReviewAgentRunner.HandlePrefix);
        (await runner.RunAsync(request with { Handle = started.Handle }, CancellationToken.None)).Status.ShouldBe(ReviewAgentStatus.Pending);

        release.SetResult();
        await runner.Completion(started.Handle!)!.WaitAsync(TimeSpan.FromSeconds(30));
        var done = await runner.RunAsync(request with { Handle = started.Handle }, CancellationToken.None);

        done.Status.ShouldBe(ReviewAgentStatus.Completed);
        done.Text.ShouldBe(Findings);
        done.Usage.ShouldBe(new ReviewAgentUsage("claude-cli", "claude-opus-5-5", ReviewTransport.OneShot, 12_000, 300, 0.42m, "api-equivalent"));
        var sent = cli.Requests.ShouldHaveSingleItem();
        sent.Model.ShouldBe("opus");
        sent.Tools.ShouldBe(["Read", "Grep", "Glob"]);
        sent.Timeout.ShouldBe(TimeSpan.FromMinutes(90));
        sent.Prompt.ShouldContain(pack, customMessage: "the pack is inline even past InlinePackMaxChars");
        sent.Prompt.ShouldNotContain("/api/review-runs/");
        sent.Prompt.ShouldContain("No checkout of the repository is available");
        sent.WorkingDirectory.ShouldStartWith(root);
        cli.DirectoryExisted.ShouldBe([true]);
        Directory.Exists(sent.WorkingDirectory).ShouldBeFalse();
        checkout.Calls.ShouldBeEmpty();
        Directory.Delete(root, recursive: true);
    }

    /// <summary>
    /// Regression (found live, 2026-11-23): the runner was the scoped IReviewAgentRunner through a
    /// factory, so each executor scope disposed the singleton when it ended and cancelled the reviewer
    /// it had just started ("stopped because the server is shutting down").
    /// </summary>
    [Test]
    public async Task A_reviewer_outlives_the_scope_that_started_it()
    {
        var root = NewRoot();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cli = new FakeClaudeCli(async _ =>
        {
            await release.Task;
            return new ClaudeCliResult(Findings, "claude-sonnet-5-5", 1000, 50, 0.05m);
        });
        var settings = new ReviewAgentSettings { Transport = ReviewAgentTransports.Cli };
        var services = new ServiceCollection();
        services.AddSingleton<IClaudeCli>(cli);
        services.AddSingleton<IReviewCheckout>(new FakeCheckout());
        services.AddSingleton(Options.Create(settings));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILogger<CliReviewAgentRunner>>(NullLogger<CliReviewAgentRunner>.Instance);
        services.AddSingleton(sp => new CliReviewAgentRunner(sp.GetRequiredService<IClaudeCli>(), sp.GetRequiredService<IReviewCheckout>(),
            sp.GetRequiredService<IOptions<ReviewAgentSettings>>(), TimeProvider.System, NullLogger<CliReviewAgentRunner>.Instance, root));
        services.AddScoped<IReviewAgentRunner, SelectedReviewAgentRunner>();
        await using var provider = services.BuildServiceProvider();
        var request = Request("## Diff\n+ x");

        string handle;
        await using (var scope = provider.CreateAsyncScope())
            handle = (await scope.ServiceProvider.GetRequiredService<IReviewAgentRunner>().RunAsync(request, CancellationToken.None)).Handle!;

        release.SetResult();
        await provider.GetRequiredService<CliReviewAgentRunner>().Completion(handle)!.WaitAsync(TimeSpan.FromSeconds(30));
        await using (var scope = provider.CreateAsyncScope())
        {
            var done = await scope.ServiceProvider.GetRequiredService<IReviewAgentRunner>().RunAsync(request with { Handle = handle }, CancellationToken.None);
            done.Status.ShouldBe(ReviewAgentStatus.Completed, "ending the starting scope must not stop the reviewer");
            done.Text.ShouldBe(Findings);
        }

        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task A_mapped_repo_is_checked_out_at_the_head_and_removed_afterwards_and_a_failure_fails_the_poll()
    {
        var root = NewRoot();
        var cli = new FakeClaudeCli(request => request.Model == "sonnet"
            ? Task.FromResult(new ClaudeCliResult(Findings, "claude-sonnet-5-5", 10, 5, 0.01m))
            : throw new ClaudeCliException("claude -p failed (exit 1, error_during_execution): API Error: 529 overloaded"));
        var checkout = new FakeCheckout();
        var settings = new ReviewAgentSettings { Repos = [new ReviewAgentRepo { Repo = "acme/widgets", LocalPath = @"C:\repos\widgets" }] };
        using var runner = new CliReviewAgentRunner(cli, checkout, Options.Create(settings), TimeProvider.System,
            NullLogger<CliReviewAgentRunner>.Instance, root);

        var request = Request("## Diff\n+ x", tier: "standard");
        var started = await runner.RunAsync(request, CancellationToken.None);
        await runner.Completion(started.Handle!)!.WaitAsync(TimeSpan.FromSeconds(30));
        (await runner.RunAsync(request with { Handle = started.Handle }, CancellationToken.None)).Status.ShouldBe(ReviewAgentStatus.Completed);

        var sent = cli.Requests.ShouldHaveSingleItem();
        sent.Prompt.ShouldContain("Your worktree is checked out at the reviewed head");
        checkout.Calls.ShouldBe([$"add C:\\repos\\widgets 7 {request.HeadSha} {sent.WorkingDirectory}", $"remove C:\\repos\\widgets {sent.WorkingDirectory}"]);

        var deep = request with { Tier = "deep" };
        var failing = await runner.RunAsync(deep, CancellationToken.None);
        await runner.Completion(failing.Handle!)!.WaitAsync(TimeSpan.FromSeconds(30));
        var error = await Should.ThrowAsync<ReviewAgentRunnerException>(() => runner.RunAsync(deep with { Handle = failing.Handle }, CancellationToken.None));
        error.Message.ShouldContain("API Error: 529 overloaded");
        error.Message.ShouldContain("claude -p opus");
        checkout.Calls.Count.ShouldBe(4, "the worktree is removed after a failure too");
        Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task An_unknown_handle_says_the_run_was_lost_and_to_refresh()
    {
        using var runner = new CliReviewAgentRunner(new FakeClaudeCli(_ => throw new InvalidOperationException("not called")), new FakeCheckout(),
            Options.Create(new ReviewAgentSettings()), TimeProvider.System, NullLogger<CliReviewAgentRunner>.Instance, NewRoot());

        var error = await Should.ThrowAsync<ReviewAgentRunnerException>(() =>
            runner.RunAsync(Request("pack") with { Handle = "cli:0123456789abcdef0123456789abcdef" }, CancellationToken.None));

        error.Message.ShouldContain("was lost");
        error.Message.ShouldContain("Refresh the run");
    }

    private static ReviewAgentRequest Request(string pack, string? tier = null) => new(
        Guid.Parse("6f1d3c52-0a5b-4c8e-9d61-2f7a0e4b9c13"), 1, 1, "correctness", pack, "abcdef0123456789", ["SEED-0001"], "acme/widgets", 7,
        new string('b', 40), 1, Tier: tier);

    private static ClaudeCliResult Answer(string text) => new(text, "claude-haiku-5-5", 900, 40, 0.0012m);

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "antiphon-review-cli-tests", Guid.NewGuid().ToString("N"));

    private sealed class FakeClaudeCli(Func<ClaudeCliRequest, Task<ClaudeCliResult>> answer) : IClaudeCli
    {
        public List<ClaudeCliRequest> Requests { get; } = [];
        public List<bool> DirectoryExisted { get; } = [];

        public Task<ClaudeCliResult> RunAsync(ClaudeCliRequest request, CancellationToken ct)
        {
            lock (Requests)
            {
                Requests.Add(request);
                DirectoryExisted.Add(Directory.Exists(request.WorkingDirectory));
            }

            return answer(request);
        }
    }

    private sealed class FakeCheckout : IReviewCheckout
    {
        public List<string> Calls { get; } = [];

        public Task AddAsync(string repoPath, int? prNumber, string headSha, string directory, CancellationToken ct)
        {
            lock (Calls)
                Calls.Add($"add {repoPath} {prNumber} {headSha} {directory}");
            Directory.CreateDirectory(directory);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string repoPath, string directory, CancellationToken ct)
        {
            lock (Calls)
                Calls.Add($"remove {repoPath} {directory}");
            return Task.CompletedTask;
        }
    }
}
