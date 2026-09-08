using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandPersistenceFailureTests
{
    [Test]
    [Arguments(LandPhase.TargetAdvanceStarted, false)]
    [Arguments(LandPhase.TargetAdvanceStarted, true)]
    [Arguments(LandPhase.PushStarted, false)]
    [Arguments(LandPhase.PushStarted, true)]
    [Arguments(LandPhase.PublicationConfirmed, false)]
    [Arguments(LandPhase.PublicationConfirmed, true)]
    [Arguments(LandPhase.CleanupStarted, false)]
    [Arguments(LandPhase.CleanupStarted, true)]
    [Arguments(LandPhase.Complete, false)]
    [Arguments(LandPhase.Complete, true)]
    public async Task C448_V16_AcknowledgedCheckpointsGateDependentMutations(LandPhase phase, bool afterCommit)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = await h.AddSourceAsync();
        h.Fault.Phase = phase;
        h.Fault.AfterCommit = afterCommit;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        h.Fault.Triggered.ShouldBeTrue();
        if (phase != LandPhase.Complete)
        {
            Directory.Exists(h.Fixture.Source).ShouldBeTrue("save acknowledgement must precede dependent removal");
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
        }
        if (phase == LandPhase.TargetAdvanceStarted)
        {
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
            h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
        }
        if (phase == LandPhase.PushStarted) h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
        if (phase == LandPhase.Complete)
        {
            await using var observer = h.CreateContext();
            var task = await observer.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            var op = await observer.AgentTaskLandings.SingleAsync(o => o.TaskId == task.Id);
            (op.Phase == LandPhase.Complete).ShouldBe(afterCommit);
            (task.LandRequestedAt is null).ShouldBe(afterCommit);
            (await observer.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Landed)).ShouldBe(afterCommit);
        }
        await h.RunAsync();
        var recovered = (await h.OperationAsync()).ShouldNotBeNull();
        recovered.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        recovered.VerifiedSourceSha.ShouldBe(sha);
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(sha);
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
