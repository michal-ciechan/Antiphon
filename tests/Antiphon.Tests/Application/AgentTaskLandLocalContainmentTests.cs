using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandLocalContainmentTests
{
    [Test]
    [Arguments("fresh")]
    [Arguments("resume-with-later-local-tip")]
    public async Task C448_V07_LocalContainmentStillPublishes(string boundary)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var remoteBefore = (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim();
        remoteBefore.ShouldBe(h.Fixture.SeedSha);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "merge", "--ff-only", source);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(source);
        (await h.Fixture.Git.RunAsync(h.Fixture.Repository,
            ["merge-base", "--is-ancestor", source, h.Fixture.TargetRef], CancellationToken.None)).ExitCode.ShouldBe(0);
        (await h.Fixture.Git.RunAsync(h.Fixture.Repository,
            ["merge-base", "--is-ancestor", source, remoteBefore], CancellationToken.None)).ExitCode.ShouldBe(1);
        await h.Fixture.AssertRemoteSourceAsync();

        await h.RequestAsync(filter: "/*/*/Fixture/Pass", expectedSourceSha: source);
        h.Fixture.Git.Trace.Clear();
        string? laterLocal = null;
        Guid? operationId = null;
        string? prepared = null;
        if (boundary == "resume-with-later-local-tip")
        {
            h.Fault.Phase = LandPhase.PushStarted;
            h.Fault.AfterCommit = true;
            await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
            h.Fault.Triggered.ShouldBeTrue();
            var cut = (await h.OperationAsync()).ShouldNotBeNull();
            cut.Phase.ShouldBe(LandPhase.PushStarted);
            operationId = cut.Id;
            prepared = cut.VerifiedSourceSha.ShouldNotBeNull();
            (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(remoteBefore);
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "unrelated local tip");
            laterLocal = (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim();
            laterLocal.ShouldNotBe(source);
            h.Fault.AfterCommit = false;
            await h.RestartServicesAsync();
            h.Fixture.Git.Trace.Clear();
        }

        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        if (operationId is not null) op.Id.ShouldBe(operationId.Value);
        op.VerifiedSourceSha.ShouldBe(prepared ?? source);
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        op.RemoteConfirmedAt.ShouldNotBeNull();
        h.Verifier.Calls.ShouldBe(1);
        var pushes = h.Fixture.Git.Trace.Where(a => a.Length > 0 && a[0] == "push").ToList();
        pushes.Count.ShouldBe(1);
        pushes[0].ShouldContain(op.VerifiedSourceSha + ":" + h.Fixture.TargetRef);
        pushes[0].ShouldNotContain("--force");
        pushes[0].ShouldNotContain("--mirror");
        pushes[0].ShouldNotContain(a => a.StartsWith("+", StringComparison.Ordinal));
        var remoteAfter = (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim();
        remoteAfter.ShouldBe(op.VerifiedSourceSha);
        op.ObservedRemoteTargetSha.ShouldBe(remoteAfter);
        if (laterLocal is not null)
        {
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(laterLocal);
            (await h.Fixture.Git.RunAsync(h.Fixture.Repository,
                ["merge-base", "--is-ancestor", laterLocal, remoteAfter], CancellationToken.None)).ExitCode.ShouldBe(1);
            op.CanonicalAdvanceReason.ShouldBe("canonical_diverged");
        }
        else
        {
            op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
            Directory.Exists(h.Fixture.Source).ShouldBeFalse();
            await using var db = h.CreateContext();
            (await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == op.Id)).RemoteConfirmedAt.ShouldNotBeNull();
        }
        await h.Fixture.AssertRemoteSourceAsync();

        if (boundary == "fresh")
        {
            await using var unrelated = new LandingSafetyHarness();
            await unrelated.InitializeAsync();
            var reviewed = await unrelated.AddSourceAsync();
            await unrelated.Fixture.RequiredAsync(unrelated.Fixture.Repository, "commit", "--allow-empty", "-m", "operator local commit");
            var operatorTip = (await unrelated.Fixture.RequiredAsync(unrelated.Fixture.Repository,
                "rev-parse", unrelated.Fixture.TargetRef)).Trim();
            operatorTip.ShouldNotBe(reviewed);
            unrelated.Fixture.Git.Trace.Clear();
            await unrelated.RequestAsync(expectedSourceSha: reviewed);
            await unrelated.RunAsync();
            (await unrelated.OperationAsync()).ShouldBeNull();
            await using var refusal = unrelated.CreateContext();
            (await refusal.AgentTaskLandRequests.SingleAsync(r => r.TaskId == unrelated.Fixture.TaskId))
                .SourceRefusalReason.ShouldBe("target_local_ahead");
            unrelated.Fixture.Git.Trace.ShouldNotContain(a => a.Length > 0 && a[0] == "push");
            Directory.Exists(unrelated.Fixture.Source).ShouldBeTrue();
            (await unrelated.Fixture.RequiredAsync(unrelated.Fixture.Remote, "rev-parse", unrelated.Fixture.TargetRef))
                .Trim().ShouldBe(unrelated.Fixture.SeedSha);
            await unrelated.Fixture.AssertRemoteSourceAsync();
        }
    }
}
