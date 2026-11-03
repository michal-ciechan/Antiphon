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
/// through the real fail-closed /proc scan (V-49, V-49b, V-50). Review 12ba9a25 D-1: a family name carrying a line
/// break or any control character is unregistered and kept.
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

    [Test]
    [NativeLinuxOnly("CARD-1677 Linux-only by construction: real /tmp entries whose names carry a line break")]
    public async Task Family_name_with_a_line_break_is_unregistered_and_kept(CancellationToken ct)
    {
        // Review 12ba9a25 D-1. Every registered sample with a trailing \n, \r or \r\n, or a leading \n, as an old,
        // owned, unheld entry (claude-scratch switched on, its mirror absent): none is a candidate, every one is
        // reported unregistered with its line break escaped, and every byte stays. An exact name beside them goes.
        using var s = new Scratch();
        var variants = new List<string>();
        foreach (var family in RunnerTempSweep.RegisteredFamilies)
            foreach (var name in new[] { family.Sample + "\n", family.Sample + "\r", family.Sample + "\r\n", "\n" + family.Sample })
            {
                s.Entry(name, TimeSpan.FromHours(49));
                variants.Add(name);
            }
        s.Age(s.Tmp + "/" + RunnerTempSweep.ClaudeContainer, TimeSpan.FromHours(49));
        s.Age(s.Tmp + "/\n" + RunnerTempSweep.ClaudeContainer, TimeSpan.FromHours(49));
        var control = "c1514-" + Hex32();
        var controlPath = s.Entry(control, TimeSpan.FromHours(49));
        var flags = new Dictionary<string, bool> { [RunnerTempSweep.ClaudeScratch] = true };

        var response = await s.Sweep().SweepAsync(Request(families: flags), ct);

        Single(response, control).Verdict.ShouldBe(TempSweepCodes.Removed, "control: the sweep is live");
        Directory.Exists(controlPath).ShouldBeFalse();
        response.Candidates.ShouldNotContain(c => c.Name.Any(char.IsControl));
        response.Removed.ShouldBe(1);
        foreach (var name in variants)
        {
            var label = name.Replace("\r", "\\r").Replace("\n", "\\n");
            Directory.Exists(s.Tmp + "/" + name).ShouldBeTrue(label);
            Directory.EnumerateFiles(s.Tmp + "/" + name).Count().ShouldBe(3, label);
        }
        response.Unregistered.Sum(u => u.Count).ShouldBe(variants.Count);
        response.Unregistered.ShouldAllBe(u => !u.Prefix.Any(char.IsControl));
        response.Unregistered.ShouldContain(u => u.Prefix == @"c1514-<hex>\x0a");
        response.Unregistered.ShouldContain(u => u.Prefix == RunnerTempSweep.ClaudeContainer + @"/-work-worktrees-task-<hex>\x0a");
        s.Probed.ShouldBeEmpty("no mirror is probed for a name that is not a registered family");
    }

    [Test]
    [NativeLinuxOnly("CARD-1677 Linux-only by construction: real /tmp entries whose names carry control characters")]
    public async Task Control_character_name_is_refused_before_any_family_match(CancellationToken ct)
    {
        // Review 12ba9a25 D-1 (defence in depth). A family that would match anything, line breaks included, still never
        // sees a name carrying a control character: it is unregistered and kept. A plain name beside them goes.
        using var s = new Scratch();
        string[] names = ["ctl-a\nb", "ctl-tab\t", "ctl-\u0001", "ctl-\u007f", "ctl-end\n"];
        foreach (var name in names)
            s.Entry(name, TimeSpan.FromHours(500));
        var controlPath = s.Entry("ctl-plain", TimeSpan.FromHours(500));
        var broad = new TempFamily("broad", @"^[\s\S]+\z", null, null, "x");

        var response = await s.Sweep(families: [broad]).SweepAsync(Request(), ct);

        Single(response, "ctl-plain").Verdict.ShouldBe(TempSweepCodes.Removed, "control: the broad family is live");
        Directory.Exists(controlPath).ShouldBeFalse();
        response.Candidates.ShouldHaveSingleItem();
        foreach (var name in names)
            Directory.EnumerateFiles(s.Tmp + "/" + name).Count().ShouldBe(3, name.Replace("\n", "\\n"));
        response.Unregistered.Sum(u => u.Count).ShouldBe(names.Length);
        response.Unregistered.ShouldAllBe(u => !u.Prefix.Any(char.IsControl));
    }
}
