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
        var script = Path.Combine(RepoRoot(), "scripts", "audit-land-verification-graph.ps1");
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c9fe-canary-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        try
        {
            await AssertKeptAsync(script, root, "repo-root", world => world.Repo);
            await AssertKeptAsync(script, root, "empty", _ => "");
            await AssertKeptAsync(script, root, "whitespace", _ => " \t ");
            await AssertKeptAsync(script, root, "parent", world => world.Parent);
            await AssertKeptAsync(script, root, "slash", _ => "/");
            await AssertKeptAsync(script, root, "outside", world => world.OutsideCanary);
            await AssertKeptAsync(script, root, "trailing", world => world.Canary + "/");
            await AssertKeptAsync(script, root, "uppercase", world => world.UpperCanary);
            await AssertKeptAsync(script, root, "newline", world => world.Canary + "\ninjected");
            await AssertSymlinkUnlinkedAsync(script, root);
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
        var script = Path.Combine(RepoRoot(), "scripts", "audit-land-verification-graph.ps1");
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c9fe-canary-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        try
        {
            var exact = CreateWorld(root, "exact");
            var exactSentinel = Path.Combine(exact.Canary, "sentinel");
            File.WriteAllText(exactSentinel, "keep");
            var removed = await RemoveRecordedCanaryAsync(script, exact.Canary, exact.Repo);
            removed.ExitCode.ShouldBe(0, removed.Text);
            File.Exists(exactSentinel).ShouldBeFalse(exactSentinel);
            Directory.Exists(exact.Repo).ShouldBeTrue(exact.Repo);

            var dotted = CreateWorld(root, "dotted");
            var dottedSentinel = Path.Combine(dotted.Canary, "sentinel");
            File.WriteAllText(dottedSentinel, "keep");
            var parentName = Path.GetFileName(dotted.Parent);
            var record = Path.Combine(dotted.Parent, "..", parentName, Path.GetFileName(dotted.Canary));
            record.ShouldContain("..");
            var normalized = await RemoveRecordedCanaryAsync(script, record, dotted.Repo);
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
        var original = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "scripts", "audit-land-verification-graph.ps1"));
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c9fe-guard-" + Guid.NewGuid().ToString("N")[..12])).FullName;
        try
        {
            await ProveGuardAsync(original, root, "name", world => world.UpperCanary, world => Path.Combine(world.UpperCanary, "sentinel"));
            await ProveGuardAsync(original, root, "parent", world => world.OutsideCanary, world => Path.Combine(world.OutsideCanary, "sentinel"));
            await ProveGuardAsync(original, root, "rebuilt", world => world.Canary + "/", world => Path.Combine(world.Canary, "sentinel"));
            await ProveGuardAsync(original, root, "repo-root", world => world.Repo, world => Path.Combine(world.Repo, "sentinel"), canaryIsRepo: true);
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
        var real = await RemoveRecordedCanaryAsync(
            Path.Combine(RepoRoot(), "scripts", "audit-land-verification-graph.ps1"), record(kept), kept.Repo);
        real.ExitCode.ShouldBe(0, guard + " real: " + real.Text);
        File.Exists(keptSentinel).ShouldBeTrue(guard + " real sentinel");

        var dropped = DisableGuard(original, guard);
        var copyDir = Path.Combine(root, "scripts-" + guard);
        Directory.CreateDirectory(copyDir);
        var copy = Path.Combine(copyDir, "audit-land-verification-graph.ps1");
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

    private static async Task<ToolRun> RemoveRecordedCanaryAsync(string script, string recorded, string repo)
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
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-RemoveRecordedCanary", record, "-RemoveRecordedCanaryRepo", repo })
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

    private readonly record struct CanaryWorld(string Parent, string Repo, string Canary, string UpperCanary, string OutsideCanary);
}
