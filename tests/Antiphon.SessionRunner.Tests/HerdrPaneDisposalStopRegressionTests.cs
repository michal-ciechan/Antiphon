using System.Diagnostics;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[NotInParallel("SessionLiveness")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class HerdrPaneDisposalStopRegressionTests
{
    [Test] public Task C461_G053_Ordinary_attached_kill() => new HerdrPaneChildKillTests().Attached_kill_detaches_without_pane_close_or_pid_kill();
    [Test] public Task C461_G054_Attached_orphan_guard() => new HerdrAdoptionSweepTests().R20_attached_orphan_is_dropped_not_killed();
    [Test] public Task C461_G057_Detach_metadata_clear() => new HerdrAttachTests().Attached_kill_detaches();
    [Test] public Task C461_G058_Detach_sidecar_remove() => new HerdrAttachTests().Attached_kill_detaches();
    [Test] public Task C461_G059_Detach_reason() => new HerdrAttachTests().Attached_kill_detaches();
    [Test] public Task C461_G060_Attached_no_last_pane() => new HerdrAdoptionSweepTests().R21_attached_exit_leaves_no_last_pane();
    [Test] public Task C461_G061_Attached_not_allocator_slot() => new HerdrAttachTests().Attached_pane_is_not_an_allocator_slot();
    [Test] public Task C461_G062_Launched_kill_foreign_guard() => new HerdrPaneChildKillTests().Foreign_foreground_process_kills_our_child_by_pid_leaves_pane_open_and_returns_true();
    [Test] public Task C461_G055_Attached_pending_guard() => AttachedGuard(true);
    [Test] public Task C461_G056_Attached_failed_bar_guard() => AttachedGuard(false);
    private static async Task AttachedGuard(bool pending)
    {
        await using var h = new HerdrPaneDisposalFixture();
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /q /k")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var dummy = Process.Start(start)!;
        try
        {
            var sidecar = HerdrPaneDisposalServiceTests.Sidecar(h) with { ChildPid = dummy.Id, ChildStartedAtUtc = dummy.StartTime.ToUniversalTime(), LaunchedAtUtc = dummy.StartTime.ToUniversalTime() };
            sidecar.SaveAtomic(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId));
            h.Fake.SetPaneProcessInfo(h.PaneId, 4242, (dummy.Id, "grok.exe"));
            if (!pending) await h.StartAsync();
            await h.Runtime.AdoptOrphanedHostsAsync(new HerdrPaneDisposalConcurrencyTests.Probe(), default);
            if (pending)
            {
                await h.Runtime.KillAsync(h.SessionId, TimeSpan.FromSeconds(1), default);
                h.Runtime.Get(h.SessionId).ExitReason.ShouldBe(HerdrExitReasons.Detached);
            }
            else
            {
                var live = h.Runtime.LiveHerdrPanes().Single();
                h.Fake.SetPaneProcessInfo(h.PaneId, 4242, Array.Empty<(int, string)>());
                (await live.Session.VerifyHerdrLivenessAsync(h.Client, default)).ShouldBeFalse();
            }
            dummy.HasExited.ShouldBeFalse(); h.Methods.ShouldNotContain("pane.close");
            File.Exists(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBeFalse();
            File.Exists(HerdrLastPane.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBeFalse();
        }
        finally { if (!dummy.HasExited) { dummy.Kill(entireProcessTree: true); await dummy.WaitForExitAsync(); } }
    }
    /// <summary>
    /// Stop of a pending session waits for the pane lease that adoption holds. When adoption wins
    /// the lease, Stop used to see a cleared _pendingReason and return with the session Running and
    /// zero detach RPCs — silently leaving it alive. Stop must re-evaluate and finish the live stop.
    /// </summary>
    [Test] public async Task C461_G075_Stop_completes_when_adoption_wins_the_pane_lease()
    {
        var probe = new HerdrPaneDisposalConcurrencyTests.Probe();
        await using var h = new HerdrPaneDisposalFixture(probe);
        HerdrPaneDisposalServiceTests.SaveLocator(h, "sidecar"); h.Occupied();
        // Herdr is unreachable (fake not listening yet) but the child is OS-alive: R6 pending.
        await h.Runtime.AdoptOrphanedHostsAsync(probe, default);
        h.Runtime.Get(h.SessionId).Pending.ShouldBe(HerdrPendingReasons.Unreachable);
        await h.StartAsync();

        // Park adoption inside the bar, holding the pane lease, and race Stop against it.
        var gate = h.Fake.GateMethod("pane.get");
        var adoption = h.Runtime.SweepVanishedSessionsAsync(probe, default);
        for (var i = 0; i < 500 && !h.Methods.Contains("pane.get"); i++) await Task.Delay(10);
        h.Methods.ShouldContain("pane.get", "adoption never reached the gated bar RPC");

        var stop = h.Runtime.KillAsync(h.SessionId, TimeSpan.FromSeconds(1), default);
        await Task.Delay(100);
        stop.IsCompleted.ShouldBeFalse("Stop must wait for the pane lease adoption holds");

        gate.Release();
        await adoption.WaitAsync(TimeSpan.FromSeconds(10));
        var stopped = await stop.WaitAsync(TimeSpan.FromSeconds(10));

        stopped.Status.ShouldBe("Exited");
        stopped.ExitReason.ShouldBe(HerdrExitReasons.Detached);
        h.Methods.Count(m => m == "pane.report_metadata")
            .ShouldBeGreaterThan(0, "the adopted session must be detached, not abandoned Running");
        h.Runtime.Get(h.SessionId).Status.ShouldBe("Exited");
        File.Exists(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId)).ShouldBeFalse();
    }

    [Test] public async Task Stop_then_fresh_preview_is_the_only_explicit_close()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); h.Occupied("claude");
        await h.Runtime.AttachHerdrAsync(new(h.SessionId, h.PaneId, "claude", "claude-jsonl", 4243, "owned-test", ExpectedNativeSessionId: h.SessionId), default);
        var bound = await h.PreviewAsync(); bound.Eligible.ShouldBeFalse();
        var stopped = await h.Runtime.KillAsync(h.SessionId, TimeSpan.FromSeconds(2), default);
        stopped.ExitReason.ShouldBe(HerdrExitReasons.Detached); h.Methods.ShouldNotContain("pane.close");
        // Ordinary detach intentionally removed the association. Native-only current evidence
        // is still valid for explicit disposal of this now rowless occupant.
        var p = await h.Service.PreviewAsync(new(h.PaneId, null, h.SessionId), default); p.Eligible.ShouldBeTrue();
        (await h.Service.ExecuteAsync(h.Request(p), default)).Outcome.ShouldBe("Closed");
        h.Runtime.Get(h.SessionId).ExitReason.ShouldBe(HerdrExitReasons.Detached);
    }
}
