using System.Diagnostics;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1614 native Linux custody lifecycle. Slice A owns the installed-probe and
/// namespace methods. Root-exit, natural completion and lost-host release stay out.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("LinuxCustody")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LinuxRunnerCustodyLifecycleTests
{
    private const string EnterHelper = "/usr/local/bin/antiphon-custody-enter";
    private const string KillHelper = "/usr/local/bin/antiphon-custody-kill";

    [Test]
    public async Task C1614_Installed_helpers_advertise_custody()
    {
        RequireHelpers();
        foreach (var helper in new[] { EnterHelper, KillHelper })
        {
            var result = await RunAsync("sudo", "-n", helper, "--probe");
            result.ExitCode.ShouldBe(0, result.Stderr);
            result.Stdout.ShouldContain("C604_CUSTODY_PROBE_OK");
        }
    }

    [Test]
    public async Task C1614_Container_namespaces_and_probes_do_not_collide()
    {
        RequireHelpers();
        var script = CommonScript();
        var first = await BashAsync(script, "custody_namespace_boot", NewDir(), NewDir());
        var second = await BashAsync(script, "custody_namespace_boot", NewDir(), NewDir());
        Guid.TryParse(NamespaceOf(first.Stdout), out var left).ShouldBeTrue(first.Stdout);
        Guid.TryParse(NamespaceOf(second.Stdout), out var right).ShouldBeTrue(second.Stdout);
        left.ShouldNotBe(right);
        var execution = Guid.NewGuid();
        var probe = Guid.Parse("00000000-0000-0000-0000-00000000c604");
        var leftTree = $"/sys/fs/cgroup/pids/antiphon-custody/{left:D}/{execution:D}/tree";
        var rightTree = $"/sys/fs/cgroup/pids/antiphon-custody/{right:D}/{execution:D}/tree";
        var leftProbe = $"/sys/fs/cgroup/pids/antiphon-custody/{left:D}/{probe:D}/tree";
        var rightProbe = $"/sys/fs/cgroup/pids/antiphon-custody/{right:D}/{probe:D}/tree";
        leftTree.ShouldNotBe(rightTree);
        leftProbe.ShouldNotBe(rightProbe);
        leftProbe.ShouldContain(left.ToString("D"));
        rightProbe.ShouldContain(right.ToString("D"));
    }

    private static void RequireHelpers()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("Linux custody lifecycle executes on Linux only.");
        if (!File.Exists(EnterHelper) || !File.Exists(KillHelper))
            throw new SkipTestException("Requires the root-owned custody helpers (session-testing image).");
    }

    private static string CommonScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docker", "session-runner-grok", "antiphon-custody-common.sh");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("antiphon-custody-common.sh");
    }

    private static string? NamespaceOf(string stdout)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (line.StartsWith("namespace=", StringComparison.Ordinal))
                return line["namespace=".Length..].Trim();
        return null;
    }

    private static string NewDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "c1614-ns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static Task<ShellResult> RunAsync(string file, params string[] args) =>
        StartAsync(file, args);

    private static Task<ShellResult> BashAsync(string script, string function, params string[] args)
    {
        var argv = new List<string> { "-c", "set -u; source \"$1\"; shift; func=\"$1\"; shift; \"$func\" \"$@\"", "bash", script, function };
        argv.AddRange(args);
        return StartAsync("/bin/bash", argv);
    }

    private static async Task<ShellResult> StartAsync(string file, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start " + file);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new System.TimeoutException(file + " did not exit");
        }

        return new ShellResult(process.ExitCode, await stdout, await stderr);
    }

    private readonly record struct ShellResult(int ExitCode, string Stdout, string Stderr);
}
