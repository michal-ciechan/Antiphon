using Antiphon.Tests.TestHelpers;
using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ScriptHarnessLinuxOwnershipTests
{
    [Test]
    public async Task Supervisor_pins_group_after_root_exit()
    {
        ScriptProcessRequest? request = null;
        LinuxScriptHarnessProcess? owner = null;
        var options = ScriptHarnessProcessFixture.Options() with
        { OwnerFactory = value => { request = value; return owner = new LinuxScriptHarnessProcess(value); } };
        var run = ScriptHarnessProcess.RunAsync("fixture", "C806", "ExitedStdout",
            ScriptHarnessProcessFixture.ScriptPath, options, CancellationToken.None);
        var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, run);
        try
        {
            var clock = Stopwatch.StartNew();
            while (tree.Root.Executing() && clock.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20);
            tree.Root.Executing().ShouldBeFalse();
            owner!.SupervisorHasExited.ShouldBeFalse();
            owner.ConfirmedGroupId.ShouldBe(owner.SupervisorId);
            var supervisor = ReadProc(owner.SupervisorId);
            var child = ReadProc(tree.Child.Pid);
            supervisor.Group.ShouldBe(owner.SupervisorId);
            supervisor.Session.ShouldBe(owner.SupervisorId);
            child.Group.ShouldBe(owner.SupervisorId);
            child.Session.ShouldBe(owner.SupervisorId);
            (await ScriptHarnessProcessFixture.CaptureAsync(run)).ShouldBeOfType<TimeoutException>();
            tree.AssertDeadBeforeEmergencySweep();
        }
        finally { tree.EmergencyStop(); }
    }

    [Test]
    public async Task Group_stop_leaves_unrelated_process_alive()
    {
        var sentinelStart = new ProcessStartInfo("pwsh") { UseShellExecute = false };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 15" })
            sentinelStart.ArgumentList.Add(arg);
        using var sentinel = Process.Start(sentinelStart) ?? throw new InvalidOperationException("Sentinel did not start.");
        try
        {
            var error = await ScriptHarnessProcessFixture.CaptureAsync(ScriptHarnessProcess.RunAsync(
                "fixture", "C806", "LiveRoot", ScriptHarnessProcessFixture.ScriptPath,
                ScriptHarnessProcessFixture.Options(), CancellationToken.None));
            error.ShouldBeOfType<TimeoutException>();
            sentinel.HasExited.ShouldBeFalse("A separate process group must not be signaled.");
        }
        finally { if (!sentinel.HasExited) sentinel.Kill(); await sentinel.WaitForExitAsync(); }
    }

    [Test]
    public async Task Supervisor_closes_its_output_writers()
    {
        var request = Request("Passing");
        using var owner = new LinuxScriptHarnessProcess(request);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var root = owner.StartAndWaitForRootAsync(deadline.Token);
        var stdout = owner.Stdout.ReadToEndAsync();
        var stderr = owner.Stderr.ReadToEndAsync();
        (await root.WaitAsync(deadline.Token)).ShouldBe(0);
        (await stdout.WaitAsync(deadline.Token)).ShouldContain("C487 HARNESS EXIT CODE: 0");
        (await stderr.WaitAsync(deadline.Token)).ShouldBe("");
        owner.SupervisorHasExited.ShouldBeFalse("EOF must precede the supervisor stop.");
        await owner.TerminateAsync(deadline.Token);
        await owner.ConfirmDeadAsync(deadline.Token);
        owner.CloseStreams();
        DeletePaths(request);
    }

    [Test]
    public async Task Control_disconnect_kills_group()
    {
        var request = Request("LiveRoot");
        using var owner = new LinuxScriptHarnessProcess(request);
        using var launch = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var root = owner.StartAndWaitForRootAsync(launch.Token);
        var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, root);
        try
        {
            var clock = Stopwatch.StartNew();
            owner.DisconnectControl();
            using var observe = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await owner.ConfirmDeadAsync(observe.Token);
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3));
            tree.Root.Executing().ShouldBeFalse();
            tree.Child.Executing().ShouldBeFalse();
            tree.Grandchild.Executing().ShouldBeFalse();
            _ = await ScriptHarnessProcessFixture.CaptureAsync(root);
        }
        finally { tree.EmergencyStop(); owner.CloseStreams(); DeletePaths(request); }
    }

    [Test]
    public async Task Failsafe_kills_group_with_connected_idle_controller()
    {
        var request = Request("LiveRoot");
        using var owner = new LinuxScriptHarnessProcess(request);
        using var launch = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var clock = Stopwatch.StartNew();
        var root = owner.StartAndWaitForRootAsync(launch.Token);
        var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, root);
        try
        {
            // Keep the host control socket connected; no stop or disconnect is sent.
            using var observe = new CancellationTokenSource(TimeSpan.FromSeconds(9));
            await owner.ConfirmDeadAsync(observe.Token);
            clock.Elapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(8));
            tree.Root.Executing().ShouldBeFalse();
            tree.Child.Executing().ShouldBeFalse();
            tree.Grandchild.Executing().ShouldBeFalse();
            _ = await ScriptHarnessProcessFixture.CaptureAsync(root);
        }
        finally { tree.EmergencyStop(); owner.CloseStreams(); DeletePaths(request); }
    }

    [Test]
    public async Task Root_stdin_is_separate_from_control_pipe()
    {
        var request = Request("Stdin");
        using var owner = new LinuxScriptHarnessProcess(request);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var root = owner.StartAndWaitForRootAsync(deadline.Token);
        var stdout = owner.Stdout.ReadToEndAsync();
        var stderr = owner.Stderr.ReadToEndAsync();
        const string payload = "stdin-c806-\u03a9";
        await owner.RootStdin.WriteLineAsync(payload);
        await owner.RootStdin.FlushAsync();
        (await root.WaitAsync(deadline.Token)).ShouldBe(0);
        (await stdout.WaitAsync(deadline.Token)).ShouldContain("STDIN:" + payload);
        (await stderr.WaitAsync(deadline.Token)).ShouldBe("");
        await owner.TerminateAsync(deadline.Token);
        await owner.ConfirmDeadAsync(deadline.Token);
        owner.CloseStreams();
        DeletePaths(request);
    }

    private static ScriptProcessRequest Request(string caseName)
    {
        var id = Guid.NewGuid().ToString("N");
        return new ScriptProcessRequest(ScriptHarnessProcess.ResolvePowerShell(), ScriptHarnessProcessFixture.ScriptPath, caseName,
            Path.Combine(Path.GetTempPath(), "c806-direct-results-" + id),
            Path.Combine(Path.GetTempPath(), "c806-direct-control-" + id),
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2),
            ["-HelperPath", ScriptHarnessProcessFixture.HelperPath], KeepStdinOpen: caseName == "Stdin");
    }

    private static void DeletePaths(ScriptProcessRequest request)
    {
        if (Directory.Exists(request.ResultsDirectory)) Directory.Delete(request.ResultsDirectory, true);
        if (Directory.Exists(request.ControlDirectory)) Directory.Delete(request.ControlDirectory, true);
    }

    private static (int Group, int Session) ReadProc(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var end = stat.LastIndexOf(") ", StringComparison.Ordinal);
        var fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (int.Parse(fields[2]), int.Parse(fields[3]));
    }
}
