using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S4a test design, class LN (Windows-native, the desktop lane only: CP-25 in the native checkpoint
/// doc). Real Windows sharing semantics, long paths, junctions and 8.3 aliases against the policy's tick. Each
/// method skips off Windows; every child process is killed in finally.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)]
public sealed class C0824DesktopLegacyReleaseNativeTests
{
    /// <summary>N-1/G-66: a handle or working directory inside the tree defers the removal, never forces it.</summary>
    [Test]
    [Arguments("open-handle")]
    [Arguments("cwd")]
    public async Task Held_directory_defers_never_forces(string holder)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows sharing semantics: a Linux rename succeeds under an open handle");
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync();
        string script;
        string? cwd = null;
        if (holder == "open-handle")
        {
            // Ignored build output (the fixture ignores obj/, not bin/): an untracked file would hold the tree as
            // source_dirty before the open handle mattered.
            var held = Path.Combine(a.Path, "obj", "held.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(held)!);
            await File.WriteAllBytesAsync(held, [1, 2, 3]);
            script = $"$f=[IO.File]::Open('{held}','Open','Read','Read'); 'ready'; Start-Sleep -Seconds 120";
        }
        else
        {
            cwd = Path.Combine(a.Path, "node_modules", "x");
            Directory.CreateDirectory(cwd);
            script = "'ready'; Start-Sleep -Seconds 120";
        }
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);
        using var child = StartHolder(script, cwd);
        try
        {
            var tick = await w.TickAsync();

            var row = await w.RowAsync(tick.RunId, a);
            row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
            row.ReasonCode.ShouldBe("worktree_remove_failed");
            (await w.SnapshotAsync(a.Path)).ShouldBe(before, "every file is intact");
            (await w.RegisteredAsync(a.Path)).ShouldBeTrue();
            (await w.BranchExistsAsync(a)).ShouldBeTrue();
            w.Fixture.Git.Trace.ShouldNotContain(x => x.Contains("--force"));
            (await w.ReleasesAsync(a)).Single().Active.ShouldBeTrue();
            (await w.RetryCursorAsync(a))!.NotBefore.ShouldNotBeNull();
        }
        finally
        {
            Kill(child);
        }

        w.Clock.Advance(TimeSpan.FromMinutes(61));
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
        (await w.AttemptsAsync(a)).Count.ShouldBe(2);
    }

    /// <summary>N-2/G-67: a long ignored path never leaves an unregistered directory with bytes.</summary>
    [Test]
    public async Task Long_path_never_leaves_an_unregistered_orphan()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows MAX_PATH semantics");
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync();
        var deep = Path.Combine(a.Path, "obj");
        while (deep.Length < 300) deep = Path.Combine(deep, "segment-" + new string('d', 24));
        Directory.CreateDirectory(@"\\?\" + deep);
        await File.WriteAllTextAsync(@"\\?\" + Path.Combine(deep, "build.out"), "disposable\n");
        var origin = await w.OriginRefsAsync();

        await w.TickAsync();
        await AssertNoOrphanAsync(w, a);
        w.Clock.Advance(TimeSpan.FromMinutes(61));
        await w.TickAsync();
        await AssertNoOrphanAsync(w, a);

        // CARD-1714: the no-follow delete cannot open a path past MAX_PATH, so the removal refuses before the move.
        (await w.ReleasesAsync(a)).Single().LastReason.ShouldBe("worktree_path_too_long");
        Directory.Exists(a.Path).ShouldBeTrue("nothing was moved");
    }

    /// <summary>
    /// N-2b/G-25: an untracked (not ignored) file past MAX_PATH holds the tree. The clean proof must see it; a
    /// git status that skips the deep directory would release a tree with unsaved bytes.
    /// </summary>
    [Test]
    public async Task Long_path_untracked_file_holds_the_tree()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows MAX_PATH semantics");
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var control = await w.AddTaskAsync(ageHours: 60);
        var a = await w.AddTaskAsync();
        var deep = Path.Combine(a.Path, "src");
        while (deep.Length < 300) deep = Path.Combine(deep, "segment-" + new string('u', 24));
        var file = Path.Combine(deep, "unsaved.txt");
        file.Length.ShouldBeGreaterThan(260);
        Directory.CreateDirectory(@"\\?\" + deep);
        await File.WriteAllTextAsync(@"\\?\" + file, "unsaved\n");
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(control, origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe("source_dirty");
        await w.AssertIntactAsync(a, before);
        File.Exists(file).ShouldBeTrue("the untracked file is kept");
        (await w.BranchExistsAsync(a)).ShouldBeTrue("the local branch is kept");
    }

    /// <summary>
    /// CARD-1714 (N-2 shape): a deep IGNORED tree past MAX_PATH. git status cannot see it (ignored), so only the
    /// guarded removal's length guard stops it: the row is Held worktree_path_too_long with nothing moved, never a
    /// set-aside, unregistered tree stuck Partial at the no-follow delete.
    /// </summary>
    [Test]
    public async Task Long_path_ignored_tree_is_held_not_partial()
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows MAX_PATH semantics");
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var control = await w.AddTaskAsync(ageHours: 60);
        var a = await w.AddTaskAsync();
        var deep = Path.Combine(a.Path, "obj");
        while (deep.Length < 300) deep = Path.Combine(deep, "segment-" + new string('d', 24));
        Directory.CreateDirectory(@"\\?\" + deep);
        await File.WriteAllTextAsync(@"\\?\" + Path.Combine(deep, "build.out"), "disposable\n");
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(control, origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe("worktree_path_too_long");
        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue("the registration is intact");
        (await w.BranchExistsAsync(a)).ShouldBeTrue("the local branch is kept");
        Directory.Exists(Antiphon.Server.Infrastructure.Git.WorktreeSetAside.SetAsidePath(a.Path)).ShouldBeFalse("nothing was set aside");
    }

    /// <summary>N-3/G-68: a junction leaf, an ignored junction inside the tree and an 8.3 alias are refused.</summary>
    [Test]
    [Arguments("junction-leaf")]
    [Arguments("junction-inside")]
    [Arguments("short-name")]
    public async Task Windows_aliases_and_junctions_are_refused(string alias)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows junctions and 8.3 aliases");
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var control = await w.AddTaskAsync(ageHours: 60);
        var id = Guid.NewGuid();
        var id8 = id.ToString("N")[..8];
        var target = Path.Combine(w.Fixture.Root, "outside", "card-task-" + id8);
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "worktree", "add", "-b", "outside/" + id8, target, "master");
        await File.WriteAllTextAsync(Path.Combine(target, "marker.txt"), "outside\n");
        var targetBefore = await w.SnapshotAsync(target);
        var leaf = Path.Combine(w.Root, "card-task-" + id8);
        DesktopLegacyWorld.Handle a;
        string? junction = null;
        string reason;
        switch (alias)
        {
            case "junction-leaf":
                Junction(leaf, target);
                junction = leaf;
                a = await w.AddTaskAsync(id: id, shape: "none", path: leaf);
                reason = "reparse_point";
                break;
            case "junction-inside":
                a = await w.AddTaskAsync(id: id);
                Directory.CreateDirectory(Path.Combine(a.Path, "obj"));
                junction = Path.Combine(a.Path, "obj", "link");
                Junction(junction, target);
                reason = "ignored_reparse_point";
                break;
            default:
                var real = await w.AddTaskAsync(id: id);
                var shortName = ShortPath(real.Path);
                // A real 8.3 alias: Path.GetFullPath expands '~' names on Windows, so the path is not normalized and
                // G-38 refuses it as outside_root before the leaf is read. Only the synthesized fallback (8.3 names
                // disabled, a path that does not exist) stays normalized and reaches the leaf check.
                reason = "outside_root";
                if (string.Equals(shortName, real.Path, StringComparison.OrdinalIgnoreCase))
                {
                    shortName = Path.Combine(w.Root, "CARD-T~1");
                    reason = "leaf_mismatch";
                }
                await using (var db = w.Db())
                {
                    var row = db.AgentTasks.Single(t => t.Id == id);
                    row.WorktreePath = shortName;
                    await db.SaveChangesAsync();
                }
                a = real with { Path = shortName };
                break;
        }
        var origin = await w.OriginRefsAsync();
        try
        {
            var tick = await w.TickAsync();

            await w.AssertRetiredAsync(control, origin);
            (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe(reason);
            (await w.SnapshotAsync(target)).ShouldBe(targetBefore, "the target is intact");
            if (alias != "junction-inside")
                (await w.ReleasesAsync(a)).ShouldBeEmpty("no release");
        }
        finally
        {
            if (junction is not null && Directory.Exists(junction)) Directory.Delete(junction);
        }
    }

    private static async Task AssertNoOrphanAsync(DesktopLegacyWorld w, DesktopLegacyWorld.Handle a)
    {
        var registered = await w.RegisteredAsync(a.Path);
        (!registered && Directory.Exists(a.Path)).ShouldBeFalse("never registration gone and directory present");
    }

    private static Process StartHolder(string script, string? cwd)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            WorkingDirectory = cwd ?? Environment.CurrentDirectory,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) start.ArgumentList.Add(arg);
        var child = Process.Start(start) ?? throw new InvalidOperationException("holder did not start");
        var ready = child.StandardOutput.ReadLineAsync();
        if (!ready.Wait(TimeSpan.FromSeconds(20)) || ready.Result != "ready")
        {
            Kill(child);
            throw new TimeoutException("holder did not report ready");
        }
        return child;
    }

    private static void Kill(Process child)
    {
        try { if (!child.HasExited) child.Kill(entireProcessTree: true); child.WaitForExit(10_000); }
        catch (InvalidOperationException) { }
    }

    private static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        child.WaitForExit(20_000).ShouldBeTrue();
        child.ExitCode.ShouldBe(0, "mklink /J");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint size);

    private static string ShortPath(string path)
    {
        var buffer = new StringBuilder(1024);
        return GetShortPathName(path, buffer, (uint)buffer.Capacity) == 0 ? path : buffer.ToString();
    }
}
