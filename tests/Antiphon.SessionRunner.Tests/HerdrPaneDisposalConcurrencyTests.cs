using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public sealed class HerdrPaneDisposalConcurrencyTests
{
    private static async Task Change(Func<HerdrDisposalObservation, HerdrDisposalObservation> mutation, bool occupied = false)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); if (occupied) h.Occupied();
        var p = await h.PreviewAsync(); h.Backend.Transform = mutation;
        var result = await h.Service.ExecuteAsync(h.Request(p), default);
        result.Outcome.ShouldBe("Refused"); result.Code.ShouldBe(HerdrProblemTypes.PaneChanged); h.Backend.Closes.ShouldBe(0);
        (await h.Client.PaneGetAsync(h.PaneId, default)).TerminalId.ShouldBe(p.TerminalId);
    }
    [Test] [Arguments(false)] [Arguments(true)]
    public Task C461_G036_Shell_snapshot_change(bool pid) => Change(o =>
    {
        var shell = pid ? o.Shell! with { Pid = 500 } : o.Shell! with { StartedAtUtc = o.Shell.StartedAtUtc!.Value.AddTicks(1) };
        return o with { Shell = shell, Affected = [shell] };
    });
    [Test] [Arguments(false)] [Arguments(true)]
    public Task C461_G037_Occupant_snapshot_change(bool pid) => Change(o =>
    {
        var p = pid ? o.Foreground![0] with { Pid = 500 } : o.Foreground![0] with { StartedAtUtc = o.Foreground[0].StartedAtUtc!.Value.AddTicks(1) };
        return o with { Foreground = [p], Affected = [o.Shell!, p] };
    }, true);
    [Test] public Task C461_G038_Native_snapshot_change() => Change(o =>
    {
        var p = o.Foreground![0] with { NativeSessionIds = [Guid.NewGuid()] };
        return o with { Foreground = [p], Affected = [o.Shell!, p] };
    }, true);
    [Test] public Task C461_G039_Pane_incarnation_change() => Change(o => o with { Pane = o.Pane with { TerminalId = "replacement" } });
    [Test] public Task C461_G040_Backend_instance_change() => Change(o => o with { Backend = o.Backend with { InstanceId = "replacement" } });
    [Test] [Arguments(false)] [Arguments(true)] public Task C461_G041_Placement_change(bool workspace) => Change(o => o with { Pane = workspace ? o.Pane with { WorkspaceId = "w999" } : o.Pane with { TabId = "w1:t999" } });

    [Test] public async Task C461_G064_Disposal_lease()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var p = await h.PreviewAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Backend.BeforeClose = async () => { entered.SetResult(); await release.Task; };
        var disposing = h.Service.ExecuteAsync(h.Request(p), default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var acquisition = h.Runtime.Placement.LockPaneAsync(h.PaneId, default);
        try { acquisition.IsCompleted.ShouldBeFalse(); }
        finally { release.SetResult(); }
        (await disposing).Outcome.ShouldBe("Closed"); await using var lease = await acquisition.WaitAsync(TimeSpan.FromSeconds(5));
    }
    [Test] public async Task C461_G074_Claims_rechecked_under_lease()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var p = await h.PreviewAsync();
        var lease = await h.Runtime.Placement.LockPaneAsync(h.PaneId, default);
        var operation = h.Service.ExecuteAsync(h.Request(p), default);
        await using var claim = h.Runtime.Placement.Claim(h.SessionId, "w1", p.TabId, h.PaneId, false);
        await lease.DisposeAsync();
        (await operation).Code.ShouldBe(HerdrProblemTypes.PaneBound); h.Backend.Closes.ShouldBe(0);
    }
    [Test] public async Task Different_panes_progress_independently()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var p = await h.PreviewAsync();
        var other = h.Fake.Workspaces.SelectMany(w => w.Tabs).SelectMany(t => t.Panes).First(pane => pane.PaneId != h.PaneId);
        other.Tokens = new() { ["antiphon-session"] = h.SessionId.ToString() };
        var second = await h.Service.PreviewAsync(new(other.PaneId, h.SessionId), default);
        await using var pinned = await h.Runtime.Placement.LockPaneAsync(h.PaneId, default);
        using var cancel = new CancellationTokenSource(); var blocked = h.Service.ExecuteAsync(h.Request(p), cancel.Token);
        try { (await h.Service.ExecuteAsync(h.Request(second), default).WaitAsync(TimeSpan.FromSeconds(5))).Outcome.ShouldBe("Closed"); blocked.IsCompleted.ShouldBeFalse(); }
        finally { cancel.Cancel(); }
        await Should.ThrowAsync<OperationCanceledException>(() => blocked);
    }
    [Test] public async Task C461_G117_Lease_release_on_failure()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var p = await h.PreviewAsync();
        h.Backend.BeforeInspect = _ => throw new OperationCanceledException();
        (await h.Service.ExecuteAsync(h.Request(p), default)).Outcome.ShouldBe("Refused");
        h.Backend.BeforeInspect = null;
        var next = await h.PreviewAsync();
        (await h.Service.ExecuteAsync(h.Request(next), default).WaitAsync(TimeSpan.FromSeconds(5))).Outcome.ShouldBe("Closed");
    }
    [Test] public Task Cancellation_releases_every_lease() => C461_G117_Lease_release_on_failure();

    private static RunnerLaunchRequest Launch(HerdrPaneDisposalFixture h, string? tab = null) => new(h.SessionId,
        @"C:\owned-test\claude.exe", ["--session-id", h.SessionId.ToString()], new Dictionary<string, string>(), h.Settings.SessionLogPath,
        Cols: 120, Rows: 30, Backend: SessionBackends.Herdr, Herdr: new("owned-test", "selected-workspace", h.Settings.SessionLogPath, "test", HerdrAgentKinds.Claude, TabLabel: tab));

    // Drive the production acquisition paths while another actor owns the disposal lease.
    private static async Task ActorWaits(string actor, bool trace = false)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        h.Fake.Workspaces[0].Tokens!["antiphon-ws"] = "owned-test";
        var tab = h.Fake.Workspaces[0].Tabs.Single(t => t.Panes.Any(p => p.PaneId == h.PaneId));
        var child = new HerdrPaneChild(h.Client, h.Settings, NullLogger.Instance,
            () => [new(h.SessionId, tab.TabId, h.PaneId, tab.Number)], new Probe(), h.Runtime.Placement);
        if (actor == "named") tab.Panes.RemoveAll(p => p.PaneId != h.PaneId);
        if (actor == "reuse") HerdrLastPane.FromSidecar(HerdrPaneDisposalServiceTests.Sidecar(h) with { Origin = HerdrPaneOrigins.Launched }, "test")
            .SaveAtomic(HerdrLastPane.PathFor(h.Settings.SessionLogPath, h.SessionId));
        if (actor is "attach" or "kill") h.Occupied("claude");
        if (actor == "kill") await child.AttachExistingAsync(HerdrPaneDisposalServiceTests.Sidecar(h), default);
        var held = await h.Runtime.Placement.LockPaneAsync(h.PaneId, default);
        var lockOrder = new List<string>();
        if (trace) h.Runtime.Placement.LockRequested += kind => lockOrder.Add(kind);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        Task task = actor switch
        {
            "attach" => child.AttachAsync(new(h.SessionId, h.PaneId, "claude", "claude-jsonl", 4243, "owned-test", ExpectedNativeSessionId: h.SessionId), cancellation.Token),
            "kill" => child.KillAsync(cancellation.Token),
            "named" => child.LaunchAsync(Launch(h, "selected-tab"), cancellation.Token),
            _ => child.LaunchAsync(Launch(h), cancellation.Token),
        };
        try
        {
            await Task.Delay(100);
            task.IsCompleted.ShouldBeFalse("the production actor must wait for the selected pane lease");
            h.Methods.ShouldNotContain("pane.send_text"); h.Methods.ShouldNotContain("pane.split"); h.Methods.ShouldNotContain("pane.close");
            File.Exists(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBeFalse();
        }
        finally { await held.DisposeAsync(); }
        await task.WaitAsync(TimeSpan.FromSeconds(8));
        if (trace) lockOrder.ShouldBe(["workspace-key", "workspace-id", "pane"]);
        if (actor != "kill") File.Exists(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBeTrue();
        if (actor is "reuse" or "named" or "attach")
        { child.PaneId.ShouldBe(h.PaneId); h.Methods.ShouldNotContain("pane.split"); }
        if (actor == "split") h.Methods.Count(m => m == "pane.split").ShouldBe(1);
        await child.DisposeAsync();
    }
    [Test] [Arguments(false)] [Arguments(true)] public async Task C461_G065_Attach_lease(bool disposalWins) { await ActorWaits("attach"); await RuntimeRace("attach", disposalWins); }
    [Test] [Arguments(false)] [Arguments(true)] public async Task C461_G066_Named_launch_lease(bool disposalWins) { await ActorWaits("named"); await RuntimeRace("named", disposalWins); }
    [Test] [Arguments(false)] [Arguments(true)] public async Task C461_G067_Last_pane_reuse_lease(bool disposalWins) { await ActorWaits("reuse"); await RuntimeRace("reuse", disposalWins); }
    [Test] public Task C461_G068_Allocator_split_lease() => ActorWaits("split");
    [Test] public Task C461_G070_Detach_kill_lease() => ActorWaits("kill");
    [Test] [Arguments(false)] [Arguments(true)] public Task C461_G071_Sidecar_publication_lease(bool disposalWins) => RuntimeRace("attach", disposalWins);
    private static async Task RuntimeRace(string actor, bool disposalWins)
    {
        await using var h = new HerdrPaneDisposalFixture(new Probe()); await h.StartAsync();
        h.Fake.Workspaces[0].Tokens!["antiphon-ws"] = "owned-test";
        var tab = h.Fake.Workspaces[0].Tabs.Single(t => t.Panes.Any(p => p.PaneId == h.PaneId));
        if (actor == "named") tab.Panes.RemoveAll(p => p.PaneId != h.PaneId);
        if (actor == "reuse") HerdrLastPane.FromSidecar(HerdrPaneDisposalServiceTests.Sidecar(h) with { Origin = HerdrPaneOrigins.Launched }, "test")
            .SaveAtomic(HerdrLastPane.PathFor(h.Settings.SessionLogPath, h.SessionId));
        if (actor == "attach") h.Occupied("claude");
        var p = await h.PreviewAsync(); p.Eligible.ShouldBeTrue();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<RunnerSessionDto> Acquire() => actor == "attach"
            ? h.Runtime.AttachHerdrAsync(new(h.SessionId, h.PaneId, "claude", "claude-jsonl", 4243, "owned-test", ExpectedNativeSessionId: h.SessionId), timeout.Token)
            : h.Runtime.StartAsync(Launch(h, actor == "named" ? "selected-tab" : null), timeout.Token);
        if (disposalWins)
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Backend.BeforeClose = async () => { entered.SetResult(); await release.Task; };
            var disposal = h.Service.ExecuteAsync(h.Request(p), timeout.Token); await entered.Task.WaitAsync(timeout.Token);
            var acquisition = Acquire();
            try { await Task.Delay(100); acquisition.IsCompleted.ShouldBeFalse(); }
            finally { release.TrySetResult(); }
            (await disposal).Outcome.ShouldBe("Closed");
            try
            {
                var acquired = await acquisition;
                h.Runtime.LiveHerdrPanes().Single(live => live.SessionId == acquired.SessionId).PaneId.ShouldNotBe(h.PaneId);
                await h.Runtime.KillAsync(h.SessionId, TimeSpan.FromSeconds(1), default);
            }
            catch (Exception ex) when (ex is HerdrLaunchException or HerdrApiException) { }
            h.Backend.Closes.ShouldBe(1);
        }
        else
        {
            var gate = h.Fake.GateMethod("pane.report_metadata");
            var acquisition = Acquire();
            for (var i = 0; i < 200 && !h.Methods.Contains("pane.report_metadata"); i++) await Task.Delay(10);
            if (acquisition.IsCompleted) await acquisition;
            h.Methods.ShouldContain("pane.report_metadata");
            var disposal = h.Service.ExecuteAsync(h.Request(p), timeout.Token);
            try { await Task.Delay(100); disposal.IsCompleted.ShouldBeFalse(); }
            finally { gate.Release(); }
            (await acquisition).Status.ShouldBe("Running");
            (await disposal).Outcome.ShouldBe("Refused"); h.Backend.Closes.ShouldBe(0);
            File.Exists(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBeTrue();
            await h.Runtime.KillAsync(h.SessionId, TimeSpan.FromSeconds(1), default);
        }
    }
    [Test] public async Task C461_G069_Pending_adoption_lease()
    {
        await using var h = new HerdrPaneDisposalFixture();
        HerdrPaneDisposalServiceTests.SaveLocator(h, "sidecar"); h.Occupied();
        await h.Runtime.AdoptOrphanedHostsAsync(new Probe(), default);
        h.Runtime.Get(h.SessionId).Pending.ShouldBe(HerdrPendingReasons.Unreachable);
        await h.StartAsync();
        var held = await h.Runtime.Placement.LockPaneAsync(h.PaneId, default);
        var adoption = h.Runtime.SweepVanishedSessionsAsync(new Probe(), default);
        try { await Task.Delay(100); adoption.IsCompleted.ShouldBeFalse(); }
        finally { await held.DisposeAsync(); }
        await adoption.WaitAsync(TimeSpan.FromSeconds(8));
        h.Runtime.Get(h.SessionId).Status.ShouldBe("Running");
    }
    [Test] public async Task C461_G072_Retirement_lease()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); h.Occupied();
        (HerdrPaneDisposalServiceTests.Sidecar(h) with { Origin = HerdrPaneOrigins.Launched }).SaveAtomic(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId));
        await h.Runtime.AdoptOrphanedHostsAsync(new Probe(), default);
        var held = await h.Runtime.Placement.LockPaneAsync(h.PaneId, default);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retirement = Task.Run(() => { started.SetResult(); return h.Runtime.SweepVanishedSessions(new DeadProbe()); });
        await started.Task;
        try
        {
            await Task.Delay(100); retirement.IsCompleted.ShouldBeFalse();
            File.Exists(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBeTrue();
            File.Exists(HerdrLastPane.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBeFalse();
        }
        finally { await held.DisposeAsync(); }
        await retirement.WaitAsync(TimeSpan.FromSeconds(5));
        File.Exists(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBeFalse();
        File.Exists(HerdrLastPane.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBeTrue();
    }
    private sealed class DeadProbe : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAt) => false;
        public string? TryGetProcessName(int pid) => null;
        public DateTime? TryGetStartTimeUtc(int pid) => null;
    }
    [Test] public async Task C461_G073_Lock_order()
    {
        await ActorWaits("named", trace: true);
        await C461_G064_Disposal_lease();
        await Different_panes_progress_independently();
    }
    internal sealed class Probe : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAt) => true;
        public string? TryGetProcessName(int pid) => "powershell";
        public DateTime? TryGetStartTimeUtc(int pid) => new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc);
    }
}
