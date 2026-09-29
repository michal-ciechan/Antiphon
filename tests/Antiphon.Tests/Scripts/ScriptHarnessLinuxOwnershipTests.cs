using Antiphon.Tests.TestHelpers;
using System.Diagnostics;
using System.Text;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ScriptHarnessLinuxOwnershipTests
{
    [Test]
    public async Task Invalid_group_handshake_never_starts_harness()
    {
        const int pid = 4242;
        const long ticks = 9911;
        const string nonce = "good";
        static LinuxScriptHarnessProcess.LinuxIdentity Identity(int _) => new('S', pid, pid, ticks);
        var good = $"HELLO {nonce} {pid} {ticks} {pid} {pid}";
        LinuxScriptHarnessProcess.ValidateHello(good, nonce, pid, 1, Identity).Group.ShouldBe(pid);
        foreach (var bad in new[] {
            $"HELLO foreign {pid} {ticks} {pid} {pid}",
            $"HELLO {nonce} {pid + 1} {ticks} {pid + 1} {pid + 1}",
            $"HELLO {nonce} {pid} {ticks + 1} {pid} {pid}",
            $"HELLO {nonce} {pid} {ticks} {pid + 1} {pid}",
            $"HELLO {nonce} {pid} {ticks} {pid} {pid + 1}",
            $"HELLO {nonce} {pid} {ticks} -1 {pid}",
            $"HELLO {nonce} {pid} {ticks} 0 {pid}",
            $"HELLO {nonce} {pid} {ticks} 1 {pid}" })
            Should.Throw<InvalidDataException>(() => LinuxScriptHarnessProcess.ValidateHello(bad, nonce, pid, 1, Identity));
        Should.Throw<InvalidDataException>(() => LinuxScriptHarnessProcess.ValidateHello(good, nonce, pid, pid, Identity));
        await using var probe = new LinuxSupervisorProbe("Passing");
        var hello = await probe.ConnectAsync();
        var observed = LinuxScriptHarnessProcess.ValidateHello(hello, probe.Nonce, probe.SupervisorId,
            LinuxScriptHarnessProcess.ReadIdentity(Environment.ProcessId).Group, LinuxScriptHarnessProcess.ReadIdentity);
        observed.Group.ShouldBe(probe.SupervisorId);
        (await probe.StartAsync()).ShouldStartWith("ROOT " + probe.Nonce);
        (await probe.ReadAsync()).ShouldStartWith("EXIT " + probe.Nonce);
    }

    [Test]
    public async Task Setsid_failure_refuses_child_launch()
    {
        var request = Request("Passing");
        using (var owner = new LinuxScriptHarnessProcess(request, new LinuxOwnerFaults(HelperMode: "fail-setsid")))
        {
            var error = await ScriptHarnessProcessFixture.CaptureAsync(owner.StartAndWaitForRootAsync(CancellationToken.None));
            error.ShouldBeOfType<IOException>().Message.ShouldContain("setsid failure");
            File.Exists(Path.Combine(request.ResultsDirectory, "root")).ShouldBeFalse();
            owner.SupervisorHasExited.ShouldBeTrue();
        }
        DeletePaths(request);
        await using var good = new LinuxSupervisorProbe("Passing");
        var hello = await good.ConnectAsync();
        var identity = LinuxScriptHarnessProcess.ValidateHello(hello, good.Nonce, good.SupervisorId,
            LinuxScriptHarnessProcess.ReadIdentity(Environment.ProcessId).Group, LinuxScriptHarnessProcess.ReadIdentity);
        identity.Session.ShouldBe(good.SupervisorId);
        (await good.StartAsync()).ShouldStartWith("ROOT " + good.Nonce);
    }

    [Test]
    public async Task Start_authorization_requires_matching_nonce()
    {
        await using (var denied = new LinuxSupervisorProbe("Passing"))
        {
            (await denied.ConnectAsync()).ShouldStartWith("HELLO " + denied.Nonce);
            await denied.SendAsync("START foreign");
            await denied.WaitExitedAsync();
            File.Exists(Path.Combine(denied.ResultsDirectory, "root")).ShouldBeFalse();
        }
        await using var allowed = new LinuxSupervisorProbe("Passing");
        await allowed.ConnectAsync();
        (await allowed.StartAsync()).ShouldStartWith("ROOT " + allowed.Nonce);
        (await allowed.ReadAsync()).ShouldStartWith("EXIT " + allowed.Nonce);
    }

    [Test]
    public async Task Missing_start_authorization_never_launches_child()
    {
        await using var probe = new LinuxSupervisorProbe("Passing");
        (await probe.ConnectAsync()).ShouldStartWith("HELLO " + probe.Nonce);
        probe.Disconnect();
        await probe.WaitExitedAsync();
        File.Exists(Path.Combine(probe.ResultsDirectory, "root")).ShouldBeFalse();
    }

    [Test]
    public async Task Malformed_control_frames_are_refused()
    {
        using (var tooLong = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 4097) + "\n")))
            (await ScriptHarnessProcessFixture.CaptureAsync(
                LinuxScriptHarnessProcess.ReadFrameAsync(tooLong, CancellationToken.None)))
                .ShouldBeOfType<InvalidDataException>().Message.ShouldContain("4096");
        using (var exact = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 4096) + "\n")))
            (await LinuxScriptHarnessProcess.ReadFrameAsync(exact, CancellationToken.None)).Length.ShouldBe(4096);
        using (var truncated = new MemoryStream(Encoding.UTF8.GetBytes("HELLO truncated")))
            (await ScriptHarnessProcessFixture.CaptureAsync(
                LinuxScriptHarnessProcess.ReadFrameAsync(truncated, CancellationToken.None)))
                .ShouldBeOfType<EndOfStreamException>();
        Should.Throw<InvalidDataException>(() => LinuxScriptHarnessProcess.ValidateHello("garbage", "n", 42, 1,
            _ => new LinuxScriptHarnessProcess.LinuxIdentity('S', 42, 42, 1)));
        await using var probe = new LinuxSupervisorProbe("Passing");
        await probe.ConnectAsync();
        await probe.SendAsync("MALFORMED");
        await probe.WaitExitedAsync();
        File.Exists(Path.Combine(probe.ResultsDirectory, "root")).ShouldBeFalse();
    }

    [Test]
    public async Task Root_exit_receipt_requires_matching_nonce()
    {
        await using var probe = new LinuxSupervisorProbe("Race");
        await probe.ConnectAsync();
        var root = (await probe.StartAsync()).Split(' ');
        await WaitForFileAsync(Path.Combine(probe.ResultsDirectory, "ready"));
        Should.Throw<InvalidDataException>(() => LinuxScriptHarnessProcess.ValidateRootExit(
            $"EXIT foreign {root[2]} {root[3]} 0", probe.Nonce, int.Parse(root[2]), long.Parse(root[3])));
        File.WriteAllText(Path.Combine(probe.ResultsDirectory, "release"), "go");
        var exit = await probe.ReadAsync();
        LinuxScriptHarnessProcess.ValidateRootExit(exit, probe.Nonce, int.Parse(root[2]), long.Parse(root[3])).ShouldBe(0);
    }

    [Test]
    public async Task Root_exit_receipt_requires_matching_identity()
    {
        await using var probe = new LinuxSupervisorProbe("Race");
        await probe.ConnectAsync();
        var root = (await probe.StartAsync()).Split(' ');
        await WaitForFileAsync(Path.Combine(probe.ResultsDirectory, "ready"));
        var pid = int.Parse(root[2]);
        var ticks = long.Parse(root[3]);
        Should.Throw<InvalidDataException>(() => LinuxScriptHarnessProcess.ValidateRootExit(
            $"EXIT {probe.Nonce} {pid + 1} {ticks} 0", probe.Nonce, pid, ticks));
        Should.Throw<InvalidDataException>(() => LinuxScriptHarnessProcess.ValidateRootExit(
            $"EXIT {probe.Nonce} {pid} {ticks + 1} 0", probe.Nonce, pid, ticks));
        File.WriteAllText(Path.Combine(probe.ResultsDirectory, "release"), "go");
        LinuxScriptHarnessProcess.ValidateRootExit(await probe.ReadAsync(), probe.Nonce, pid, ticks).ShouldBe(0);
    }

    [Test]
    public async Task Stop_requires_matching_nonce()
    {
        await using var probe = new LinuxSupervisorProbe("LiveRoot");
        await probe.ConnectAsync();
        await probe.StartAsync();
        await WaitForFileAsync(Path.Combine(probe.ResultsDirectory, "ready"));
        var rootPid = int.Parse(File.ReadAllText(Path.Combine(probe.ResultsDirectory, "root")).Split(' ')[0]);
        await probe.SendAsync("STOP foreign");
        await Task.Delay(100);
        probe.SupervisorExited.ShouldBeFalse();
        IsExecuting(rootPid).ShouldBeTrue();
        await probe.SendAsync("STOP " + probe.Nonce);
        await probe.WaitExitedAsync();
        await WaitDeadAsync(rootPid);
    }
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
    public async Task Lost_supervisor_reports_cleanup_failure()
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
            await WaitDeadAsync(tree.Root.Pid);
            using var supervisor = Process.GetProcessById(owner!.SupervisorId);
            supervisor.Kill(entireProcessTree: false);
            await supervisor.WaitForExitAsync();
            var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
            error.ShouldNotBeNull();
            Diagnostic(error).ShouldContain("supervisor exited before explicit stop");
            Directory.Exists(request!.ResultsDirectory).ShouldBeTrue();
            tree.Child.Executing().ShouldBeTrue();
        }
        finally { tree.EmergencyStop(); if (request is not null) DeletePaths(request); }
    }

    [Test]
    public async Task Lost_identity_never_signals_saved_group()
    {
        ScriptProcessRequest? request = null;
        LinuxScriptHarnessProcess? owner = null;
        var stale = false;
        LinuxScriptHarnessProcess.LinuxIdentity original = default;
        var options = ScriptHarnessProcessFixture.Options() with
        {
            OwnerFactory = value =>
            {
                request = value;
                return owner = new LinuxScriptHarnessProcess(value, new LinuxOwnerFaults(IdentityReader: pid =>
                {
                    if (stale && pid == owner?.SupervisorId) return original with { Start = original.Start + 1 };
                    return LinuxScriptHarnessProcess.ReadIdentity(pid);
                }));
            }
        };
        var run = ScriptHarnessProcess.RunAsync("fixture", "C806", "ExitedStdout",
            ScriptHarnessProcessFixture.ScriptPath, options, CancellationToken.None);
        var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, run);
        try
        {
            await WaitDeadAsync(tree.Root.Pid);
            var savedGroup = owner!.ConfirmedGroupId;
            original = LinuxScriptHarnessProcess.ReadIdentity(owner.SupervisorId);
            using var supervisor = Process.GetProcessById(owner.SupervisorId);
            // The fault reader must still report a stale identity after the process is gone.
            stale = true;
            supervisor.Kill(entireProcessTree: false);
            await supervisor.WaitForExitAsync();
            var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
            error.ShouldNotBeNull();
            Diagnostic(error).ShouldContain("identity mismatch");
            owner.ConfirmedGroupId.ShouldBe(savedGroup);
            tree.Child.Executing().ShouldBeTrue("A stale group must not be signaled by the host.");
            Directory.Exists(request!.ResultsDirectory).ShouldBeTrue();
        }
        finally { tree.EmergencyStop(); if (request is not null) DeletePaths(request); }
    }

    [Test]
    public async Task Unresponsive_supervisor_stop_is_bounded()
    {
        ScriptProcessRequest? request = null;
        LinuxScriptHarnessProcess? owner = null;
        using var caller = new CancellationTokenSource();
        var options = ScriptHarnessProcessFixture.Options() with
        {
            OwnerFactory = value =>
            {
                request = value;
                return owner = new LinuxScriptHarnessProcess(value, new LinuxOwnerFaults(
                    StopWriter: (_, _, token) => Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token)));
            }
        };
        var clock = Stopwatch.StartNew();
        var run = ScriptHarnessProcess.RunAsync("fixture", "C806", "LiveRoot",
            ScriptHarnessProcessFixture.ScriptPath, options, caller.Token);
        var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, run);
        try
        {
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(4));
            caller.Cancel();
            var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
            error.ShouldBeOfType<OperationCanceledException>().Data["ScriptHarnessDiagnostics"]
                .ToString().ShouldContain("terminate:");
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(8));
            Directory.Exists(request!.ResultsDirectory).ShouldBeTrue();
            await WaitDeadAsync(owner!.SupervisorId);
            tree.Root.Executing().ShouldBeFalse();
            tree.Child.Executing().ShouldBeFalse();
        }
        finally { tree.EmergencyStop(); if (request is not null) DeletePaths(request); }
    }

    [Test]
    public async Task Unreadable_group_membership_is_not_empty()
    {
        ScriptProcessRequest? request = null;
        LinuxScriptHarnessProcess? owner = null;
        var deny = false;
        var options = ScriptHarnessProcessFixture.Options() with
        {
            OwnerFactory = value =>
            {
                request = value;
                return owner = new LinuxScriptHarnessProcess(value, new LinuxOwnerFaults(IdentityReader: pid =>
                {
                    if (deny && pid == 1) throw new UnauthorizedAccessException("injected /proc member census denial");
                    var identity = LinuxScriptHarnessProcess.ReadIdentity(pid);
                    if (pid == owner?.SupervisorId) deny = true;
                    return identity;
                }));
            }
        };
        var run = ScriptHarnessProcess.RunAsync("fixture", "C806", "Passing",
            ScriptHarnessProcessFixture.ScriptPath, options, CancellationToken.None);
        var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
        error.ShouldBeOfType<IOException>().Message.ShouldContain("injected /proc member census denial");
        Directory.Exists(request!.ControlDirectory).ShouldBeTrue();
        DeletePaths(request);
    }

    [Test]
    public async Task Zombie_members_are_not_executing()
    {
        foreach (var state in new[] { 'R', 'S', 'T' })
            LinuxScriptHarnessProcess.IsExecutingState(state).ShouldBeTrue();
        LinuxScriptHarnessProcess.IsExecutingState('Z').ShouldBeFalse();
        LinuxScriptHarnessProcess.IsExecutingState('X').ShouldBeFalse();
        await using var probe = new LinuxSupervisorProbe("Race");
        await probe.ConnectAsync();
        var root = (await probe.StartAsync()).Split(' ');
        await WaitForFileAsync(Path.Combine(probe.ResultsDirectory, "ready"));
        var receipt = probe.ReadAsync();
        await Task.Delay(100);
        receipt.IsCompleted.ShouldBeFalse("Root-exit receipt cannot precede the live root exit.");
        File.WriteAllText(Path.Combine(probe.ResultsDirectory, "release"), "go");
        var exit = await receipt;
        LinuxScriptHarnessProcess.ValidateRootExit(exit, probe.Nonce, int.Parse(root[2]), long.Parse(root[3])).ShouldBe(0);
        await WaitDeadAsync(int.Parse(root[2]));
    }

    [Test]
    public async Task Stop_ack_waits_for_group_exit()
    {
        ScriptProcessRequest? request = null;
        LinuxScriptHarnessProcess? owner = null;
        var stopSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = ScriptHarnessProcessFixture.Options() with
        {
            OwnerFactory = value =>
            {
                request = value;
                return owner = new LinuxScriptHarnessProcess(value, new LinuxOwnerFaults(
                    StopWriter: (_, _, _) => { stopSeen.TrySetResult(); return Task.CompletedTask; }));
            }
        };
        var run = ScriptHarnessProcess.RunAsync("fixture", "C806", "Silent",
            ScriptHarnessProcessFixture.ScriptPath, options, CancellationToken.None);
        var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, run);
        try
        {
            await stopSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
            run.IsCompleted.ShouldBeFalse("Stop delivery alone cannot confirm group death.");
            tree.Child.Executing().ShouldBeTrue();
            Directory.Exists(request!.ResultsDirectory).ShouldBeTrue();
            await owner!.SendStopForTestAsync(CancellationToken.None);
            (await ScriptHarnessProcessFixture.CaptureAsync(run)).ShouldBeNull();
            tree.AssertDeadBeforeEmergencySweep();
        }
        finally { tree.EmergencyStop(); if (request is not null) DeletePaths(request); }
    }

    [Test]
    public async Task Failsafe_includes_time_waiting_for_start()
    {
        var clock = Stopwatch.StartNew();
        await using var probe = new LinuxSupervisorProbe("LiveRoot", failsafeMilliseconds: 7000);
        (await probe.ConnectAsync()).ShouldStartWith("HELLO " + probe.Nonce);
        var startDelay = TimeSpan.FromSeconds(2) - clock.Elapsed;
        if (startDelay > TimeSpan.Zero) await Task.Delay(startDelay);
        (await probe.StartAsync()).ShouldStartWith("ROOT " + probe.Nonce);
        await WaitForFileAsync(Path.Combine(probe.ResultsDirectory, "ready"));
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        var rootPid = int.Parse(File.ReadAllText(Path.Combine(probe.ResultsDirectory, "root")).Split(' ')[0]);
        await probe.WaitExitedAsync(TimeSpan.FromSeconds(8) - clock.Elapsed);
        clock.Elapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(8));
        await WaitDeadAsync(rootPid);
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

    private static async Task WaitForFileAsync(string path)
    {
        var clock = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Fixture marker missing: " + path);
            await Task.Delay(20);
        }
    }

    private static bool IsExecuting(int pid)
    {
        try { return LinuxScriptHarnessProcess.IsExecutingState(LinuxScriptHarnessProcess.ReadIdentity(pid).State); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static async Task WaitDeadAsync(int pid)
    {
        var clock = Stopwatch.StartNew();
        while (IsExecuting(pid))
        {
            if (clock.Elapsed >= TimeSpan.FromSeconds(3)) throw new TimeoutException($"Process {pid} stayed executing.");
            await Task.Delay(20);
        }
    }

    private static string Diagnostic(Exception error) => error.Message + "\n" +
        error.Data["ScriptHarnessDiagnostics"];

    private static (int Group, int Session) ReadProc(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var end = stat.LastIndexOf(") ", StringComparison.Ordinal);
        var fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (int.Parse(fields[2]), int.Parse(fields[3]));
    }
}
