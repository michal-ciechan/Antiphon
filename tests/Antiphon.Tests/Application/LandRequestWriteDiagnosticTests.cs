using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task C883_MonitorWriterAttributionIsTokenLinked(bool staleStamp)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var queued = await h.RequestAsync();
        await using var stale = h.CreateContext();
        var tracked = await stale.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        var originalToken = tracked.ConcurrencyToken;
        await using (var monitorDb = h.CreateContext())
        {
            var monitor = new AgentTaskLandMonitorService(monitorDb, TimeProvider.System,
                Options.Create(new DelegationSettings()), new MockEventBus());
            await monitor.SweepAsync(CancellationToken.None);
        }
        await using (var observer = h.CreateContext())
        {
            var stored = await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            stored.ConcurrencyToken.ShouldNotBe(originalToken, "C883: real monitor token write committed");
            stored.LastWriterOperation.ShouldBe("monitor-sweep", "C883: monitor stamp committed");
            stored.LastWriterToken.ShouldBe(stored.ConcurrencyToken, "C883: stamp and token share row update");
            if (staleStamp)
            {
                stored.LastWriterToken = Guid.NewGuid();
                await observer.SaveChangesAsync();
            }
        }
        tracked.LastProgressAt = tracked.LastProgressAt.AddSeconds(1);
        var conflict = await Should.ThrowAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        var entries = LandFailureDiagnostic.CaptureConcurrencyEntries(conflict);
        entries.Count.ShouldBe(1, "C883: exact conflicting entry captured");
        entries[0].Entity.ShouldBe(nameof(AgentTaskLandRequest), "C883: actual entity type captured");
        entries[0].Key.ShouldBe(queued.RequestId, "C883: actual request key captured");
        entries[0].OriginalToken.ShouldBe(originalToken, "C883: original token captured before tracker clear");
        stale.ChangeTracker.Clear();
        var summary = await LandFailureDiagnostic.DescribeConcurrencyAsync(stale, conflict, entries,
            "source_adoption", h.Fixture.TaskId, queued.RequestId, 1, CancellationToken.None);
        summary.ShouldContain($"request={queued.RequestId:N}", Case.Sensitive,
            "C883: request attribution persisted in safe summary");
        summary.ShouldContain(staleStamp ? "observedDatabaseWriter=unknown" : "observedDatabaseWriter=monitor-sweep",
            Case.Sensitive, "C883: observed writer follows committed token only");
        summary.ShouldNotContain("valuable feature", Case.Sensitive, "C883: no worktree content in summary");
    }
}
