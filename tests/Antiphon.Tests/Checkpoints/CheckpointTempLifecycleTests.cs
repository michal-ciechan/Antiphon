using System.Diagnostics;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointTempLifecycleTests : CheckpointTestBase
{
    [Test]
    public Task passing_test_runs_teardown() => Verify("LifecycleHostTests", "passing", 1, success: true);

    [Test]
    public Task assertion_failure_runs_teardown() => Verify("LifecycleHostTests", "assertion_failure", 1);

    [Test]
    public Task cancellation_runs_teardown() => Verify("LifecycleHostTests", "cancellation", 1);

    [Test]
    public Task setup_failure_runs_teardown() => Verify("LifecycleSetupFailureTests", "setup_failure", 1);

    [Test]
    public Task multiple_roots_run_teardown() => Verify("LifecycleHostTests", "multiple_roots", 2, success: true);

    [Test]
    public Task early_assertion_still_joins_owned_child() => Verify("LifecycleHostTests", "early_assertion_with_child", 1,
        verifyChild: true);

    private async Task Verify(string className, string method, int expectedRoots, bool success = false,
        bool verifyChild = false)
    {
        var sandbox = TempDir();
        var inventory = Path.Combine(sandbox, "roots.txt");
        var childPid = Path.Combine(sandbox, "child-pid.txt");
        var hostDll = Path.Combine(AppContext.BaseDirectory, "checkpoint-lifecycle-host",
            "Antiphon.Checkpoints.LifecycleHost.dll");
        File.Exists(hostDll).ShouldBeTrue("lifecycle host must be staged by the parent build");
        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { hostDll, "--treenode-filter", $"/*/*/{className}/{method}" },
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["TMPDIR"] = sandbox;
        psi.Environment["TEMP"] = sandbox;
        psi.Environment["TMP"] = sandbox;
        psi.Environment["C804_LIFECYCLE_ROOTS"] = inventory;
        psi.Environment["C804_LIFECYCLE_CHILD_PID"] = childPid;
        psi.Environment.Remove("C804_ROSTER_FILE");
        using var process = Process.Start(psi)!;
        RegisterCheckpointChild(process);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
        var output = await stdout + "\n" + await stderr;
        (process.ExitCode == 0).ShouldBe(success, output);
        File.Exists(inventory).ShouldBeTrue(output);
        var roots = File.ReadAllLines(inventory);
        roots.Length.ShouldBe(expectedRoots, output);
        foreach (var root in roots) Directory.Exists(root).ShouldBeFalse(output);
        if (verifyChild)
        {
            File.Exists(childPid).ShouldBeTrue(output);
            var pid = int.Parse(File.ReadAllText(childPid));
            try
            {
                using var child = Process.GetProcessById(pid);
                child.HasExited.ShouldBeTrue("fixture teardown must join the child");
            }
            catch (ArgumentException) { }
        }
    }
}
