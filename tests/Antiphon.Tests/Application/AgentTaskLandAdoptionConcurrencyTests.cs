using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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
        fixture.Boundary.Fired.ShouldBe(1, "C883: typed request fault fired at historical cut");
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
}
