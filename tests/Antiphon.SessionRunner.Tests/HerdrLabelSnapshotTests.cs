using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class HerdrLabelSnapshotTests
{
    [Test]
    public async Task Failed_persistence_does_not_publish_or_advance_cached_labels()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        var saves = 0; f.Child.BeforeLabelFileReplace = _ => { if (++saves >= 2) throw new IOException("result replace fault"); };
        await f.FollowAsync(); f.Child.Sidecar!.TabLabel.ShouldBe("Old"); f.Saved.TabLabel.ShouldBe("Old");
        (await f.ReadAsync()).ShouldBeNull(); f.Saved.LabelFollow!.Observation.ShouldBeNull();
        f.Child.BeforeLabelFileReplace = null; f.Clock.Advance(TimeSpan.FromHours(1)); await f.FollowAsync();
        f.Child.Sidecar!.TabLabel.ShouldBe("New"); f.Saved.TabLabel.ShouldBe("New");
    }

    [Test]
    public async Task Crash_after_sidecar_commit_recovers_without_stale_publication()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        f.Child.LabelFollowBoundary = (name, _) => name == "after-result-file" ? Task.FromException(new OperationCanceledException("crash")) : Task.CompletedTask;
        await f.FollowAsync(); f.Saved.TabLabel.ShouldBe("New"); f.Child.Sidecar!.TabLabel.ShouldBe("Old");
        (await f.ReadAsync()).ShouldBeNull(); await f.RecreateChildAsync(); (await f.ReadAsync()).ShouldBeNull();
        f.Clock.Advance(TimeSpan.FromHours(1)); await f.FollowAsync(); (await f.ReadAsync())!.TabLabel.ShouldBe("New");
    }

    [Test]
    public async Task Concurrent_snapshot_saves_use_distinct_temp_files()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        using var arrived = new CountdownEvent(2); using var release = new ManualResetEventSlim();
        var names = new System.Collections.Concurrent.ConcurrentBag<string>();
        void Before(string path) { names.Add(path); arrived.Signal(); release.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue(); }
        var one = Task.Run(() => f.Binding.SaveAtomic(f.Path, Before));
        var two = Task.Run(() => (f.Binding with { TabLabel = "alternate" }).SaveAtomic(f.Path, Before));
        try { arrived.Wait(TimeSpan.FromSeconds(2)).ShouldBeTrue(); names.Distinct().Count().ShouldBe(2); }
        finally { release.Set(); await Task.WhenAll(one, two); }
        f.Saved.TabLabel.ShouldBeOneOf("Old", "alternate");
    }

    [Test]
    [Arguments("retirement", false)][Arguments("retirement", true)]
    [Arguments("stop", false)][Arguments("stop", true)]
    [Arguments("detach", false)][Arguments("detach", true)]
    [Arguments("adoption", false)][Arguments("adoption", true)]
    [Arguments("replacement", false)][Arguments("replacement", true)]
    [Arguments("disposal", false)][Arguments("disposal", true)]
    public async Task Observer_coordinates_with_existing_pane_actors(string actor, bool observerFirst)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        await using var runtime = await f.AdoptRuntimeAsync();
        await using var successor = new HerdrPaneChild(f.Client, f.Settings, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            () => [], new HerdrLabelFollowFixture.DenyProcesses(), f.Coordinator);
        var owns = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Observe() => f.Child.FollowLabelsAsync(f.Clock, () => owns, CancellationToken.None);
        async Task Act()
        {
            if (actor == "stop") { await f.Child.KillAsync(CancellationToken.None); return; }
            if (actor == "detach")
            {
                await successor.AttachExistingAsync(f.Saved with { Origin = HerdrPaneOrigins.Attached }, CancellationToken.None);
                await successor.KillAsync(CancellationToken.None); return;
            }
            if (actor == "disposal")
            {
                var backend = new HerdrDisposalBackend(f.Client, runtime, new(f.Settings.SessionLogPath), new HerdrPaneDisposalFixture.TestProcesses());
                var service = new HerdrPaneDisposalService(runtime, f.Settings.SessionLogPath, f.Clock, backend);
                var preview = await service.PreviewAsync(new(f.Binding.PaneId, f.Binding.SessionId), CancellationToken.None);
                preview.Eligible.ShouldBeFalse("an actively bound pane is never a disposal candidate");
                (await service.ExecuteAsync(new(Guid.NewGuid(), preview.PreviewId, "owned fixture", "antiphon-best-effort"), CancellationToken.None)).Outcome.ShouldBe("Refused");
                f.Methods.ShouldNotContain("pane.close"); return;
            }
            await using var lease = await f.Coordinator.LockPaneAsync(f.Binding.PaneId, CancellationToken.None);
            entered.TrySetResult(); if (!observerFirst) await release.Task;
            if (actor == "retirement") f.Child.RaiseVerifiedClosed();
            else
            {
                owns = false;
                if (actor == "replacement") (f.Binding with { AcceptedStartedAt = f.Binding.AcceptedStartedAt!.Value.AddSeconds(1), TabLabel = "replacement" }).SaveAtomic(f.Path);
                await successor.AttachExistingAsync(f.Saved, CancellationToken.None);
            }
        }
        var getterGate = observerFirst ? f.Fake.GateMethod("tab.get") : null;
        var actorMethod = actor == "stop" ? "pane.close" : "pane.report_metadata";
        var actorGate = !observerFirst && actor is "stop" or "detach" ? f.Fake.GateMethod(actorMethod) : null;
        Task? observing = null, acting = null;
        try
        {
            if (observerFirst)
            {
                observing = Observe(); await HerdrLabelFollowFixture.WaitAsync(() => f.GetterCount == 1);
                acting = Act();
                if (actor == "disposal") await acting; else acting.IsCompleted.ShouldBeFalse("actor waits for observer publication");
            }
            else
            {
                acting = Act();
                if (actor == "disposal") await acting;
                else if (actorGate is not null) await HerdrLabelFollowFixture.WaitAsync(() => f.Methods.Contains(actorMethod));
                else await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                observing = Observe();
                if (actor != "disposal")
                {
                    await HerdrLabelFollowFixture.WaitAsync(() => f.Saved.LabelFollow!.Sequence == 1);
                    f.GetterCount.ShouldBe(0); observing.IsCompleted.ShouldBeFalse("observer waits for the pane actor");
                }
            }
            await using var unrelated = await f.Coordinator.LockPaneAsync("unrelated", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            release.TrySetResult(); getterGate?.Release(); actorGate?.Release();
            if (observing is not null) await observing; if (acting is not null) await acting;
        }
        if (actor is "retirement" or "stop" or "detach") File.Exists(f.Path).ShouldBeFalse();
        else if (actor == "replacement") { f.Saved.TabLabel.ShouldBe("replacement"); successor.Sidecar!.TabLabel.ShouldBe("replacement"); }
        else if (actor == "adoption") { successor.Sidecar!.TabLabel.ShouldBe(observerFirst ? "New" : "Old"); f.Saved.TabLabel.ShouldBe(successor.Sidecar.TabLabel); }
        else f.Saved.TabLabel.ShouldBe("New");
        if (actor == "retirement") HerdrLastPane.TryLoad(f.Settings.SessionLogPath, f.Binding.SessionId)!.TabLabel.ShouldBe(observerFirst ? "New" : "Old");
        if (actor != "disposal") (await f.Child.ReadLabelObservationAsync(f.Clock, () => owns, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Runtime_disposal_cancels_and_joins_its_observer()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        await using var runtime = await f.AdoptRuntimeAsync();
        var gate = f.Fake.GateMethod("tab.get"); var reading = runtime.GetAsync(f.Binding.SessionId, CancellationToken.None);
        try
        {
            await HerdrLabelFollowFixture.WaitAsync(() => f.GetterCount == 1);
            await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            (await reading.WaitAsync(TimeSpan.FromSeconds(2))).LabelObservation.ShouldBeNull();
            f.Saved.TabLabel.ShouldBe("Old"); runtime.List().ShouldBeEmpty();
        }
        finally { gate.Drop(); await reading; }
    }

    [Test][Arguments("exit-first")][Arguments("follow-first")][Arguments("replacement")]
    public async Task Retirement_and_refresh_cannot_resurrect_or_revert_sidecar(string order)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        if (order == "follow-first")
        {
            await f.FollowAsync(); await using var lease = await f.Coordinator.LockPaneAsync(f.Binding.PaneId, CancellationToken.None);
            f.Child.RaiseVerifiedClosed();
            HerdrLastPane.TryLoad(f.Settings.SessionLogPath, f.Binding.SessionId)!.TabLabel.ShouldBe("New");
        }
        else
        {
            var lease = await f.Coordinator.LockPaneAsync(f.Binding.PaneId, CancellationToken.None);
            var run = f.FollowAsync();
            try
            {
                await HerdrLabelFollowFixture.WaitAsync(() => f.Saved.LabelFollow!.Sequence == 1);
                if (order == "replacement") (f.Binding with { AcceptedStartedAt = f.Binding.AcceptedStartedAt!.Value.AddSeconds(1), TabLabel = "replacement" }).SaveAtomic(f.Path);
                else f.Child.RaiseVerifiedClosed();
            }
            finally { await lease.DisposeAsync(); await run; }
        }
        if (order == "replacement") f.Saved.TabLabel.ShouldBe("replacement"); else File.Exists(f.Path).ShouldBeFalse();
        (await f.ReadAsync()).ShouldBeNull();
    }

    [Test]
    public async Task Observer_preserves_lock_order()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); var order = new List<string>();
        f.Coordinator.LockRequested += order.Add; await f.FollowAsync();
        order.ShouldBe(new[] { "workspace-key", "workspace-id", "pane" });
    }

    [Test]
    public async Task Blocked_observation_does_not_block_unrelated_runtime_reads()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync(); await using var runtime = await f.AdoptRuntimeAsync();
        var gate = f.Fake.GateMethod("tab.get"); var task = runtime.GetAsync(f.Binding.SessionId, CancellationToken.None);
        try
        {
            await HerdrLabelFollowFixture.WaitAsync(() => f.GetterCount == 1);
            var reads = Task.Run(() => { runtime.List().Count.ShouldBe(1); runtime.Get(f.Binding.SessionId).Status.ShouldBe("Running"); });
            await reads.WaitAsync(TimeSpan.FromSeconds(2));
            (await runtime.GetAsync(f.Binding.SessionId, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2))).LabelObservation.ShouldBeNull();
        }
        finally { gate.Release(); await task; }
    }

    [Test]
    [Arguments("matching")][Arguments("absent")][Arguments("session")][Arguments("generation")][Arguments("legacy")]
    [Arguments("workspace")][Arguments("tab")][Arguments("pane")][Arguments("attached")]
    public async Task Last_pane_refresh_requires_exact_generation_and_binding(string arm)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        var last = HerdrLastPane.FromSidecar(f.Binding, "reason");
        last = arm switch {
            "session" => last with { SessionId = Guid.NewGuid() }, "generation" => last with { AcceptedStartedAt = last.AcceptedStartedAt!.Value.AddSeconds(1) },
            "legacy" => last with { AcceptedStartedAt = null }, "workspace" => last with { WorkspaceId = "other" }, "tab" => last with { TabId = "other" },
            "pane" => last with { PaneId = "other" }, "attached" => last with { Origin = HerdrPaneOrigins.Attached }, _ => last };
        var path = HerdrLastPane.PathFor(f.Settings.SessionLogPath, f.Binding.SessionId);
        if (arm != "absent") last.SaveAtomic(path);
        var before = File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
        await f.FollowAsync();
        if (arm == "matching") HerdrLastPane.TryLoad(path)!.TabLabel.ShouldBe("New");
        else if (arm == "absent") File.Exists(path).ShouldBeFalse();
        else (await File.ReadAllBytesAsync(path)).ShouldBe(before);
    }

    [Test]
    public async Task Last_pane_refresh_preserves_retirement_metadata()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        var last = HerdrLastPane.FromSidecar(f.Binding, "original") with { ExitedAtUtc = DateTime.UnixEpoch };
        var path = HerdrLastPane.PathFor(f.Settings.SessionLogPath, f.Binding.SessionId); last.SaveAtomic(path);
        await f.FollowAsync(); var after = HerdrLastPane.TryLoad(path)!;
        (after with { TabLabel = last.TabLabel, WorkspaceLabel = last.WorkspaceLabel }).ShouldBe(last);
    }

    [Test]
    public async Task Last_pane_write_failure_is_repaired_without_new_observation()
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        var path = HerdrLastPane.PathFor(f.Settings.SessionLogPath, f.Binding.SessionId);
        HerdrLastPane.FromSidecar(f.Binding, "original").SaveAtomic(path);
        f.Child.BeforeLastPaneFileReplace = _ => throw new IOException("last-pane replace fault");
        await f.FollowAsync(); f.Saved.LabelFollow!.LastPaneRepairPending.ShouldBeTrue(); f.Saved.TabLabel.ShouldBe("New");
        HerdrLastPane.TryLoad(path)!.TabLabel.ShouldBe("Old"); var reads = f.GetterCount;
        await f.RecreateChildAsync(); await f.FollowAsync(); f.GetterCount.ShouldBe(reads);
        HerdrLastPane.TryLoad(path)!.TabLabel.ShouldBe("New"); f.Saved.LabelFollow.LastPaneRepairPending.ShouldBeFalse(); f.Saved.LabelFollow.Sequence.ShouldBe(1);
    }

    [Test][Arguments(false)][Arguments(true)]
    public async Task Crash_after_last_pane_replace_reconstructs_and_clears_only_owned_repair_debt(bool replace)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        var path = HerdrLastPane.PathFor(f.Settings.SessionLogPath, f.Binding.SessionId);
        var last = HerdrLastPane.FromSidecar(f.Binding, "original exit") with { ExitedAtUtc = DateTime.UnixEpoch };
        last.SaveAtomic(path);
        f.Child.LabelFollowBoundary = (name, _) => name == "after-repair-file"
            ? Task.FromException(new OperationCanceledException("crash after second file commit")) : Task.CompletedTask;
        await f.FollowAsync();
        f.Saved.LabelFollow!.LastPaneRepairPending.ShouldBeTrue();
        var written = HerdrLastPane.TryLoad(path)!;
        written.TabLabel.ShouldBe("New"); written.WorkspaceLabel.ShouldBe("New workspace");
        (written with { TabLabel = last.TabLabel, WorkspaceLabel = last.WorkspaceLabel }).ShouldBe(last);
        var state = f.Saved.LabelFollow; var getters = f.GetterCount;
        if (replace)
            (written with { AcceptedStartedAt = written.AcceptedStartedAt!.Value.AddSeconds(1), TabLabel = "replacement" }).SaveAtomic(path);
        var bytes = await File.ReadAllBytesAsync(path);
        await f.RecreateChildAsync();
        f.Saved.LabelFollow.LastPaneRepairPending.ShouldBeTrue(); (await f.ReadAsync()).ShouldBeNull();
        var start = f.Methods.Length;
        await f.FollowAsync(); f.AssertReadOnly(start); f.GetterCount.ShouldBe(getters);
        f.Saved.LabelFollow.ShouldBe(state with { LastPaneRepairPending = false });
        f.Child.Sidecar!.ShouldBe(f.Saved); (await File.ReadAllBytesAsync(path)).ShouldBe(bytes);
        (await f.ReadAsync()).ShouldBeNull("local repair does not reauthorize a pre-restart observation");
    }

    [Test][Arguments(false)][Arguments(true)]
    public async Task Repair_marker_survives_failure_and_respects_replacement(bool replace)
    {
        await using var f = new HerdrLabelFollowFixture(); await f.StartAsync();
        var path = HerdrLastPane.PathFor(f.Settings.SessionLogPath, f.Binding.SessionId);
        var last = HerdrLastPane.FromSidecar(f.Binding, "original"); last.SaveAtomic(path);
        f.Child.BeforeLastPaneFileReplace = _ => throw new IOException("last-pane replace fault");
        await f.FollowAsync(); await f.FollowAsync(); f.Saved.LabelFollow!.LastPaneRepairPending.ShouldBeTrue();
        if (replace) (last with { AcceptedStartedAt = last.AcceptedStartedAt!.Value.AddSeconds(1), TabLabel = "replacement" }).SaveAtomic(path);
        f.Child.BeforeLastPaneFileReplace = null; await f.FollowAsync();
        HerdrLastPane.TryLoad(path)!.TabLabel.ShouldBe(replace ? "replacement" : "New");
        f.Saved.LabelFollow.LastPaneRepairPending.ShouldBeFalse(); f.GetterCount.ShouldBe(2);
    }
}
