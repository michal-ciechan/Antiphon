using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C0824SlotScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0824 S3 CP-2 (D-7). An incomplete holder scan is lingering: the release defers.
/// <see cref="Rm_lock_defers_release"/> is the Windows Restart Manager witness and skips on Linux.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)]
public sealed class C0824WindowsSlotHolderTests
{
    [Test]
    public async Task Incomplete_scan_defers_release(CancellationToken ct)
    {
        // A probe that cannot prove the scan is complete must not look holder-free.
        using var scratch = Create();
        var pool = scratch.Pool(processes: new ProcWorkspaceSlotProcessProbe("/proc", false));
        var lease = await pool.LeaseAsync(scratch.RequestA(), ct);
        var response = await Release(pool, lease, TaskA);
        response.Outcome.ShouldBe("deferred");
        pool.Ledger().Slot("ws-01")!.Detail.ShouldContain("scan incomplete");

        using var windows = Create();
        var calls = 0;
        var windowsPool = windows.Pool(processes: new WindowsWorkspaceSlotProcessProbe(_ =>
        {
            calls++;
            return new HolderScan(false, []);
        }));
        var second = await windowsPool.LeaseAsync(windows.RequestA(), ct);
        (await Release(windowsPool, second, TaskA)).Outcome.ShouldBe("deferred");
        calls.ShouldBeGreaterThan(0);

        if (!OperatingSystem.IsWindows())
            new WindowsWorkspaceSlotProcessProbe().Scan(second.Path).Complete.ShouldBeFalse();
        var source = ProbeSource();
        source.Contains("RmShutdown", StringComparison.Ordinal).ShouldBeFalse();
        source.Contains("RmGetList", StringComparison.Ordinal).ShouldBeTrue();
        source.Contains("RmEndSession", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Test]
    public async Task Rm_lock_defers_release(CancellationToken ct)
    {
        // Real RmGetList is UNOBSERVED on this Linux runner. The desktop runs this one method at S4.
        NativeWindowsGate.Require(
            "CARD-0824 Windows Restart Manager file lock; the real probe runs on the desktop at S4");
        using var scratch = Create();
        var pool = scratch.Pool(processes: new WindowsWorkspaceSlotProcessProbe());
        var lease = await pool.LeaseAsync(scratch.RequestA(), ct);
        var held = Path.Combine(lease.Path, "held.bin");
        await File.WriteAllTextAsync(held, "held", ct);
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = lease.Path,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"$f = [IO.File]::Open('{held}', 'Open', 'Read', 'None'); Start-Sleep -Seconds 120");
        using var child = Process.Start(start)!;
        try
        {
            var response = await Release(pool, lease, TaskA);
            response.Outcome.ShouldBe("deferred");
        }
        finally
        {
            try { child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }
    }

    private static string ProbeSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        dir.ShouldNotBeNull();
        return File.ReadAllText(Path.Combine(dir!.FullName, "src/Antiphon.SessionRunner/WindowsWorkspaceSlotProcessProbe.cs"));
    }
}
