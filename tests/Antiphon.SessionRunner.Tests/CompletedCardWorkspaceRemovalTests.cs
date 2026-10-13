using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), Category("Slow")]
[NotInParallel("SessionLiveness")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class CompletedCardWorkspaceRemovalTests
{
    // PC-223/230: bypass normalization or take session before workspace -> concurrent entry or held session lock.
    [Test]
    public async Task C1032_FenceStartOrders()
    {
        await using var f = new CompletedCardRunnerFixture();
        f.InstallGate();
        await f.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        var entered = NewBarrier(); var release = NewBarrier();
        var command = f.Command();
        var retirementAdmissions = 0;
        var retire = f.Retire(command, async () =>
        {
            Interlocked.Increment(ref retirementAdmissions);
            using var intent = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(f.State, "journal.json")));
            var persisted = intent.RootElement.GetProperty("intents")[0].GetProperty("command");
            persisted.GetProperty("operationId").GetGuid().ShouldBe(command.OperationId);
            persisted.GetProperty("binding").GetProperty("bindingId").GetGuid().ShouldBe(f.Binding.BindingId);
            entered.SetResult(); await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var id = Guid.NewGuid();
        var launch = f.Runtime.StartAsync(f.Launch(id, Path.Combine(f.Checkout, "..", "task-deadbeef")), CancellationToken.None);
        try
        {
            var sessionGateHeldBeforeWorkspace = f.Runtime.LaunchGateForTest(id).CurrentCount == 0;
            sessionGateHeldBeforeWorkspace.ShouldBeFalse();
            var simultaneousAdmissions = f.Effects + Volatile.Read(ref retirementAdmissions);
            simultaneousAdmissions.ShouldBe(1);
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
        var runtime = f.NewRuntime(new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath }));
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
        var inverseRuntime = inverse.NewRuntime(new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath }));
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

        // PC-224/cwd-changed: the pane moves after read-only inspect. Skipping the fresh
        // cwd comparison would persist an attached sidecar outside the admitted workspace.
        await using var moved = new CompletedCardRunnerFixture();
        moved.InstallGate();
        var movedRuntime = moved.NewRuntime(new HerdrClient(new HerdrSettings
            { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath }));
        movedRuntime.CreationEffectForTest = null;
        moved.Bind(movedRuntime);
        await movedRuntime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        pane.Cwd = moved.Checkout;
        fake.SetPaneProcessInfo(pane.PaneId, 1, [(child.Id, "claude", new[] { "claude" }, moved.Checkout)]);
        var reads = 0;
        fake.BeforeRequest = method =>
        {
            if (method == "pane.process_info" && Interlocked.Increment(ref reads) == 2)
                fake.SetPaneProcessInfo(pane.PaneId, 1, [(child.Id, "claude", new[] { "claude" }, f.Checkout)]);
        };
        var movedId = Guid.NewGuid();
        var changed = await Should.ThrowAsync<PhoneHomeAdmissionException>(
            movedRuntime.AttachHerdrAsync(attach with { SessionId = movedId }, CancellationToken.None));
        changed.Code.ShouldBe("workspace_owner_changed");
        reads.ShouldBe(2);
        File.Exists(HerdrPaneSidecar.PathFor(moved.Settings.SessionLogPath, movedId)).ShouldBeFalse();
        movedRuntime.List().ShouldBeEmpty();
        child.HasExited.ShouldBeFalse();
        fake.BeforeRequest = null;
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
        f.Runtime.GetType().GetProperty("WorkspaceCensusForTest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(f.Runtime, (Func<Task>)(async () => { entered.SetResult(); await release.Task; }));
        var adoption = f.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var earlyCleanup = f.Retire();
        try
        {
            earlyCleanup.IsCompleted.ShouldBeTrue("an unfinished census gives an immediate hold");
            var cleanupAdmittedBeforeCensus = await earlyCleanup;
            cleanupAdmittedBeforeCensus.ShouldBeFalse();
            child.HasExited.ShouldBeFalse("ownedChildStillAlive");
        }
        finally { release.TrySetResult(); await adoption; await earlyCleanup; }
        child.WaitForExit(100).ShouldBeFalse("ownedChildStillAlive");
        ((bool)f.Gate.HasOwnershipConflict(f.Checkout)).ShouldBeTrue("retainedOwnerVisible");
        f.Runtime.Get(id).Pending.ShouldBe("workspace_ownership_conflict");
        (await f.Retire()).ShouldBeFalse();
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
        var runtime = f.NewRuntime(new HerdrClient(new HerdrSettings { Enabled = true, Session = endpoint.Session,
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
        var adopting = NewBarrier(); var finishAdoption = NewBarrier();
        runtime.BeforePendingHerdrPublicationAsync = async () => { adopting.SetResult(); await finishAdoption.Task; };
        var pendingSweep = runtime.SweepVanishedSessionsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        await adopting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pendingCleanup = f.Retire();
        try { pendingCleanup.IsCompleted.ShouldBeFalse("retirement must wait behind pending readoption admission"); }
        finally { finishAdoption.TrySetResult(); await pendingSweep; await pendingCleanup; }
        (await pendingCleanup).ShouldBeFalse();
        runtime.Get(id).Status.ShouldBe("Running");
        (await f.Retire()).ShouldBeFalse();
        child.HasExited.ShouldBeFalse();

        await using var conflict = new CompletedCardRunnerFixture();
        conflict.InstallGate(); await conflict.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        (await conflict.Retire()).ShouldBeTrue();
        var owner = conflict.Child(); var conflictId = Guid.NewGuid(); conflict.SeedSidecar(owner, conflictId);
        conflict.InstallGate(restart: true);
        var restarted = conflict.NewRuntime(new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath }));
        restarted.CreationEffectForTest = null; conflict.Bind(restarted);
        fake.SetPaneProcessInfo(pane.PaneId, 1, [(owner.Id, "claude", new[] { "claude" }, conflict.Checkout)]);
        await restarted.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        ((bool)conflict.Gate.HasOwnershipConflict(conflict.Checkout)).ShouldBeTrue("ownershipConflictVisible");
        owner.WaitForExit(100).ShouldBeFalse();
        restarted.Get(conflictId).Pending.ShouldBe("workspace_ownership_conflict");
        restarted.List().Count.ShouldBe(1);
    }

    // PC-228: omit mirror admission -> an existing binding replays at the retiring path;
    // inverse binding replay holds retirement admission before any Git inspection.
    [Test]
    public async Task C1032_FenceCreationOrders()
    {
        await using var f = new CompletedCardRunnerFixture();
        f.InstallGate(); await f.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        (await f.Retire()).ShouldBeTrue();
        var service = f.Workspace();
        var starts = f.GitStarts;
        var before = File.ReadAllBytes(Path.Combine(f.Checkout, ".git"));
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(service.MirrorAsync(f.MirrorRequest, CancellationToken.None));
        refusal.Code.ShouldBe("workspace_retiring_or_unknown");
        (f.GitStarts - starts).ShouldBe(0, "mirrorCreatesAtRetiringPath");
        File.ReadAllBytes(Path.Combine(f.Checkout, ".git")).ShouldBe(before);

        await using var inverse = new CompletedCardRunnerFixture();
        inverse.InstallGate(); await inverse.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        var creating = inverse.Workspace();
        var entered = NewBarrier(); var release = NewBarrier();
        creating.GetType().GetProperty("WorkspaceAdmissionForTest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(creating, (Func<Task>)(async () => { entered.SetResult(); await release.Task; }));
        var first = creating.MirrorAsync(inverse.MirrorRequest, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = inverse.Retire();
        try { second.IsCompleted.ShouldBeFalse(); }
        finally { release.TrySetResult(); }
        var mirror = await first; mirror.Path.ShouldBe(inverse.Checkout.Replace('\\', '/'));
        mirror.Binding!.BindingId.ShouldBe(inverse.Binding.BindingId);
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
