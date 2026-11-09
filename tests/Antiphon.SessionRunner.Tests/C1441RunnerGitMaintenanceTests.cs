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
    public async Task C1441_non_inspection_git_reads_gc_auto_zero(CancellationToken ct)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1441-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Git(root, "init", "-q");
            Git(root, "config", "gc.auto", "6700");
            Git(root, "config", "maintenance.auto", "true");
            Git(root, "config", "commit.gpgsign", "true");

            var gc = await Read(root, "gc.auto", ct);
            gc.Output.ShouldBe("0", "the repository's own gc.auto=6700 must be overridden");
            gc.Arguments.ShouldBe(new[] { "config", "--get", "gc.auto" },
                "the override is environment config, so the argument vector stays the command");
            Last(gc.Start, "gc.auto").ShouldBe("0");
            Last(gc.Start, "maintenance.auto").ShouldBe("false");

            (await Read(root, "maintenance.auto", ct)).Output.ShouldBe("false");
            (await Read(root, "commit.gpgsign", ct)).Output.ShouldBe("true",
                "a repository value the override does not name still reads back");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void C1441_append_preserves_existing_entries()
    {
        var start = new ProcessStartInfo("git");
        start.Environment["GIT_CONFIG_COUNT"] = "2";
        start.Environment["GIT_CONFIG_KEY_0"] = "commit.gpgSign";
        start.Environment["GIT_CONFIG_VALUE_0"] = "false";
        start.Environment["GIT_CONFIG_KEY_1"] = "core.hooksPath";
        start.Environment["GIT_CONFIG_VALUE_1"] = "/none";
        start.ArgumentList.Add("status");

        RunnerGitProcess.DisableAutoMaintenance(start);

        start.Environment["GIT_CONFIG_COUNT"].ShouldBe("4");
        start.Environment["GIT_CONFIG_KEY_0"].ShouldBe("commit.gpgSign");
        start.Environment["GIT_CONFIG_VALUE_0"].ShouldBe("false");
        start.Environment["GIT_CONFIG_KEY_1"].ShouldBe("core.hooksPath");
        start.Environment["GIT_CONFIG_VALUE_1"].ShouldBe("/none");
        start.Environment["GIT_CONFIG_KEY_2"].ShouldBe("gc.auto");
        start.Environment["GIT_CONFIG_VALUE_2"].ShouldBe("0");
        start.Environment["GIT_CONFIG_KEY_3"].ShouldBe("maintenance.auto");
        start.Environment["GIT_CONFIG_VALUE_3"].ShouldBe("false");
        start.ArgumentList.ToArray().ShouldBe(new[] { "status" },
            "the argument vector the test seams read is unchanged");

        var empty = new ProcessStartInfo("git");
        empty.Environment.Remove("GIT_CONFIG_COUNT");
        RunnerGitProcess.DisableAutoMaintenance(empty);
        empty.Environment["GIT_CONFIG_COUNT"].ShouldBe("2");
        empty.Environment["GIT_CONFIG_KEY_0"].ShouldBe("gc.auto");
        empty.Environment["GIT_CONFIG_KEY_1"].ShouldBe("maintenance.auto");
    }

    [Test]
    [Arguments("x")]
    [Arguments("-1")]
    [Arguments(" ")]
    public void C1441_invalid_count_throws(string count)
    {
        var start = new ProcessStartInfo("git");
        start.Environment["GIT_CONFIG_COUNT"] = count;
        Should.Throw<InvalidOperationException>(() => RunnerGitProcess.DisableAutoMaintenance(start))
            .Message.ShouldBe("git_config_count_invalid");
        start.Environment["GIT_CONFIG_COUNT"].ShouldBe(count, "an invalid count is never overwritten");
    }

    [Test]
    [Timeout(60_000)]
    public async Task C1441_inspection_keeps_its_isolated_override_list(CancellationToken ct)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1441-inspect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Git(root, "init", "-q");
            Git(root, "config", "gc.auto", "6700");
            ProcessStartInfo? seen = null;
            var service = new RunnerWorkspaceService(root, root, root, psi =>
            {
                seen = psi;
                return Process.Start(psi);
            }, TimeSpan.FromSeconds(15));
            var result = await service.InspectionGitAsync(root, ct, "config", "--get", "gc.auto");
            result.ExitCode.ShouldBe(0, result.Stderr);
            result.Stdout.Trim().ShouldBe("0");
            seen.ShouldNotBeNull();
            seen.Environment["GIT_CONFIG_COUNT"].ShouldBe("16",
                "inspection sets its own list, including the two maintenance keys, and is not appended again");
            Last(seen, "gc.auto").ShouldBe("0");
            Last(seen, "maintenance.auto").ShouldBe("false");
            seen.ArgumentList.ToArray().ShouldBe(new[] { "--no-replace-objects", "config", "--get", "gc.auto" });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ReadResult> Read(string root, string key, CancellationToken ct)
    {
        ProcessStartInfo? seen = null;
        var result = await RunnerWorkspaceService.RunGitAsync(root, psi =>
        {
            seen = psi;
            return Process.Start(psi);
        }, TimeSpan.FromSeconds(15), ct, "config", "--get", key);
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
