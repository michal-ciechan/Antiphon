using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Review.Execution;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

public partial class ReviewEndToEndApiTests
{
    [Test]
    public async Task C2110_S1_pg_Declared_publish_inputs_use_the_latest_row_of_the_named_node_in_this_iteration()
    {
        const int number = 21101;
        var pr = factory.Host.Open(number, "Explicit provenance", "dana", BaseSha, HeadSha, Files, Diff);
        var graph = PublishGraph(new Dictionary<string, object?> { ["inputs"] = new[] { "collate", "verify" } });
        var verify = graph.Node("verify")!;
        graph = graph with
        {
            Nodes = [.. graph.Nodes, verify with { Id = "verify-decoy" }],
            Edges = [.. graph.Edges.Where(e => e.From != "verify"),
                new("verify", "verify-decoy", null, null, null, null), new("verify-decoy", "publish", null, null, null, null)],
        };
        var named = PublishCandidate("A", "Named verify finding A", 13);
        var decoy = PublishCandidate("B", "Decoy verify finding B", 14);
        var id = await WalkPublishAsync(number, graph,
        [
            Producer("collate", ReviewNodeKind.Join, 1, new CollateOutput([named, decoy], [], [])),
            Producer("verify", ReviewNodeKind.Completion, 2, new VerifyOutput(["A"], [], true)),
            Producer("verify-decoy", ReviewNodeKind.Completion, 3, new VerifyOutput(["B"], [], true)),
        ]);

        await using var reader = factory.Services.CreateAsyncScope();
        var db = reader.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = await db.ReviewRuns.AsNoTracking().Include(r => r.Iterations).Include(r => r.Nodes).SingleAsync(r => r.Id == id);
        run.Status.ShouldBe(ReviewRunStatus.AwaitingFixes);
        var iteration = run.Iterations.ShouldHaveSingleItem();
        iteration.HeadSha.ShouldBe(HeadSha);
        var rows = run.Nodes.OrderBy(n => n.Sequence).ToList();
        rows.Take(3).Select(n => n.NodeId).ShouldBe(["collate", "verify", "verify-decoy"]);
        rows.Take(3).ShouldAllBe(n => n.Iteration == iteration.Iteration && n.Status == ReviewNodeStatus.Succeeded && n.ItemKey == null);
        ReviewWalk.Output<VerifyOutput>(rows[1])!.Kept.ShouldBe(["A"]);
        ReviewWalk.Output<VerifyOutput>(rows[2])!.Kept.ShouldBe(["B"]);
        var publish = rows.Single(n => n.NodeId == "publish");
        publish.Status.ShouldBe(ReviewNodeStatus.Succeeded, publish.Summary);
        var output = ReviewWalk.Output<PublishOutput>(publish).ShouldNotBeNull();
        output.Inline.ShouldBe(["F1"]);
        var receipt = pr.Read(p => p.AcceptedReviews.Values.ShouldHaveSingleItem());
        receipt.Posted.ReviewId.ShouldBe(output.ReviewId);
        receipt.Target.Repo.ShouldBe("acme/widgets");
        receipt.Target.PrNumber.ShouldBe(number);
        receipt.Review.CommitSha.ShouldBe(iteration.HeadSha);
        var finding = await db.ReviewFindings.AsNoTracking().SingleAsync(f => f.RunId == id);
        var acceptedComment = receipt.Posted.Comments.ShouldHaveSingleItem();
        finding.ExternalCommentId.ShouldBe(acceptedComment.CommentId);
        finding.ExternalThreadId.ShouldBe(acceptedComment.ThreadId);
        finding.Posted.ShouldBeTrue();

        receipt.Review.Body.ShouldContain(named.Title, Case.Sensitive, "Publish must use the declared Verify execution");
        receipt.Review.Body.ShouldNotContain(decoy.Title);
        var inline = receipt.Review.Comments.ShouldHaveSingleItem();
        (inline.Path, inline.Line).ShouldBe((named.File, named.Line));
    }

    private static WorkflowDefinition PublishGraph(IReadOnlyDictionary<string, object?> parameters)
    {
        var graph = ReviewWorkflowYaml.LoadDefault();
        return graph with
        {
            Name = $"publish-fixture-{Guid.NewGuid():N}",
            Nodes = graph.Nodes.Select(n => n.Id == "publish" ? n with { Params = parameters } : n).ToList(),
            Edges = graph.Edges.Select(e => e.From == "publish" ? e with { To = "await-fixes" } : e).ToList(),
        };
    }

    private static FindingCandidate PublishCandidate(string id, string title, int line) =>
        new(id, "fixture", "none", ReviewSeverity.Major, 0.99, "src/Lease.cs", line, null, true,
            $"anchor-{id}", title, $"Body for {id}", null, [], null);

    private static ReviewNodeExecution Producer(string id, ReviewNodeKind kind, int sequence, object output,
        ReviewNodeStatus status = ReviewNodeStatus.Succeeded) =>
        new()
        {
            Id = Guid.NewGuid(), NodeId = id, Kind = kind, Iteration = 1, Sequence = sequence, Status = status,
            OutputJson = ReviewWalk.ToJson(output), StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
        };

    private async Task<Guid> WalkPublishAsync(int number, WorkflowDefinition graph, List<ReviewNodeExecution> rows)
    {
        using var deadline = new CancellationTokenSource(StepTimeout);
        var ct = deadline.Token;
        var now = DateTime.UtcNow;
        var revision = new ReviewWorkflowRevision { Id = Guid.NewGuid(), Name = graph.Name, Hash = graph.Hash(), CreatedAt = now };
        revision.GraphJson = System.Text.Json.JsonSerializer.Serialize(
            ReviewWorkflowService.ToDto(graph, revision.Id), ReviewWorkflowService.GraphJsonOptions);
        var subject = new ReviewSubject
        {
            Id = Guid.NewGuid(), Host = ReviewHost.GitHub, Repo = "acme/widgets", PrNumber = number, Title = "Publish fixture",
            AuthorLogin = "dana", OpenedByLogin = "dana", BaseRef = "main", HeadRef = $"fix/pr-{number}",
            BaseSha = BaseSha, HeadSha = HeadSha, CreatedAt = now,
        };
        var run = new ReviewRun
        {
            Id = Guid.NewGuid(), Subject = subject, Status = ReviewRunStatus.Queued, Stage = ReviewStage.Publish,
            WorkflowName = revision.Name, WorkflowRevisionId = revision.Id, WorkflowHash = revision.Hash, StartedAt = now, UpdatedAt = now,
            Iterations = [new() { Id = Guid.NewGuid(), Iteration = 1, Round = 1, Trigger = "opened", BaseSha = BaseSha, HeadSha = HeadSha, StartedAt = now }],
            Nodes = rows,
        };
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ReviewWorkflowRevisions.Add(revision);
            db.ReviewRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }

        var executor = factory.Services.GetServices<IHostedService>().OfType<ReviewWorkflowExecutor>().Single();
        await executor.RunPendingAsync(ct);

        // The hosted poll can win the claim. Read through a fresh scope until its walk is parked.
        await using var reader = factory.Services.CreateAsyncScope();
        var states = reader.ServiceProvider.GetRequiredService<AppDbContext>().ReviewRuns.AsNoTracking().Where(r => r.Id == run.Id);
        while (await states.Select(r => r.Status).SingleAsync(ct) is ReviewRunStatus.Queued or ReviewRunStatus.Running)
            await Task.Delay(25, ct);
        return run.Id;
    }
}
