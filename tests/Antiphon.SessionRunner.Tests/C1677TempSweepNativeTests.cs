using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C1677TempSweepTests;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1677 S2 CP-8 (linux-native). A real symlinked temp entry is refused and its target kept, an inner
/// symlink is unlinked without following it, and a real child whose cwd is in an old family entry keeps it
/// through the real fail-closed /proc scan (V-49, V-49b, V-50).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
public sealed class C1677TempSweepNativeTests
{
    [Test]
    [NativeLinuxOnly("CARD-1677 Linux-only by construction: a real symlinked /tmp entry")]
    public async Task Symlinked_entry_is_refused_and_its_target_kept(CancellationToken ct)
    {
        // V-49 (G-99).
        using var s = new Scratch();
        var target = s.Root + "/outside";
        Directory.CreateDirectory(target);
        File.WriteAllText(target + "/keep.txt", "keep");
        s.Age(target, TimeSpan.FromHours(100));
        var name = "c487-probe-" + Hex32();
        Directory.CreateSymbolicLink(s.Tmp + "/" + name, target);
        var response = await s.Sweep().SweepAsync(Request(), ct);
        var candidate = Single(response, name);
        candidate.Verdict.ShouldBe(TempSweepCodes.Kept);
        candidate.Reason.ShouldBe(TempSweepCodes.Symlink);
        new DirectoryInfo(s.Tmp + "/" + name).LinkTarget.ShouldBe(target);
        File.ReadAllText(target + "/keep.txt").ShouldBe("keep");
    }

    [Test]
    [NativeLinuxOnly("CARD-1677 Linux-only by construction: a real symlink inside a swept /tmp entry")]
    public async Task Inner_symlink_is_not_followed(CancellationToken ct)
    {
        // V-49b (G-100). The entry goes; the link is unlinked itself and the outside file stays.
        using var s = new Scratch();
        var outside = s.Root + "/outside";
        Directory.CreateDirectory(outside);
        File.WriteAllText(outside + "/keep.txt", "keep");
        s.Age(outside, TimeSpan.FromHours(100));
        var name = "c1514-" + Hex32();
        var path = s.Entry(name, TimeSpan.FromHours(49));
        Directory.CreateSymbolicLink(path + "/link", outside);
        s.Age(path, TimeSpan.FromHours(49));
        var response = await s.Sweep().SweepAsync(Request(), ct);
        var candidate = Single(response, name);
        candidate.Verdict.ShouldBe(TempSweepCodes.Removed, candidate.Reason + " " + candidate.Detail);
        Directory.Exists(path).ShouldBeFalse();
        File.ReadAllText(outside + "/keep.txt").ShouldBe("keep");
    }

    [Test]
    [NativeLinuxOnly("CARD-1677 Linux-only by construction: a real /proc cwd holder of a /tmp entry")]
    public async Task Real_cwd_holder_keeps_the_entry(CancellationToken ct)
    {
        // V-50 (G-88, real probe).
        using var s = new Scratch();
        var name = "antiphon-c514-pty-" + Hex32();
        var path = s.Entry(name, TimeSpan.FromHours(49));
        var psi = new ProcessStartInfo { FileName = "sleep", WorkingDirectory = path, UseShellExecute = false };
        psi.ArgumentList.Add("30");
        using var child = Process.Start(psi)!;
        try
        {
            var probe = new ProcWorkspaceSlotProcessProbe();
            for (var i = 0; i < 100 && probe.Scan(path).Holders.Count == 0; i++)
                await Task.Delay(50, ct);
            var response = await s.Sweep(holders: probe).SweepAsync(Request(), ct);
            KeptIntact(response, name, TempSweepCodes.LiveProcess, path);
            Single(response, name).Holders.ShouldNotBeNull().ShouldContain(h => h.Pid == child.Id && h.Kind == "cwd");
        }
        finally
        {
            try { child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            child.WaitForExit(10_000);
        }
    }
}
