using System.Diagnostics;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1614: namespace boot and residue counting over a private filesystem.
/// The extracted shell is what runs; these methods do not touch a live cgroup.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CustodyNamespaceBootstrapTests
{
    private static readonly string[] ComposeFiles =
    [
        "docker-compose.server2-runner.yml",
        "docker-compose.server2-runner.temp.yml",
        "docker-compose.runner-grok.yml",
    ];

    [Test]
    public async Task C1614_Namespace_lifecycle_and_boot_failure_are_safe()
    {
        RequireLinux();
        var script = CommonScript();
        var state = NewDir();
        var run = NewDir();
        try
        {
            var first = await BootAsync(script, state, run, null);
            Guid.TryParse(NamespaceOf(first.Stdout), out var guid).ShouldBeTrue(
                "namespace boot must record a guid, actual '" + first.Stdout + "'");
            var restart = await BootAsync(script, state, run, null);
            NamespaceOf(restart.Stdout).ShouldBe(guid.ToString("D"));

            var recreated = await BootAsync(script, NewDir(), NewDir(), null);
            var fresh = NamespaceOf(recreated.Stdout);
            Guid.TryParse(fresh, out var other).ShouldBeTrue(recreated.Stdout);
            other.ShouldNotBe(guid);

            var linked = NewDir();
            var target = Path.Combine(linked, "target");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "namespace"), guid.ToString("D"));
            File.CreateSymbolicLink(Path.Combine(linked, "namespace"), Path.Combine(target, "namespace"));
            var invalid = await BootAsync(script, linked, NewDir(), null);
            NamespaceOf(invalid.Stdout).ShouldBeNull();
            File.Exists(Path.Combine(linked, "capability")).ShouldBeFalse();

            var failedState = NewDir();
            var failed = await BootAsync(script, failedState, NewDir(), "mkdir");
            failed.ExitCode.ShouldBe(0);
            NamespaceOf(failed.Stdout).ShouldBeNull();
            failed.Stdout.ShouldContain("continued=yes");
            File.Exists(Path.Combine(failedState, "namespace")).ShouldBeFalse();
            File.Exists(Path.Combine(failedState, "capability")).ShouldBeFalse();
        }
        finally
        {
            TryDelete(state);
            TryDelete(run);
        }
    }

    [Test]
    public async Task C1614_Read_only_namespace_marker_does_not_stop_boot()
    {
        RequireLinux();
        var failed = await StepAsync(CommonScript());
        failed.ExitCode.ShouldBe(0, failed.Stdout);
        failed.Stdout.ShouldContain("continued=yes");
        failed.Stdout.ShouldContain("qualified=false");
    }

    [Test]
    public async Task C1614_Verifiers_count_execution_trees_in_current_namespace()
    {
        RequireLinux();
        var script = CommonScript();
        var root = NewDir();
        var blocked = Path.Combine(NewDir(), "blocked");
        try
        {
            var current = Guid.NewGuid();
            var foreign = Guid.NewGuid();
            WriteTree(root, current, Guid.NewGuid(), "4321\n");
            WriteTree(root, current, Guid.NewGuid(), "");
            WriteTree(root, foreign, Guid.NewGuid(), "4322\n");
            WriteTree(root, "legacy-root", Guid.NewGuid(), "4323\n");

            var counted = await CountAsync(script, root, current);
            Field(counted.Stdout, "current").ShouldBe("2");
            Field(counted.Stdout, "populated").ShouldBe("1");
            Field(counted.Stdout, "foreign").ShouldBe("1");
            Field(counted.Stdout, "legacy").ShouldBe("1");
            counted.ExitCode.ShouldBe(0);

            Directory.CreateDirectory(blocked);
            File.SetUnixFileMode(blocked, UnixFileMode.None);
            var unreadable = await CountAsync(script, blocked, current);
            unreadable.ExitCode.ShouldNotBe(0);
            Field(unreadable.Stdout, "status").ShouldBe("read-error");

            var repo = RepoRoot();
            foreach (var relative in ComposeFiles)
            {
                var text = File.ReadAllText(Path.Combine(repo, relative));
                text.ShouldNotContain("/var/lib/antiphon-custody");
                text.ShouldNotContain("/run/antiphon-custody");
            }

            var dockerfile = File.ReadAllText(Path.Combine(repo, "docker", "session-runner-grok", "Dockerfile"));
            foreach (var line in dockerfile.Split('\n'))
            {
                if (!line.TrimStart().StartsWith("VOLUME", StringComparison.Ordinal))
                    continue;
                line.ShouldNotContain("/var/lib/antiphon-custody");
                line.ShouldNotContain("/run/antiphon-custody");
            }
        }
        finally
        {
            try { File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            catch { }
            TryDelete(root);
            TryDelete(Path.GetDirectoryName(blocked)!);
        }
    }

    private static void WriteTree(string root, Guid namespaceId, Guid execution, string procs) =>
        WriteTree(root, namespaceId.ToString("D"), execution, procs);

    private static void WriteTree(string root, string namespaceName, Guid execution, string procs)
    {
        var tree = Path.Combine(root, namespaceName, execution.ToString("D"), "tree");
        Directory.CreateDirectory(tree);
        File.WriteAllText(Path.Combine(tree, "cgroup.procs"), procs);
    }

    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("Linux custody namespace shell executes on Linux only.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker", "session-runner-grok", "antiphon-custody-common.sh")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("antiphon-custody-common.sh");
    }

    private static string CommonScript() =>
        Path.Combine(RepoRoot(), "docker", "session-runner-grok", "antiphon-custody-common.sh");

    private static string? NamespaceOf(string stdout)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (line.StartsWith("namespace=", StringComparison.Ordinal))
                return line["namespace=".Length..].Trim();
        return null;
    }

    private static string Field(string stdout, string name)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = line.Split('=', 2);
            if (split.Length == 2 && split[0] == name)
                return split[1].Trim();
        }

        return "";
    }

    private static string NewDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "c1614-boot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private static Task<ShellResult> BootAsync(string script, string state, string run, string? fail) =>
        BashAsync(script, "custody_namespace_boot", fail is null ? [state, run] : [state, run, fail]);

    private static Task<ShellResult> StepAsync(string script)
    {
        var psi = new ProcessStartInfo("unshare")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-rm");
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("""
            set -euo pipefail
            base=$(mktemp -d)
            trap 'umount "$base/run" 2>/dev/null || true; rm -rf "$base"' EXIT
            mkdir -p "$base/state" "$base/run"
            printf 'not-a-guid\n' > "$base/state/namespace"
            chmod 444 "$base/state/namespace"
            printf 'stale\n' > "$base/run/namespace"
            mount --bind -o ro "$base/run" "$base/run"
            source "$1"
            custody_namespace_entrypoint_step "$base/state" "$base/run" "$1"
            echo "continued=yes"
            echo "qualified=${SessionRunner__LinuxCustodyQualified-unset}"
            """);
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add(script);
        return StartAsync(psi, "custody_namespace_entrypoint_step");
    }

    private static async Task<ShellResult> StartAsync(ProcessStartInfo psi, string name)
    {
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("bash");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new System.TimeoutException(name);
        }

        return new ShellResult(process.ExitCode, (await stdout) + (await stderr), "");
    }

    private static Task<ShellResult> CountAsync(string script, string root, Guid namespaceId) =>
        BashAsync(script, "custody_count_execution_trees", root, namespaceId.ToString("D"));

    private static async Task<ShellResult> BashAsync(string script, string function, params string[] args)
    {
        var psi = new ProcessStartInfo("/bin/bash")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("set -u; source \"$1\"; shift; func=\"$1\"; shift; \"$func\" \"$@\"");
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(function);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("bash");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new System.TimeoutException(function);
        }

        return new ShellResult(process.ExitCode, (await stdout) + (await stderr), "");
    }

    private readonly record struct ShellResult(int ExitCode, string Stdout, string Stderr);
}
