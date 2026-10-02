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
public sealed class LandRequestWriteDiagnosticTests
{
    [Test]
    public async Task C883_WriterStampComesFromCommittedToken()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var accepted = await h.RequestAsync();
        await using (var db = h.CreateContext())
        {
            var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
            row.LastWriterOperation.ShouldBe("admission", "D.AdmissionStamped");
            row.LastWriterToken.ShouldBe(row.ConcurrencyToken, "D.AdmissionTokenLinked");
            row.LastWriterAt.ShouldNotBeNull("D.AdmissionTimeStamped");
        }
        await using (var db = h.CreateContext())
        {
            var monitor = new AgentTaskLandMonitorService(db, h.Clock,
                Options.Create(new DelegationSettings()), h.Events);
            await monitor.SweepAsync(CancellationToken.None);
        }
        await using (var db = h.CreateContext())
        {
            var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
            row.LastWriterOperation.ShouldBe("monitor-sweep", "D.MonitorStamped");
            row.LastWriterToken.ShouldBe(row.ConcurrencyToken, "D.MonitorTokenLinked");
        }
    }

    [Test]
    public async Task C883_ConflictNamesActualEntityAndTokens()
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (_, reviewed, evidence) = await fixture.SeedReviewedDescendantAsync();
        var accepted = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        fixture.Interceptor.RequestId = accepted.RequestId;
        var failure = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => h.RunQueuedAsync());
        await h.FailAsync(failure);
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
        row.TerminalFailureCode.ShouldBe("landing_concurrency_conflict", "D.ConcurrencyCodePersisted");
        var terminal = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.Id == row.TerminalEventId);
        terminal.Detail.ShouldContain("entity=AgentTaskLandRequest", "D.EntryEntityNamed");
        terminal.Detail.ShouldContain($"row={accepted.RequestId:N}", "D.EntryRowNamed");
        terminal.Detail.ShouldContain("originalToken=", "D.OriginalTokenNamed");
        terminal.Detail.ShouldContain("attemptedToken=", "D.AttemptedTokenNamed");
        terminal.Detail.ShouldContain("observedDatabaseWriter=", "D.ObservedWriterNamed");
        terminal.Detail.ShouldNotContain("fixture-request-save-conflict", "D.RawExceptionHidden");
    }
}
