using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Review.Decisions;
using Antiphon.Server.Application.Review.Execution;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Review;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReviewCliFailureWorkflowTests
{
    private const string Chain = "x";
    private const string Provider = "c";
    private const string FixtureModel = "fixture-review-cli-failure";
    private const string Repo = "acme/widgets";
    private const string DecisionNode = "d";
    private const string CompletionNode = "v";
    private const string GateNode = "next";
    private static readonly string[] SensitiveValues =
    [
        "xyShort", "aGVsbG86d29ybGQ=", "cook1e", "track8", "fixture token value", "escaped", "quote",
        "user:pass", "fixture-user@host.example", "fixture-owner", "fixture-unlabelled-owner",
    ];

    [Test]
    [Arguments(true, true)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task Queued_CLI_failures_persist_safe_diagnostics_and_obey_onFailure(bool continueDecision, bool continueCompletion)
    {
        var ct = CancellationToken.None;
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var host = new ScriptedCodeHost();
        var pr = host.Open(57, "Fixture change", "fixture-author", new string('a', 40), new string('b', 40), [], "");
        var logs = new ReviewTestLog();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)));
        services.AddSingleton<ICodeHost>(host);
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CodeHostRouter>();
        services.AddScoped<ReviewFindingSync>();
        services.AddSingleton<ReviewWorkflowExecutor>();
        services.AddSingleton<ReviewExecutorSignal>();
        services.AddSingleton(Options.Create(new ReviewExecutorSettings()));
        services.AddSingleton(Options.Create(new ReviewSettings()));
        services.AddSingleton(Options.Create(new ReviewPolicySettings()));
        services.Configure<ReviewCodeHostSettings>(settings => settings.ApplyDefaults());
        services.Configure<ReviewAgentSettings>(settings => settings.Cli.Command = RemoteControlPtyLane.FakeClaudeExe);
        services.Configure<ReviewDecisionSettings>(settings =>
        {
            settings.Providers[Provider] = new ReviewDecisionProvider { Dialect = "claude-cli", Model = FixtureModel };
            settings.Chains[Chain] = [new() { Provider = Provider, Transport = "OneShot" }];
        });
        services.Configure<ReviewCompletionSettings>(settings =>
        {
            settings.Providers[Provider] = new ReviewCompletionProvider { Dialect = "claude-cli", Model = FixtureModel };
            settings.Chains[Chain] = [new() { Provider = Provider, Transport = "OneShot" }];
        });
        services.AddSingleton<IClaudeCli, ClaudeCliProcess>();
        services.AddSingleton<IDecisionModel, ClaudeCliDecisionModel>();
        services.AddSingleton<ICompletionModel, ClaudeCliCompletionModel>();
        services.AddSingleton<DecisionChain>();
        services.AddSingleton<CompletionChain>();
        await using var provider = services.BuildServiceProvider();
        File.Exists(RemoteControlPtyLane.FakeClaudeExe).ShouldBeTrue("the staged fake executable is required");

        Guid runId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            runId = await QueueAsync(db, pr, continueDecision, continueCompletion, ct);
        }

        var executor = provider.GetRequiredService<ReviewWorkflowExecutor>();
        (await executor.RunPendingAsync(ct)).ShouldBe(1);

        await using var reader = provider.CreateAsyncScope();
        var reloaded = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var status = await reloaded.ReviewRuns.Where(run => run.Id == runId).Select(run => run.Status).SingleAsync(ct);
        var nodes = await reloaded.ReviewNodeExecutions.AsNoTracking()
            .Where(node => node.RunId == runId && node.Iteration == 1 && node.ItemKey == null)
            .ToDictionaryAsync(node => node.NodeId, ct);
        AssertFailure(nodes[DecisionNode], continueDecision);
        if (continueDecision)
            AssertFailure(nodes[CompletionNode], continueCompletion);
        else
            nodes.ShouldNotContainKey(CompletionNode);

        var stopped = !continueDecision || !continueCompletion;
        var statuses = pr.Read(p => p.Statuses.Where(s => s.Status.State == CodeHostStatusState.Error).ToArray());
        if (stopped)
        {
            status.ShouldBe(ReviewRunStatus.Failed);
            nodes.ShouldNotContainKey(GateNode);
            var error = statuses.ShouldHaveSingleItem().Status.Description;
            error.ShouldStartWith($"Failed at {(continueDecision ? CompletionNode : DecisionNode)}:");
            error.ShouldContain("exit 7");
            AssertSafe(error);
        }
        else
        {
            status.ShouldBe(ReviewRunStatus.AtGate);
            nodes[GateNode].Status.ShouldBe(ReviewNodeStatus.Waiting);
            statuses.ShouldBeEmpty();
        }

        var diagnostics = logs.Warnings.Where(message => message.Contains("exit 7", StringComparison.Ordinal)).ToArray();
        diagnostics.Length.ShouldBe((continueDecision ? 2 : 1) + (stopped ? 1 : 0),
            "each CLI failure is logged by its chain, and each stopped run is logged by the executor");
        foreach (var diagnostic in diagnostics)
            AssertSafe(diagnostic);
    }

    private static void AssertFailure(ReviewNodeExecution node, bool continued)
    {
        node.CompletedAt.ShouldNotBeNull();
        node.Status.ShouldBe(continued ? ReviewNodeStatus.FailedContinued : ReviewNodeStatus.Failed);
        node.Summary.ShouldNotBeNull();
        node.Summary.ShouldContain("exit 7");
        node.Summary.ShouldContain("CLI backend overloaded");
        AssertSafe(node.Summary);
    }

    private static void AssertSafe(string text)
    {
        foreach (var sensitive in SensitiveValues)
            text.Contains(sensitive, StringComparison.Ordinal).ShouldBeFalse("the public diagnostic excludes fixture credentials and account IDs");
    }

    private static async Task<Guid> QueueAsync(AppDbContext db, ScriptedPr pr, bool continueDecision, bool continueCompletion,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var revisionId = Guid.NewGuid();
        var workflow = new WorkflowDefinition("cli-failure-fixture", 1, [],
            [
                new("collate", ReviewNodeKind.Code, "CollateFindings", null, new Dictionary<string, object?>()),
                FailureNode(DecisionNode, ReviewNodeKind.Decision, "Categorise", continueDecision),
                FailureNode(CompletionNode, ReviewNodeKind.Completion, "VerifyFindings", continueCompletion),
                new(GateNode, ReviewNodeKind.Gate, null, null, new Dictionary<string, object?>()),
            ],
            [
                new("collate", DecisionNode, null, null, null, null),
                new(DecisionNode, CompletionNode, null, null, null, null),
                new(CompletionNode, GateNode, null, null, null, null),
            ]);
        db.ReviewWorkflowRevisions.Add(new ReviewWorkflowRevision
        {
            Id = revisionId, Name = workflow.Name, Hash = workflow.Hash(), CreatedAt = now,
            GraphJson = JsonSerializer.Serialize(ReviewWorkflowService.ToDto(workflow, revisionId), ReviewWorkflowService.GraphJsonOptions),
        });

        var id = Guid.NewGuid();
        var subject = new ReviewSubject
        {
            Id = Guid.NewGuid(), Host = ReviewHost.GitHub, Repo = Repo, PrNumber = pr.Number, Title = pr.Title,
            BaseRef = "main", BaseSha = pr.BaseSha, HeadRef = "fixture", HeadSha = pr.HeadSha, CreatedAt = now,
        };
        var candidate = new FindingCandidate("F1", "fixture", "fixture", ReviewSeverity.Major, 1, "fixture.cs", 1, null,
            true, "anchor", "Fixture finding", "Fixture body", null, [], null);
        db.ReviewRuns.Add(new ReviewRun
        {
            Id = id, SubjectId = subject.Id, Subject = subject, Status = ReviewRunStatus.Queued, Stage = ReviewStage.Categorise,
            WorkflowName = workflow.Name, WorkflowRevisionId = revisionId, WorkflowHash = workflow.Hash(), StartedAt = now, UpdatedAt = now,
            Iterations = [new() { Id = Guid.NewGuid(), RunId = id, Iteration = 1, Round = 1, BaseSha = pr.BaseSha, HeadSha = pr.HeadSha, StartedAt = now }],
            Nodes =
            [
                // The queued run already carries one finding so VerifyFindings must call the completion chain.
                new() { Id = Guid.NewGuid(), RunId = id, Iteration = 1, Sequence = 1, NodeId = "collate",
                    Kind = ReviewNodeKind.Code, Status = ReviewNodeStatus.Succeeded, CompletedAt = now,
                    OutputJson = JsonSerializer.Serialize(new CollateOutput([candidate], [], []), ReviewWalk.Json) },
            ],
        });
        await db.SaveChangesAsync(ct);
        return id;
    }

    private static WorkflowNode FailureNode(string id, ReviewNodeKind kind, string run, bool continued) =>
        new(id, kind, run, null, new Dictionary<string, object?>
        {
            ["chain"] = Chain,
            ["onFailure"] = continued ? "continue" : "stop",
        });
}
