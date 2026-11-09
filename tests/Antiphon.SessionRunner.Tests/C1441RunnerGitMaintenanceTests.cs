using System.Diagnostics;
using System.Globalization;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1441 (CARD-1432 F-1). A non-inspection runner git child overrides repository
/// <c>gc.auto</c> and <c>maintenance.auto</c> through the environment form of <c>-c</c>.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1441RunnerGitMaintenanceTests
{
    [Test]
    [Timeout(60_000)]
    public async Task C1441_non_inspection_git_reads_gc_auto_zero()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1441-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Git(root, "init", "-q");
            Git(root, "config", "gc.auto", "6700");
            Git(root, "config", "maintenance.auto", "true");
            Git(root, "config", "commit.gpgsign", "true");

            var gc = await Read(root, "gc.auto");
            gc.Output.ShouldBe("0", "the repository's own gc.auto=6700 must be overridden");
            gc.Arguments.ShouldBe(new[] { "config", "--get", "gc.auto" },
                "the override is environment config, so the argument vector stays the command");
            Last(gc.Start, "gc.auto").ShouldBe("0");
            Last(gc.Start, "maintenance.auto").ShouldBe("false");

            (await Read(root, "maintenance.auto")).Output.ShouldBe("false");
            (await Read(root, "commit.gpgsign")).Output.ShouldBe("true",
                "a repository value the override does not name still reads back");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ReadResult> Read(string root, string key)
    {
        ProcessStartInfo? seen = null;
        var result = await RunnerWorkspaceService.RunGitAsync(root, psi =>
        {
            seen = psi;
            return Process.Start(psi);
        }, TimeSpan.FromSeconds(15), CancellationToken.None, "config", "--get", key);
        result.ExitCode.ShouldBe(0, key + " " + result.Stderr);
        seen.ShouldNotBeNull();
        return new ReadResult(result.Stdout.Trim(), seen.ArgumentList.ToArray(), seen);
    }

    private static string? Last(ProcessStartInfo start, string key)
    {
        if (!start.Environment.TryGetValue("GIT_CONFIG_COUNT", out var text)
            || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || count < 0)
            return null;
        string? value = null;
        for (var i = 0; i < count; i++)
        {
            if (start.Environment["GIT_CONFIG_KEY_" + i.ToString(CultureInfo.InvariantCulture)] == key)
                value = start.Environment["GIT_CONFIG_VALUE_" + i.ToString(CultureInfo.InvariantCulture)];
        }
        return value;
    }

    private static void Git(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("git " + string.Join(' ', args) + " failed: " + stderr);
    }

    private sealed record ReadResult(string Output, string[] Arguments, ProcessStartInfo Start);
}
