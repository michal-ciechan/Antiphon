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
}
