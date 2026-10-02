using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandAdoptionConcurrencyTests
{
    [Test]
    public async Task C883_Save409FaultThenFreshRequestCompletes()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        var failure = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        fixture.Boundary.Reached.ShouldBe(1, "C883: historical cut reached once");
        fixture.Interceptor.Fired.ShouldBe(1, "C883: typed request save conflict fired");
        await h.FailAsync(failure);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(reviewed, "C883: HEAD moved to S");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim()
            .ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", local + "^{tree}")).Trim(),
                "C883: index remains L");
        (await h.Fixture.Git.RunAsync(h.Fixture.Source,
            ["diff", "--quiet", "--no-ext-diff", "--no-textconv", local, "--"], CancellationToken.None))
            .Succeeded.ShouldBeTrue("C883: worktree remains L");
        h.Fixture.Git.Commands.ShouldNotContain(x => x.Directory == h.Fixture.Source
            && x.Arguments.Length > 0 && x.Arguments[0] == "reset", "C883: no reset after historical cut");
        await using (var db = h.CreateContext())
        {
            var old = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
            old.TerminalFailureCode.ShouldBe("landing_concurrency_conflict", "C883: first request refused");
            old.SourceAdvanceChildOperation.ShouldBe("source-adopt-reset", "C883: old intent retained");
        }
        var second = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        second.RequestId.ShouldNotBe(first.RequestId, "C883: fresh request identity");
        await h.RunQueuedAsync();
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue("C883: fresh request publication confirmed");
    }

    [Test]
    public async Task C883_MonitorBetweenMoveAndResetDoesNotPreventReset()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = request.RequestId;
        fixture.Boundary.ThrowConflict = false;
        Guid before = Guid.Empty;
        Guid after = Guid.Empty;
        DateTime evaluated = default;
        fixture.Boundary.AtCut = async () =>
        {
            await using var db = h.CreateContext();
            var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
            before = row.ConcurrencyToken;
            var monitor = new AgentTaskLandMonitorService(db, h.Clock,
                Options.Create(new DelegationSettings()), h.Events);
            await monitor.SweepAsync(CancellationToken.None);
            await using var fresh = h.CreateContext();
            row = await fresh.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
            after = row.ConcurrencyToken;
            evaluated = row.LastEvaluatedAt;
        };
        await h.RunQueuedAsync();
        fixture.Boundary.Reached.ShouldBe(1, "V2.MonitorCutReached");
        before.ShouldNotBe(Guid.Empty, "V2.IntentCommittedBeforeCAS");
        after.ShouldNotBe(before, "V2.MonitorRotatedTokenAtCut");
        await using var verify = h.CreateContext();
        var saved = await verify.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        saved.LastEvaluatedAt.ShouldBe(evaluated, "V2.MonitorEvaluationPreserved");
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue("V2.PublicationSurvivesMonitorWrite");
    }

    [Test]
    public async Task C883_PreIntentConflictLeavesCheckoutUnmoved()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        fixture.Interceptor.OperationFilter = "source-adopt-reset";
        fixture.Interceptor.Armed = true;
        var conflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        fixture.Interceptor.Fired.ShouldBe(1, "V2.PreIntentSaveFaultFired");
        fixture.Boundary.Reached.ShouldBe(0, "V2.RefMoveBoundaryNotReached");
        await h.FailAsync(conflict);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(local, "V2.PreIntentHeadUnmoved");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim()
            .ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", local + "^{tree}")).Trim(),
                "V2.PreIntentIndexUnmoved");
        h.Fixture.Git.Commands.ShouldNotContain(x => x.Arguments.Count(a => a == "--no-deref") > 0
            && x.Arguments.Contains(h.Fixture.SourceRef), "V2.NoSourceCASAfterPreIntentFault");
        h.Fixture.Git.Commands.ShouldNotContain(x => x.Directory == h.Fixture.Source
            && x.Arguments.Length > 0 && x.Arguments[0] == "reset", "V2.NoOwnerResetAfterPreIntentFault");
        var second = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        second.RequestId.ShouldNotBe(first.RequestId, "V2.PreIntentRecoveryFreshId");
        await h.RunQueuedAsync();
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue("V2.PreIntentRetryPublished");
    }

    [Test]
    public async Task C883_PostResetSaveConflictFreshRequestCompletes()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        fixture.Boundary.ThrowConflict = false;
        h.Fixture.Git.AfterCommand = (directory, args, result) =>
        {
            if (result.Succeeded && directory == h.Fixture.Source && args.Count > 0 && args[0] == "reset")
                fixture.Interceptor.Armed = true;
            return Task.CompletedTask;
        };
        var conflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        fixture.Boundary.Reached.ShouldBe(1, "V2.PostResetHistoricalCutReached");
        fixture.Interceptor.Fired.ShouldBe(1, "V2.PostResetSaveFaultFired");
        await h.FailAsync(conflict);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(reviewed, "V2.PostResetHeadAligned");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim()
            .ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", reviewed + "^{tree}")).Trim(),
                "V2.PostResetIndexAligned");
        await using (var db = h.CreateContext())
        {
            var old = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
            old.TerminalFailureCode.ShouldBe("landing_concurrency_conflict", "V2.PostResetFailureClassified");
            old.SourceAdvanceChildOperation.ShouldBe("source-adopt-reset", "V2.PostResetIntentRetained");
        }
        var resetCount = h.Fixture.Git.Commands.Count(x => x.Directory == h.Fixture.Source
            && x.Arguments.Length > 0 && x.Arguments[0] == "reset");
        var second = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        await h.RunQueuedAsync();
        second.RequestId.ShouldNotBe(first.RequestId, "V2.PostResetFreshId");
        h.Fixture.Git.Commands.Count(x => x.Directory == h.Fixture.Source
            && x.Arguments.Length > 0 && x.Arguments[0] == "reset")
            .ShouldBe(resetCount, "V2.NoResetReplayAfterAlignment");
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue("V2.PostResetFreshPublication");
    }

    [Test]
    public async Task C883_HistoricalMonitorSaveConflictThenFreshRequestCompletes()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync(bulk: true);
        var first = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = first.RequestId;
        fixture.Boundary.ThrowConflict = false;
        Guid before = Guid.Empty;
        Guid after = Guid.Empty;
        fixture.Boundary.AtCut = async () =>
        {
            var status = await h.Fixture.RequiredAsync(h.Fixture.Source, "status", "--porcelain=v1", "-z");
            var rows = status.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            rows.Count(x => x.StartsWith("M ", StringComparison.Ordinal)).ShouldBe(51, "V2.BulkStagedModifications");
            rows.Count(x => x.StartsWith("D ", StringComparison.Ordinal)).ShouldBe(13, "V2.BulkStagedDeletions");
            rows.ShouldAllBe(x => x.Length > 1 && x[1] == ' ', "V2.NoWorktreeChanges");
            (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim()
                .ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", local + "^{tree}")).Trim(),
                    "V2.BulkIndexStillOldTip");
            await using var stale = h.CreateContext();
            var request = await stale.AgentTaskLandRequests.SingleAsync(r => r.Id == first.RequestId);
            before = request.ConcurrencyToken;
            await using (var other = h.CreateContext())
            {
                var monitor = new AgentTaskLandMonitorService(other, h.Clock,
                    Options.Create(new DelegationSettings()), h.Events);
                await monitor.SweepAsync(CancellationToken.None);
            }
            await using (var observed = h.CreateContext())
                after = (await observed.AgentTaskLandRequests.AsNoTracking()
                    .SingleAsync(r => r.Id == first.RequestId)).ConcurrencyToken;
            request.ConcurrencyToken = Guid.NewGuid(); // Test-only replay of the old naked save.
            await stale.SaveChangesAsync();
        };
        var conflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        fixture.Boundary.Reached.ShouldBe(1, "V2.HistoricalCutReached");
        before.ShouldNotBe(after, "V2.ActualMonitorConflictToken");
        await h.FailAsync(conflict);
        await using (var db = h.CreateContext())
        {
            var old = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == first.RequestId);
            old.TerminalFailureCode.ShouldBe("landing_concurrency_conflict", "V2.HistoricalFailureClassified");
            old.SourceAdvanceChildOperation.ShouldBe("source-adopt-reset", "V2.HistoricalIntentRetained");
        }
        var second = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        second.RequestId.ShouldNotBe(first.RequestId, "V2.BulkRecoveryUsesFreshId");
        await h.RunQueuedAsync();
        var op = await h.OperationAsync();
        (op is not null && new AgentTaskLandingState().HasPublication(op))
            .ShouldBeTrue("V2.BulkRecoveryPublishes");
    }
    [Test]
    public async Task C883_HeldWriterBetweenMoveAndResetDoesNotLoseHoldFacts()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        fixture.Interceptor.RequestId = request.RequestId;
        fixture.Boundary.ThrowConflict = false;
        AgentTaskLandRequest? held = null;
        fixture.Boundary.AtCut = async () =>
        {
            (await h.RunRequestAsync(request.RequestId)).ShouldBe(LandRunResult.Held, "V2.CompetingDrainHeldByRealLease");
            await using var db = h.CreateContext();
            held = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
            held.State.ShouldBe(LandRequestState.Held);
            held.HoldReasonCode.ShouldBe("repository_mutation_lease_busy");
            held.HoldEpisode.ShouldBeGreaterThan(0);
            held.LastWriterOperation.ShouldBe("hold");
            held.LastWriterToken.ShouldBe(held.ConcurrencyToken);
        };
        AgentTaskLandRequest? acknowledged = null;
        fixture.Interceptor.AfterSave = async context =>
        {
            if (!context.ChangeTracker.Entries<AgentTaskLandRequest>().Any(e => e.Entity.Id == request.RequestId
                && e.Entity.LastWriterOperation == "source-checkpoint" && e.Entity.SourceAdvanceChildOperation is null)) return;
            await using var db = h.CreateContext();
            acknowledged = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        };
        await h.RunQueuedAsync();
        fixture.Boundary.Reached.ShouldBe(1);
        held.ShouldNotBeNull();
        acknowledged.ShouldNotBeNull();
        acknowledged.HoldEpisode.ShouldBe(held.HoldEpisode, "V2.HoldEpisodeRetainedAcrossReset");
        acknowledged.HoldReasonCode.ShouldBe(held.HoldReasonCode);
        acknowledged.HoldingTaskId.ShouldBe(held.HoldingTaskId);
        await using var verify = h.CreateContext();
        var final = await verify.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
        final.HoldEpisode.ShouldBe(held.HoldEpisode, "V2.SourceCheckpointPreservesHoldEpisode");
        h.Fixture.Git.Commands.Count(x => x.Directory == h.Fixture.Source && x.Arguments[0] == "reset").ShouldBe(1, "V2.OnlyLeaseOwnerResets");
        new AgentTaskLandingState().HasPublication((await h.OperationAsync()).ShouldNotBeNull()).ShouldBeTrue();
    }

    [Test]
    public async Task C883_RestartAfterMoveResumesPendingBeforeFreshAdmission()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var request = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true);
        fixture.Interceptor.RequestId = request.RequestId;
        fixture.Boundary.ThrowConflict = false;
        fixture.Boundary.AtCut = () => throw new OperationCanceledException("owned controlled shutdown cut");
        await Should.ThrowAsync<OperationCanceledException>(() => h.RunQueuedAsync());
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(reviewed);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "write-tree")).Trim()
            .ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", local + "^{tree}")).Trim());
        await h.RestartServicesAsync();
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() => h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence, recoverReviewedSource: true));
        await using (var db = h.CreateContext())
        {
            var pending = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
            pending.IsPending.ShouldBeTrue();
            pending.SourceAdvanceChildOperation.ShouldBe("source-adopt-reset");
            pending.RecoveryLocalBeforeSha.ShouldBe(local);
        }
        fixture.Boundary.AtCut = null;
        await h.RunRequestAsync(request.RequestId);
        new AgentTaskLandingState().HasPublication((await h.OperationAsync()).ShouldNotBeNull()).ShouldBeTrue("V2.RestartResumesOriginalRequest");
        await using var verify = h.CreateContext();
        (await verify.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId)).IsPending.ShouldBeFalse();
        (await verify.AgentTaskLandRequests.CountAsync(r => r.TaskId == h.Fixture.TaskId)).ShouldBe(1, "V2.NoReplacementAdmitted");
    }
}
