using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), Category("Slow")]
[NotInParallel("SessionLiveness")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CompletedCardWorkspaceRemovalTests
{
    // PC-223/230: bypass normalization or take session before workspace -> concurrent entry or held session lock.
    [Test]
    public async Task C1032_FenceStartOrders()
    {
        await using var f = new CompletedCardRunnerFixture();
        f.InstallGate();
        await f.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        var entered = NewBarrier(); var release = NewBarrier();
        var retire = f.Retire(effect: async () => { entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var id = Guid.NewGuid();
        var launch = f.Runtime.StartAsync(f.Launch(id, Path.Combine(f.Checkout, "..", "task-deadbeef")), CancellationToken.None);
        try
        {
            f.Runtime.LaunchGateForTest(id).CurrentCount.ShouldBe(1, "sessionGateHeldBeforeWorkspace");
            f.Effects.ShouldBe(0, "simultaneousAdmissions");
        }
        finally { release.TrySetResult(); }
        (await retire).ShouldBeTrue();
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(launch);
        refusal.Code.ShouldContain("workspace");
        f.Effects.ShouldBe(0);

        await using var inverse = new CompletedCardRunnerFixture();
        inverse.InstallGate();
        await inverse.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        var useEntered = NewBarrier(); var useRelease = NewBarrier();
        inverse.EffectBarrier = async () => { useEntered.SetResult(); await useRelease.Task; };
        var first = inverse.Runtime.StartAsync(inverse.Launch(Guid.NewGuid()), CancellationToken.None);
        await useEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = inverse.Retire();
        try { second.IsCompleted.ShouldBeFalse("retirement waits for real launch admission"); }
        finally { useRelease.TrySetResult(); }
        await Should.ThrowAsync<FenceEffectException>(first);
        await second;
        inverse.Effects.ShouldBe(1);
    }

    // PC-224: bypass explicit attach fence -> provider effect at the retiring path.
    [Test]
    public async Task C1032_FenceAttachOrders()
    {
        await using var f = new CompletedCardRunnerFixture();
        await using var fake = new FakeHerdrServer();
        var child = f.Child();
        var pane = fake.SeedDetectedAgent("fixture:p1", HerdrAgentKinds.Claude);
        pane.Cwd = f.Checkout;
        fake.SetPaneProcessInfo(pane.PaneId, 1, [(child.Id, "claude", new[] { "claude" }, f.Checkout)]);
        fake.Start(); await fake.WaitUntilListeningAsync();
        f.InstallGate();
        var runtime = f.NewRuntime(new HerdrClient(new() { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath }));
        f.Bind(runtime);
        await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        (await f.Retire()).ShouldBeTrue();
        var attach = new HerdrAttachRequest(Guid.NewGuid(), pane.PaneId, HerdrAgentKinds.Claude,
            TranscriptFormats.Claude, child.Id, "fixture");
        await Should.ThrowAsync<PhoneHomeAdmissionException>(runtime.AttachHerdrAsync(attach, CancellationToken.None));
        f.Effects.ShouldBe(0, "attachEffectsAtRetiringPath");
        child.HasExited.ShouldBeFalse();

        await using var inverse = new CompletedCardRunnerFixture();
        inverse.InstallGate();
        pane.Cwd = inverse.Checkout;
        fake.SetPaneProcessInfo(pane.PaneId, 1, [(child.Id, "claude", new[] { "claude" }, inverse.Checkout)]);
        var inverseRuntime = inverse.NewRuntime(new HerdrClient(new() { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath }));
        inverse.Bind(inverseRuntime);
        await inverseRuntime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        var entered = NewBarrier(); var release = NewBarrier();
        inverse.EffectBarrier = async () => { entered.SetResult(); await release.Task; };
        var first = inverseRuntime.AttachHerdrAsync(attach with { SessionId = Guid.NewGuid() }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = inverse.Retire();
        try { second.IsCompleted.ShouldBeFalse(); }
        finally { release.TrySetResult(); }
        await Should.ThrowAsync<FenceEffectException>(first);
        await second;
        inverse.Effects.ShouldBe(1);
    }

    // PC-225/231/232: publish census early, kill or hide contradictory retained child -> named witnesses fail.
    [Test]
    public async Task C1032_FenceStartupAdoptionOrders()
    {
        await using var f = new CompletedCardRunnerFixture();
        f.InstallGate();
        (await f.Retire()).ShouldBeFalse("cleanupAdmittedBeforeCensus");
        await f.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        (await f.Retire()).ShouldBeTrue();
        var child = f.Child(); var id = Guid.NewGuid(); f.SeedManifest(child, id);
        f.InstallGate(restart: true);
        var entered = NewBarrier(); var release = NewBarrier();
        f.EffectBarrier = async () => { entered.SetResult(); await release.Task; };
        var adoption = f.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            (await f.Retire()).ShouldBeFalse("cleanupAdmittedBeforeCensus");
            child.HasExited.ShouldBeFalse("ownedChildStillAlive");
            ((bool)f.Gate.HasOwnershipConflict(f.Checkout)).ShouldBeTrue("retainedOwnerVisible");
        }
        finally { release.TrySetResult(); }
        await Should.ThrowAsync<FenceEffectException>(adoption);
        child.HasExited.ShouldBeFalse();
        File.Exists(Antiphon.PtyHost.Protocol.PtyHostManifest.PathFor(f.Settings.PtyHostManifestDir, id)).ShouldBeTrue();
    }

    // PC-226/227: omit startup/pending Herdr ownership -> cleanup admits despite the surviving owner.
    [Test]
    public async Task C1032_FenceHerdrReadoptionOrders()
    {
        await using var f = new CompletedCardRunnerFixture();
        await using var endpoint = new FakeHerdrEndpoint();
        var child = f.Child(); var id = Guid.NewGuid(); f.SeedSidecar(child, id);
        f.InstallGate();
        var runtime = f.NewRuntime(new HerdrClient(new() { Enabled = true, Session = endpoint.Session,
            SocketPath = endpoint.Path, ConnectTimeoutMs = 250 }));
        runtime.CreationEffectForTest = null;
        f.Bind(runtime);
        await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        runtime.Get(id).Pending.ShouldBe(HerdrPendingReasons.Unreachable);
        (await f.Retire()).ShouldBeFalse("cleanupAdmittedDuringPendingAdoption");
        await using var fake = new FakeHerdrServer(endpoint: endpoint);
        var pane = fake.SeedDetectedAgent("fixture:p1", HerdrAgentKinds.Claude); pane.Cwd = f.Checkout;
        fake.SetPaneProcessInfo(pane.PaneId, 1, [(child.Id, "claude", new[] { "claude" }, f.Checkout)]);
        fake.Start(); await fake.WaitUntilListeningAsync();
        await runtime.SweepVanishedSessionsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        runtime.Get(id).Status.ShouldBe("Running");
        (await f.Retire()).ShouldBeFalse();
        child.HasExited.ShouldBeFalse();

        await using var conflict = new CompletedCardRunnerFixture();
        conflict.InstallGate(); await conflict.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        (await conflict.Retire()).ShouldBeTrue();
        var owner = conflict.Child(); conflict.SeedSidecar(owner, Guid.NewGuid());
        conflict.InstallGate(restart: true);
        var restarted = conflict.NewRuntime(new HerdrClient(new() { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath }));
        restarted.CreationEffectForTest = null; conflict.Bind(restarted);
        fake.SetPaneProcessInfo(pane.PaneId, 1, [(owner.Id, "claude", new[] { "claude" }, conflict.Checkout)]);
        await restarted.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        ((bool)conflict.Gate.HasOwnershipConflict(conflict.Checkout)).ShouldBeTrue("ownershipConflictVisible");
        owner.HasExited.ShouldBeFalse();
        restarted.List().Count.ShouldBe(1);
    }

    // PC-228: omit mirror admission -> Git creates at the retiring path; inverse creation holds retirement.
    [Test]
    public async Task C1032_FenceCreationOrders()
    {
        await using var f = new CompletedCardRunnerFixture();
        f.InstallGate(); await f.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        (await f.Retire()).ShouldBeTrue();
        var service = f.Workspace();
        await Should.ThrowAsync<PhoneHomeAdmissionException>(service.MirrorAsync(
            new("feat/card-task-deadbeef", f.Sha, "task-deadbeef"), CancellationToken.None));
        File.Exists(Path.Combine(f.Checkout, ".git")).ShouldBeFalse("mirrorCreatesAtRetiringPath");

        await using var inverse = new CompletedCardRunnerFixture();
        Directory.Delete(inverse.Checkout);
        inverse.InstallGate(); await inverse.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        var creating = inverse.Workspace();
        var entered = NewBarrier(); var release = NewBarrier();
        creating.GetType().GetProperty("WorkspaceAdmissionForTest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(creating, (Func<Task>)(async () => { entered.SetResult(); await release.Task; }));
        var first = creating.MirrorAsync(new("feat/card-task-deadbeef", inverse.Sha, "task-deadbeef"), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = inverse.Retire();
        try { second.IsCompleted.ShouldBeFalse(); }
        finally { release.TrySetResult(); }
        var mirror = await first; mirror.Path.ShouldBe(inverse.Checkout.Replace('\\', '/'));
        await second;
        CompletedCardRunnerFixture.Git(mirror.Path, "rev-parse", "HEAD").Trim().ShouldBe(inverse.Sha);
    }

    // PC-229: missing/torn/unreadable journal restored as open -> cleanup or a new start is admitted.
    [Test]
    public async Task C1032_FenceUnknownJournal()
    {
        foreach (var corruption in new[] { "missing", "torn", "unreadable" })
        {
            await using var f = new CompletedCardRunnerFixture();
            f.InstallGate(); await f.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
            (await f.Retire()).ShouldBeTrue();
            var file = Path.Combine(f.State, "journal.json");
            if (corruption == "torn") File.WriteAllText(file, "{");
            else { File.Delete(file); if (corruption == "unreadable") Directory.CreateDirectory(file); }
            f.InstallGate(restart: true);
            await f.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
            (await f.Retire()).ShouldBeFalse("cleanupAdmitted: " + corruption);
            await Should.ThrowAsync<PhoneHomeAdmissionException>(f.Runtime.StartAsync(f.Launch(Guid.NewGuid()), CancellationToken.None));
            f.Effects.ShouldBe(0);
            File.ReadAllText(Path.Combine(f.Root, "outside", "sentinel")).ShouldBe("preserve");
        }
    }

    private static TaskCompletionSource NewBarrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
