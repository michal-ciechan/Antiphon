using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Review.Execution;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

public partial class ReviewEndToEndApiTests
{
    [Test]
    public async Task C2110_S1_pg_Expired_lease_resumes_from_committed_Verify_and_posts_once()
    {
        using var deadline = new CancellationTokenSource(StepTimeout);
        using var firstAttempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var ct = deadline.Token;
        var fixture = await SeedRunningVerifyAsync(21115, ct);
        using var first = ActivatorUtilities.CreateInstance<ReviewWorkflowExecutor>(factory.Services, fixture.Clock);
        using var second = ActivatorUtilities.CreateInstance<ReviewWorkflowExecutor>(factory.Services, fixture.Clock);
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.VerifyCut.Arm(fixture.VerifyId, async token =>
        {
            committed.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var firstPass = first.RunPendingAsync(firstAttempt.Token);
        ReviewNodeExecution producer;
        try
        {
            await committed.Task.WaitAsync(ct);
            var busyRun = await ReadRecoveryRunAsync(fixture.Run.Id, ct);
            busyRun.Status.ShouldBe(ReviewRunStatus.Running);
            busyRun.Nodes.Select(n => n.NodeId).Order().ShouldBe(["collate", "verify"]);
            producer = AssertCommittedVerify(busyRun, fixture);
            (await second.RunPendingAsync(ct)).ShouldBe(0);
            fixture.Pr.Read(p => p.AcceptedReviews.Count).ShouldBe(0);
        }
        finally
        {
            factory.VerifyCut.Clear();
            firstAttempt.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(async () => await firstPass);
        }

        (await second.RunPendingAsync(ct)).ShouldBe(0);
        fixture.Clock.Advance(fixture.Lease + TimeSpan.FromSeconds(1));
        (await second.RunPendingAsync(ct)).ShouldBe(1);

        var recovered = await ReadRecoveryRunAsync(fixture.Run.Id, ct);
        recovered.Status.ShouldBe(ReviewRunStatus.AwaitingFixes);
        var sameProducer = AssertCommittedVerify(recovered, fixture);
        (sameProducer.Id, sameProducer.StartedAt, sameProducer.CompletedAt, sameProducer.OutputJson)
            .ShouldBe((producer.Id, producer.StartedAt, producer.CompletedAt, producer.OutputJson));
        var publish = recovered.Nodes.Where(n => n.NodeId == "publish").ShouldHaveSingleItem();
        publish.Status.ShouldBe(ReviewNodeStatus.Succeeded, publish.Summary);
        (publish.RunId, publish.Iteration, publish.ItemKey, publish.Sequence)
            .ShouldBe((fixture.Run.Id, sameProducer.Iteration, (string?)null, sameProducer.Sequence + 1));
        publish.Id.ShouldNotBe(Guid.Empty);
        publish.Id.ShouldNotBe(sameProducer.Id);
        var output = ReviewWalk.Output<PublishOutput>(publish).ShouldNotBeNull();
        output.SummaryOnly.ShouldBe(["F1"]);
        var receipt = Receipt(fixture.Pr, output);
        receipt.Posted.ReviewId.ShouldBe(output.ReviewId);
        receipt.Target.PrNumber.ShouldBe(21115);
        receipt.Review.CommitSha.ShouldBe(recovered.Iterations.ShouldHaveSingleItem().HeadSha);
        receipt.Review.Body.ShouldStartWith("Verified findings\n");
        receipt.Review.Body.ShouldContain(fixture.Candidate.Title);
        receipt.Review.Comments.ShouldBeEmpty();
        (await second.RunPendingAsync(ct)).ShouldBe(0);
        (await ReadRecoveryRunAsync(fixture.Run.Id, ct)).Nodes.Single(n => n.NodeId == "publish").Id.ShouldBe(publish.Id);
        receipt.ShouldBe(Receipt(fixture.Pr, output));
    }

    [Test]
    public async Task C2110_S1_pg_Exception_after_Verify_commit_marks_the_run_failed_outside_the_poll()
    {
        using var deadline = new CancellationTokenSource(StepTimeout);
        var ct = deadline.Token;
        var fixture = await SeedRunningVerifyAsync(21116, ct);
        using var executor = ActivatorUtilities.CreateInstance<ReviewWorkflowExecutor>(factory.Services, fixture.Clock);
        factory.VerifyCut.Arm(fixture.VerifyId, _ => throw new InvalidOperationException("Verify committed before successor"));
        try
        {
            (await executor.RunPendingAsync(ct)).ShouldBe(1);
            var failed = await ReadRecoveryRunAsync(fixture.Run.Id, ct);
            failed.Status.ShouldBe(ReviewRunStatus.Failed);
            failed.WaitingOn.ShouldBeNull();
            AssertCommittedVerify(failed, fixture);
            failed.Nodes.ShouldNotContain(n => n.NodeId == "publish");

            fixture.Clock.Advance(fixture.Lease + TimeSpan.FromSeconds(1));
            (await executor.RunPendingAsync(ct)).ShouldBe(0);
            var stillFailed = await ReadRecoveryRunAsync(fixture.Run.Id, ct);
            stillFailed.Status.ShouldBe(ReviewRunStatus.Failed);
            stillFailed.Nodes.ShouldNotContain(n => n.NodeId == "publish");
            fixture.Pr.Read(p => p.AcceptedReviews.Count).ShouldBe(0);
        }
        finally
        {
            factory.VerifyCut.Clear();
        }
    }

    private async Task<VerifyRecoveryFixture> SeedRunningVerifyAsync(int number, CancellationToken ct)
    {
        var graph = PublishGraph(new Dictionary<string, object?>
        {
            ["inputs"] = new[] { "collate", "verify" },
            ["summaryTemplate"] = "Verified findings\n\n{{findings}}",
        });
        ReviewWorkflowValidator.Validate(graph).Valid.ShouldBeTrue();
        var pr = factory.Host.Open(number, "Recovery fixture", "dana", BaseSha, HeadSha, Files, Diff);
        var candidate = PublishCandidate("C1", "Finding from the real verifier", 55) with { InDiff = false };
        var verify = new ReviewNodeExecution
        {
            Id = Guid.NewGuid(), NodeId = "verify", Kind = ReviewNodeKind.Completion,
            Iteration = 1, Sequence = 2, Status = ReviewNodeStatus.Pending,
        };

        // Only the test's executors see the lease as stale. The background poll keeps running
        // for other tests in this fixture, and it cannot take this particular run.
        var settings = factory.Services.GetRequiredService<IOptions<ReviewExecutorSettings>>().Value;
        var lease = TimeSpan.FromSeconds(Math.Max(30, settings.LeaseSeconds));
        var clock = new ControlledTimeProvider(DateTimeOffset.UtcNow.AddDays(1));
        var run = await SeedPublishAsync(number, graph,
            [Producer("collate", ReviewNodeKind.Join, 1, new CollateOutput([candidate], [], [])), verify], true,
            row =>
            {
                row.Status = ReviewRunStatus.Running;
                row.UpdatedAt = clock.GetUtcNow().UtcDateTime - lease - TimeSpan.FromSeconds(1);
            }, ct);
        return new(run, verify.Id, candidate, pr, clock, lease);
    }

    private async Task<ReviewRun> ReadRecoveryRunAsync(Guid id, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ReviewRuns.AsNoTracking().Include(r => r.Iterations).Include(r => r.Nodes).SingleAsync(r => r.Id == id, ct);
    }

    private ReviewNodeExecution AssertCommittedVerify(ReviewRun run, VerifyRecoveryFixture fixture)
    {
        (run.Id, run.WorkflowRevisionId, run.WorkflowHash)
            .ShouldBe((fixture.Run.Id, fixture.Run.WorkflowRevisionId, fixture.Run.WorkflowHash));
        var iteration = run.Iterations.ShouldHaveSingleItem();
        (iteration.Id, iteration.HeadSha).ShouldBe((fixture.Run.Iterations.Single().Id, HeadSha));
        var producer = run.Nodes.Where(n => n.NodeId == "verify").ShouldHaveSingleItem();
        (producer.Id, producer.RunId, producer.Iteration, producer.ItemKey)
            .ShouldBe((fixture.VerifyId, run.Id, iteration.Iteration, (string?)null));
        producer.Status.ShouldBe(ReviewNodeStatus.Succeeded, producer.Summary);
        var result = ReviewWalk.Output<VerifyOutput>(producer).ShouldNotBeNull();
        result.Verified.ShouldBeTrue();
        result.Kept.ShouldBe([fixture.Candidate.Cid]);
        factory.Completions.Requests.Count(r => r.Instructions.Contains($"verify-{run.Id:N}-{iteration.Iteration}", StringComparison.Ordinal))
            .ShouldBe(1);
        return producer;
    }

    private sealed record VerifyRecoveryFixture(
        ReviewRun Run, Guid VerifyId, FindingCandidate Candidate, ScriptedPr Pr, ControlledTimeProvider Clock, TimeSpan Lease);

    internal sealed class VerifyCommitCut : SaveChangesInterceptor
    {
        private sealed record Cut(Guid VerifyId, Func<CancellationToken, Task> AfterCommit);
        private Cut? _cut;

        public void Arm(Guid verifyId, Func<CancellationToken, Task> afterCommit) =>
            Volatile.Write(ref _cut, new Cut(verifyId, afterCommit));

        public void Clear() => Volatile.Write(ref _cut, null);

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            var cut = Volatile.Read(ref _cut);
            if (cut is not null && data.Context!.ChangeTracker.Entries<ReviewNodeExecution>()
                .Any(entry => entry.Entity.Id == cut.VerifyId && entry.Entity.Status == ReviewNodeStatus.Succeeded)
                && ReferenceEquals(Interlocked.CompareExchange(ref _cut, null, cut), cut))
                await cut.AfterCommit(ct);
            return result;
        }
    }
}
