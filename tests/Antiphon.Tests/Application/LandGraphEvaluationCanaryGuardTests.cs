using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    [Test]
    [Timeout(120_000)]
    public async Task RecordedCanaryDeleteRejectsUnsafeRecords()
    {
        var library = CanaryRemovalLibrary();
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c9fe-canary-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        try
        {
            await AssertKeptAsync(library, root, "repo-root", world => world.Repo);
            await AssertKeptAsync(library, root, "empty", _ => "");
            await AssertKeptAsync(library, root, "whitespace", _ => " \t ");
            await AssertKeptAsync(library, root, "parent", world => world.Parent);
            await AssertKeptAsync(library, root, "slash", _ => "/");
            await AssertKeptAsync(library, root, "outside", world => world.OutsideCanary);
            await AssertKeptAsync(library, root, "trailing", world => world.Canary + "/");
            await AssertKeptAsync(library, root, "uppercase", world => world.UpperCanary);
            await AssertKeptAsync(library, root, "newline", world => world.Canary + "\ninjected");
            await AssertSymlinkUnlinkedAsync(library, root);
        }
        finally
        {
            if (Directory.Exists(root)) StartRefGit.DeleteDirectory(root);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task RecordedCanaryDeleteRemovesOnlyTheExactCanary()
    {
        var library = CanaryRemovalLibrary();
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c9fe-canary-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        try
        {
            var exact = CreateWorld(root, "exact");
            var exactSentinel = Path.Combine(exact.Canary, "sentinel");
            File.WriteAllText(exactSentinel, "keep");
            var removed = await RemoveRecordedCanaryAsync(library, exact.Canary, exact.Repo);
            removed.ExitCode.ShouldBe(0, removed.Text);
            File.Exists(exactSentinel).ShouldBeFalse(exactSentinel);
            Directory.Exists(exact.Repo).ShouldBeTrue(exact.Repo);

            var dotted = CreateWorld(root, "dotted");
            var dottedSentinel = Path.Combine(dotted.Canary, "sentinel");
            File.WriteAllText(dottedSentinel, "keep");
            var parentName = Path.GetFileName(dotted.Parent);
            var record = Path.Combine(dotted.Parent, "..", parentName, Path.GetFileName(dotted.Canary));
            record.ShouldContain("..");
            var normalized = await RemoveRecordedCanaryAsync(library, record, dotted.Repo);
            normalized.ExitCode.ShouldBe(0, normalized.Text);
            File.Exists(dottedSentinel).ShouldBeFalse(dottedSentinel);
            Directory.Exists(dotted.Repo).ShouldBeTrue(dotted.Repo);
        }
        finally
        {
            if (Directory.Exists(root)) StartRefGit.DeleteDirectory(root);
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task EachCanaryGuardIsNecessary()
    {
        var original = await File.ReadAllTextAsync(CanaryRemovalLibrary());
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c9fe-guard-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        try
        {
            await ProveGuardAsync(original, root, "name", world => world.UpperCanary, world => Path.Combine(world.UpperCanary, "sentinel"));
            await ProveGuardAsync(original, root, "parent", world => world.OutsideCanary, world => Path.Combine(world.OutsideCanary, "sentinel"));
            await ProveGuardAsync(original, root, "rebuilt", world => world.Canary + "/", world => Path.Combine(world.Canary, "sentinel"));
            await ProveGuardAsync(original, root, "repo-root", world => world.Repo, world => Path.Combine(world.Repo, "sentinel"), canaryIsRepo: true);
            await ProveRepoSeparatorGuardAsync(original, root);
            await ProveRepoDirectoryGuardAsync(original, root);
        }
        finally
        {
            if (Directory.Exists(root)) StartRefGit.DeleteDirectory(root);
        }
    }

    private static async Task AssertKeptAsync(string script, string root, string label, Func<CanaryWorld, string> record)
    {
        var world = CreateWorld(root, label);
        var sentinels = new[]
        {
            Path.Combine(world.Canary, "sentinel"),
            Path.Combine(world.UpperCanary, "sentinel"),
            Path.Combine(world.OutsideCanary, "sentinel"),
            Path.Combine(world.Repo, "repo-sentinel"),
            Path.Combine(world.Parent, "parent-sentinel"),
        };
        foreach (var sentinel in sentinels)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            File.WriteAllText(sentinel, "keep");
        }

        var run = await RemoveRecordedCanaryAsync(script, record(world), world.Repo);
        run.ExitCode.ShouldBe(0, label + ": " + run.Text);
        foreach (var sentinel in sentinels) File.Exists(sentinel).ShouldBeTrue(label + ": " + sentinel);
        Directory.Exists(world.Repo).ShouldBeTrue(label);
        Directory.Exists("/").ShouldBeTrue();
    }

    private static async Task AssertSymlinkUnlinkedAsync(string script, string root)
    {
        var world = CreateWorld(root, "symlink", createCanary: false);
        var target = Path.Combine(root, "symlink-target");
        Directory.CreateDirectory(target);
        var sentinel = Path.Combine(target, "sentinel");
        await File.WriteAllTextAsync(sentinel, "keep");
        var name = "antiphon-landgraph-canary-" + Guid.NewGuid().ToString("N");
        var link = Path.Combine(world.Parent, name);
        await CreateDirectoryLinkAsync(link, target);
        var run = await RemoveRecordedCanaryAsync(script, link, world.Repo);
        run.ExitCode.ShouldBe(0, run.Text);
        File.Exists(sentinel).ShouldBeTrue(sentinel);
        Directory.Exists(target).ShouldBeTrue(target);
        Directory.Exists(link).ShouldBeFalse(link);
        (await File.ReadAllTextAsync(sentinel)).ShouldBe("keep");
    }

    private static async Task ProveGuardAsync(string original, string root, string guard,
        Func<CanaryWorld, string> record, Func<CanaryWorld, string> sentinelPath, bool canaryIsRepo = false)
    {
        var kept = CreateWorld(root, guard + "-real", canaryIsRepo: canaryIsRepo);
        var keptSentinel = sentinelPath(kept);
        Directory.CreateDirectory(Path.GetDirectoryName(keptSentinel)!);
        await File.WriteAllTextAsync(keptSentinel, "keep");
        var real = await RemoveRecordedCanaryAsync(CanaryRemovalLibrary(), record(kept), kept.Repo);
        real.ExitCode.ShouldBe(0, guard + " real: " + real.Text);
        File.Exists(keptSentinel).ShouldBeTrue(guard + " real sentinel");

        var dropped = DisableGuard(original, guard);
        var copyDir = Path.Combine(root, "scripts-" + guard);
        Directory.CreateDirectory(copyDir);
        var copy = Path.Combine(copyDir, "land-graph-canary-removal.ps1");
        await File.WriteAllTextAsync(copy, dropped);
        var victim = CreateWorld(root, guard + "-mutated", canaryIsRepo: canaryIsRepo);
        var victimSentinel = sentinelPath(victim);
        Directory.CreateDirectory(Path.GetDirectoryName(victimSentinel)!);
        await File.WriteAllTextAsync(victimSentinel, "keep");
        var mutated = await RemoveRecordedCanaryAsync(copy, record(victim), victim.Repo);
        mutated.ExitCode.ShouldBe(0, guard + " mutated: " + mutated.Text);
        File.Exists(victimSentinel).ShouldBeFalse(guard + " mutated sentinel must go when only this guard is removed");
    }

    private static string DisableGuard(string script, string guard)
    {
        var newline = script.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = script.Split(newline);
        var marker = "# guard:" + guard;
        var index = Array.FindIndex(lines, line => line.Contains(marker, StringComparison.Ordinal));
        index.ShouldBeGreaterThanOrEqualTo(0, marker);
        var original = lines[index];
        original.ShouldContain("if (", Case.Sensitive);
        lines[index] = "    if ($false) { return } " + marker;
        var mutated = string.Join(newline, lines);
        mutated.ShouldContain("if ($false) { return } " + marker, Case.Sensitive);
        mutated.ShouldNotContain(original, Case.Sensitive);
        return mutated;
    }

    private static CanaryWorld CreateWorld(string root, string label, bool canaryIsRepo = false, bool createCanary = true)
    {
        var parent = Path.Combine(root, label);
        Directory.CreateDirectory(parent);
        var name = "antiphon-landgraph-canary-" + Guid.NewGuid().ToString("N");
        var upper = Path.Combine(parent, "antiphon-landgraph-canary-" + Guid.NewGuid().ToString("N").ToUpperInvariant());
        Directory.CreateDirectory(upper);
        var outside = Path.Combine(root, label + "-outside", "antiphon-landgraph-canary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        string repo;
        string canary;
        if (canaryIsRepo)
        {
            repo = Path.Combine(parent, name);
            canary = repo;
        }
        else
        {
            repo = Path.Combine(parent, "repo");
            canary = Path.Combine(parent, name);
        }

        Directory.CreateDirectory(repo);
        if (createCanary && !canaryIsRepo) Directory.CreateDirectory(canary);
        return new CanaryWorld(parent, repo, canary, upper, outside);
    }

    private static async Task CreateDirectoryLinkAsync(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is IOException or UnauthorizedAccessException)
        {
            var psi = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi)!;
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            process.ExitCode.ShouldBe(0, error);
        }
    }

    private static string CanaryRemovalLibrary() =>
        Path.Combine(RepoRoot(), "scripts", "lib", "land-graph-canary-removal.ps1");

    private static async Task ProveRepoSeparatorGuardAsync(string original, string root)
    {
        var kept = await CreateInsideCanaryAsync(root, "sep-real");
        var real = await RemoveRecordedCanaryAsync(CanaryRemovalLibrary(), kept.Canary, kept.Repo + "/");
        real.ExitCode.ShouldBe(0, "separator real: " + real.Text);
        File.Exists(kept.Sentinel).ShouldBeTrue("a trailing separator must not delete a canary inside the repository");

        var copy = await WriteMutatedLibraryAsync(original, root, "repo-separator");
        var victim = await CreateInsideCanaryAsync(root, "sep-mutated");
        var mutated = await RemoveRecordedCanaryAsync(copy, victim.Canary, victim.Repo + "/");
        mutated.ExitCode.ShouldBe(0, "separator mutated: " + mutated.Text);
        File.Exists(victim.Sentinel).ShouldBeFalse("separator guard must be the only check stopping that delete");
    }

    private static async Task ProveRepoDirectoryGuardAsync(string original, string root)
    {
        var kept = await CreateSiblingCanaryAsync(root, "dir-real");
        var real = await RemoveRecordedCanaryAsync(CanaryRemovalLibrary(), kept.Canary, kept.MissingRepo);
        real.ExitCode.ShouldBe(0, "directory real: " + real.Text);
        File.Exists(kept.Sentinel).ShouldBeTrue("a missing repository directory must not delete the canary");

        var copy = await WriteMutatedLibraryAsync(original, root, "repo-directory");
        var victim = await CreateSiblingCanaryAsync(root, "dir-mutated");
        var mutated = await RemoveRecordedCanaryAsync(copy, victim.Canary, victim.MissingRepo);
        mutated.ExitCode.ShouldBe(0, "directory mutated: " + mutated.Text);
        File.Exists(victim.Sentinel).ShouldBeFalse("directory guard must be the only check stopping that delete");
    }

    [Test]
    [Timeout(120_000)]
    public async Task CanaryNamedFolderInsideTheRepositoryIsNeverDeleted()
    {
        var library = CanaryRemovalLibrary();
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c9fe-inside-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        try
        {
            var inside = await CreateInsideCanaryAsync(root, "inside");
            foreach (var repo in new[] { inside.Repo, inside.Repo + "/", inside.Repo + "\\" })
            {
                var run = await RemoveRecordedCanaryAsync(library, inside.Canary, repo);
                run.ExitCode.ShouldBe(0, repo + ": " + run.Text);
                File.Exists(inside.Sentinel).ShouldBeTrue(repo);
            }

            var sibling = await CreateSiblingCanaryAsync(root, "sibling");
            var missing = await RemoveRecordedCanaryAsync(library, sibling.Canary, sibling.MissingRepo);
            missing.ExitCode.ShouldBe(0, missing.Text);
            File.Exists(sibling.Sentinel).ShouldBeTrue(sibling.MissingRepo);

            var fileRepo = Path.Combine(Path.GetDirectoryName(sibling.Canary)!, "repo-file");
            await File.WriteAllTextAsync(fileRepo, "not a directory");
            var asFile = await RemoveRecordedCanaryAsync(library, sibling.Canary, fileRepo);
            asFile.ExitCode.ShouldBe(0, asFile.Text);
            File.Exists(sibling.Sentinel).ShouldBeTrue(fileRepo);
            Directory.Exists(inside.Repo).ShouldBeTrue(inside.Repo);
        }
        finally
        {
            if (Directory.Exists(root)) StartRefGit.DeleteDirectory(root);
        }
    }

    private static async Task<InsideCanary> CreateInsideCanaryAsync(string root, string label)
    {
        var repo = Directory.CreateDirectory(Path.Combine(root, label, "repo")).FullName;
        var canary = Directory.CreateDirectory(Path.Combine(repo, "antiphon-landgraph-canary-" + Guid.NewGuid().ToString("N"))).FullName;
        var sentinel = Path.Combine(canary, "sentinel");
        await File.WriteAllTextAsync(sentinel, "keep");
        return new InsideCanary(repo, canary, sentinel);
    }

    private static async Task<SiblingCanary> CreateSiblingCanaryAsync(string root, string label)
    {
        var parent = Directory.CreateDirectory(Path.Combine(root, label)).FullName;
        var canary = Directory.CreateDirectory(Path.Combine(parent, "antiphon-landgraph-canary-" + Guid.NewGuid().ToString("N"))).FullName;
        var sentinel = Path.Combine(canary, "sentinel");
        await File.WriteAllTextAsync(sentinel, "keep");
        return new SiblingCanary(Path.Combine(parent, "missing-repo"), canary, sentinel);
    }

    private static async Task<string> WriteMutatedLibraryAsync(string original, string root, string guard)
    {
        var copyDir = Path.Combine(root, "lib-" + guard);
        Directory.CreateDirectory(copyDir);
        var copy = Path.Combine(copyDir, "land-graph-canary-removal.ps1");
        await File.WriteAllTextAsync(copy, DisableGuard(original, guard));
        return copy;
    }

    private static async Task<ToolRun> RemoveRecordedCanaryAsync(string library, string recorded, string repo)
    {
        var record = Path.Combine(Path.GetTempPath(), "antiphon-landgraph-canary-record-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(record, recorded);
        try
        {
            var psi = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.Environment["CANARY_LIBRARY"] = library;
            psi.Environment["CANARY_RECORD"] = record;
            psi.Environment["CANARY_REPO"] = repo;
            foreach (var arg in new[]
                     {
                         "-NoProfile", "-NonInteractive", "-Command",
                         "$ErrorActionPreference = 'Stop'; . $env:CANARY_LIBRARY; Remove-RecordedCanary -RecordPath $env:CANARY_RECORD -RepositoryRoot $env:CANARY_REPO",
                     })
                psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi)!;
            var text = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ToolRun(process.ExitCode, text);
        }
        finally
        {
            if (File.Exists(record)) File.Delete(record);
        }
    }

    private readonly record struct InsideCanary(string Repo, string Canary, string Sentinel);

    private readonly record struct SiblingCanary(string MissingRepo, string Canary, string Sentinel);

    private readonly record struct CanaryWorld(string Parent, string Repo, string Canary, string UpperCanary, string OutsideCanary);
}
