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
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("Linux custody lifecycle executes on Linux only.");
        // Container recreate with a reused volume stays NATIVE-PENDING (NP-36, NP-39).
        // Two enter --probe calls share one user namespace and a tmpfs v1 root.
        // mkdir does not create cgroup.procs there, so prepare_tree_v1 refuses
        // CustodyTreeUnavailable on the pids hierarchy, before remove_tree and
        // before the freezer hierarchy. The two pids trees remain and differ.
        var result = await StartAsync("unshare",
        [
            "-rm", "bash", "-c", """
            set -euo pipefail
            library=$1
            enter=$2
            ns1=$(tr -d '[:space:]' < /proc/sys/kernel/random/uuid)
            ns2=$(tr -d '[:space:]' < /proc/sys/kernel/random/uuid)
            probe=00000000-0000-0000-0000-00000000c604
            trap 'umount /usr/local 2>/dev/null || true; umount /sys/fs/cgroup 2>/dev/null || true; umount /run 2>/dev/null || true' EXIT
            mount -t tmpfs tmpfs /usr/local
            mount -t tmpfs tmpfs /sys/fs/cgroup
            mount -t tmpfs tmpfs /run
            mkdir -p /usr/local/lib /run/antiphon-custody \
              /sys/fs/cgroup/pids/antiphon-custody \
              /sys/fs/cgroup/freezer/antiphon-custody
            cp "$library" /usr/local/lib/antiphon-custody-common.sh
            printf '%s\n' "$ns1" > /run/antiphon-custody/namespace
            set +e
            bash "$enter" --probe
            rc1=$?
            set -e
            echo "rc1=$rc1"
            p1=/sys/fs/cgroup/pids/antiphon-custody/$ns1/$probe/tree
            f1=/sys/fs/cgroup/freezer/antiphon-custody/$ns1/$probe/tree
            printf '%s\n' "$ns2" > /run/antiphon-custody/namespace
            set +e
            bash "$enter" --probe
            rc2=$?
            set -e
            echo "rc2=$rc2"
            p2=/sys/fs/cgroup/pids/antiphon-custody/$ns2/$probe/tree
            f2=/sys/fs/cgroup/freezer/antiphon-custody/$ns2/$probe/tree
            echo "pids_left=$p1"
            echo "pids_right=$p2"
            echo "freezer_left=$f1"
            echo "freezer_right=$f2"
            if [ -d "$p1" ] && [ -d "$p2" ] && [ ! -d "$f1" ] && [ ! -d "$f2" ] \
              && [ "$p1" != "$p2" ] \
              && [ "$rc1" -ne 0 ] && [ "$rc2" -ne 0 ]; then
              echo "disjoint=yes"
              exit 0
            fi
            echo "disjoint=no"
            exit 1
            """,
            "bash", CommonScript(), EnterScript(),
        ]);
        result.ExitCode.ShouldBe(0, result.Stdout + "\n" + result.Stderr);
        result.Stdout.ShouldContain("disjoint=yes");
        result.Stderr.ShouldContain("CustodyTreeUnavailable");
        var pidsLeft = Value(result.Stdout, "pids_left");
        var pidsRight = Value(result.Stdout, "pids_right");
        var freezerLeft = Value(result.Stdout, "freezer_left");
        var freezerRight = Value(result.Stdout, "freezer_right");
        pidsLeft.ShouldNotBe(pidsRight);
        freezerLeft.ShouldNotBe(freezerRight);
        pidsLeft.ShouldContain("/pids/antiphon-custody/");
        freezerLeft.ShouldContain("/freezer/antiphon-custody/");
        pidsLeft.ShouldEndWith("/00000000-0000-0000-0000-00000000c604/tree");
        pidsRight.ShouldEndWith("/00000000-0000-0000-0000-00000000c604/tree");
        result.Stdout.ShouldContain("rc1=3");
        result.Stdout.ShouldContain("rc2=3");
    }

    private static void RequireHelpers()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("Linux custody lifecycle executes on Linux only.");
        if (!File.Exists(EnterHelper) || !File.Exists(KillHelper))
            throw new SkipTestException("Requires the root-owned custody helpers (session-testing image).");
    }

    private static string EnterScript() =>
        Path.Combine(Path.GetDirectoryName(CommonScript())!, "antiphon-custody-enter.sh");

    private static string Value(string text, string key)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            if (line.StartsWith(key + "=", StringComparison.Ordinal))
                return line[(key.Length + 1)..].Trim();
        return "";
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

    private static Task<ShellResult> RunAsync(string file, params string[] args) =>
        StartAsync(file, args);

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
