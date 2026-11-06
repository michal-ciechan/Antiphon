using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C0824SlotScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0824 S1 CP-3 (linux-native). The real /proc scan defers a release, then quarantines.</summary>
// CARD-1722: n=5 Linux per-case p95 <= 4.109s; max(60s Git floor, 2*p95).
// Full measured table: 2026-11-06-ab37b2cf-git-timeout-budgets plan; Windows rc collection pending.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)]
public sealed class C0824WorkspaceSlotProcessScanTests
{
    [Test]
    [NativeLinuxOnly("CARD-0824 Linux-only by construction: /proc process scan")]
    public async Task C0824_CwdHolderDefersThenQuarantines(CancellationToken ct)
    {
        // V-5a. A process with its cwd in the slot defers three times, then the directory is quarantined.
        using var scratch = Create();
        var pool = scratch.Pool(processes: new ProcWorkspaceSlotProcessProbe());
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        using var holder = Child("sleep", ["30"], a.Path);
        try
        {
            await DefersThenQuarantines(scratch, pool, a, "sleep");
        }
        finally
        {
            Stop(holder);
        }
    }

    [Test]
    [NativeLinuxOnly("CARD-0824 Linux-only by construction: /proc process scan")]
    public async Task C0824_OpenFdHolderDefersThenQuarantines(CancellationToken ct)
    {
        // V-5b. A process with an open descriptor under the slot and its cwd outside it.
        using var scratch = Create();
        var pool = scratch.Pool(processes: new ProcWorkspaceSlotProcessProbe());
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        Plant(a.Path, "held.log", "held");
        using var holder = Child("tail", ["-f", a.Path + "/held.log"], scratch.Root);
        try
        {
            await WaitForHolderAsync(a.Path, ct);
            await DefersThenQuarantines(scratch, pool, a, "tail");
        }
        finally
        {
            Stop(holder);
        }
    }

    [Test]
    [NativeLinuxOnly("CARD-0824 Linux-only by construction: /proc process scan")]
    public async Task C0824_HolderExitsBeforeRetryReleasesNormally(CancellationToken ct)
    {
        // V-5c. A holder that exits before the next attempt: the release completes with no quarantine.
        using var scratch = Create();
        var pool = scratch.Pool(processes: new ProcWorkspaceSlotProcessProbe());
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        var holder = Child("sleep", ["30"], a.Path);
        (await Release(pool, a, TaskA)).Outcome.ShouldBe("deferred");
        Stop(holder);
        holder.Dispose();
        scratch.Clock.Advance(TimeSpan.FromMinutes(1));
        await pool.MaintainAsync(ct);
        var ledger = pool.Ledger();
        ledger.Slot("ws-01")!.State.ShouldBe(WorkspaceSlotStates.Free);
        ledger.Slot("ws-01")!.Path.ShouldBe(a.Path);
        ledger.QuarantinedDirectories.ShouldBeEmpty();
    }

    private static async Task DefersThenQuarantines(C0824SlotScratch scratch, RunnerWorkspaceSlotPool pool, WorkspaceSlotLease a, string argv0)
    {
        var first = await Release(pool, a, TaskA);
        first.Outcome.ShouldBe("deferred", "Deferred(1)");
        pool.Ledger().Slot("ws-01")!.LingerAttempts.ShouldBe(1);
        foreach (var (minutes, attempt) in new[] { (1, 2), (2, 3) })
        {
            scratch.Clock.Advance(TimeSpan.FromMinutes(minutes));
            await pool.MaintainAsync(CancellationToken.None);
            pool.Ledger().Slot("ws-01")!.LingerAttempts.ShouldBe(attempt);
            pool.Ledger().QuarantinedDirectories.ShouldBeEmpty();
        }

        scratch.Clock.Advance(TimeSpan.FromMinutes(4));
        await pool.MaintainAsync(CancellationToken.None);
        var entry = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem("quarantined after attempt 4");
        entry.Reason.ShouldBe("lingering");
        entry.Error.ShouldContain("argv0=" + argv0);
        entry.Error.ShouldContain("pid=");
        Directory.Exists(a.Path).ShouldBeTrue();
        var next = await pool.LeaseAsync(scratch.RequestB(), CancellationToken.None);
        next.Path.ShouldBe(scratch.Slot("ws-01-d2"));
    }

    private static async Task WaitForHolderAsync(string path, CancellationToken ct)
    {
        var probe = new ProcWorkspaceSlotProcessProbe();
        for (var i = 0; i < 100 && probe.Holders(path).Count == 0; i++)
            await Task.Delay(50, ct);
    }

    private static Process Child(string file, string[] args, string cwd)
    {
        var psi = new ProcessStartInfo { FileName = file, WorkingDirectory = cwd, UseShellExecute = false };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        return Process.Start(psi)!;
    }

    private static void Stop(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        process.WaitForExit(10_000);
    }
}
