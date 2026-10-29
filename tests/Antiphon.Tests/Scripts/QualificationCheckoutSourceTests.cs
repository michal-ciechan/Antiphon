using System.Diagnostics;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// RC qualification checkouts keep /.antiphon-nightly-owned and
// /.antiphon-release-owned. git clean -fdx excludes those two paths, and
// checkpoint source counts every porcelain row. Unignored, they are dirty=2
// and the repeat-script rows refuse the clone they were launched from.
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class QualificationCheckoutSourceTests
{
    private static readonly string[] RootMarkers =
    [
        ".antiphon-nightly-owned",
        ".antiphon-release-owned",
    ];

    [Test]
    public async Task C885_OwnershipMarkers_are_ignored_at_the_checkout_root()
    {
        var root = DockerStackDocuments.RepoRoot;
        var listed = await Git(root, 0, "check-ignore", "--no-index", "-v", "--",
            RootMarkers[0], RootMarkers[1], "nested/.antiphon-nightly-owned");
        var paths = listed.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t').Last())
            .ToArray();
        paths.ShouldBe(RootMarkers, "c885-root-markers-ignored");
    }

    [Test]
    public async Task C885_MarkedAutocrlfClone_stays_checkpoint_clean()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "c885-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            await Git(scratch, 0, "init", "-q");
            await Git(scratch, 0, "config", "core.autocrlf", "true");
            await Git(scratch, 0, "config", "user.email", "checkpoint@example.invalid");
            await Git(scratch, 0, "config", "user.name", "Checkpoint Test");
            await Git(scratch, 0, "config", "commit.gpgsign", "false");
            File.Copy(Path.Combine(DockerStackDocuments.RepoRoot, ".gitignore"), Path.Combine(scratch, ".gitignore"));
            await Git(scratch, 0, "add", "--", ".gitignore");
            await Git(scratch, 0, "commit", "-qm", "seed");

            foreach (var marker in RootMarkers)
                await File.WriteAllTextAsync(Path.Combine(scratch, marker), "{\"kind\":\"marker\"}\n");
            Directory.CreateDirectory(Path.Combine(scratch, "nested"));
            await File.WriteAllTextAsync(Path.Combine(scratch, "nested", ".antiphon-nightly-owned"), "{\"kind\":\"nested\"}\n");

            var clean = await Porcelain(scratch);
            clean.ShouldBe(["nested/.antiphon-nightly-owned"], "c885-markers-are-not-source");

            await File.WriteAllTextAsync(Path.Combine(scratch, "stray.txt"), "stray\n");
            var dirty = await Porcelain(scratch);
            dirty.ShouldBe(["nested/.antiphon-nightly-owned", "stray.txt"], "c885-real-dirt-still-refused");
        }
        finally
        {
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }
    }

    private static async Task<string[]> Porcelain(string root)
    {
        var output = await Git(root, 0, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none");
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(record => record.Length >= 3 ? record[3..] : record)
            .OrderBy(path => path, StringComparison.Ordinal)
            .ToArray();
    }

    private static async Task<string> Git(string root, int expect, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var key in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" })
            start.Environment.Remove(key);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }

        process.ExitCode.ShouldBe(expect, "c885-git: " + string.Join(" ", arguments) + "\n" + await stderr + await stdout);
        return (await stdout).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
    }
}
