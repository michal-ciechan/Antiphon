using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Tests.TestHelpers;
using Microsoft.Win32.SafeHandles;
using Shouldly;
using TUnit.Core;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ScriptHarnessWindowsOwnershipTests
{
    private const uint HandleFlagInherit = 0x00000001;

    [Test]
    public async Task Child_is_assigned_before_first_instruction()
    {
        var request = Request("Passing");
        var faults = new WindowsFaultInjection();
        string[]? traceAtBarrier = null;
        uint activeAtBarrier = 0;
        faults.OnAssigned = () =>
        {
            traceAtBarrier = faults.Trace.ToArray();
            activeAtBarrier = WindowsScriptHarnessProcess.QueryActiveProcessesForTesting(faults.JobHandleCopy!);
        };
        using var owner = new WindowsScriptHarnessProcess(request, faults);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var root = owner.StartAndWaitForRootAsync(deadline.Token);
        (await root.WaitAsync(deadline.Token)).ShouldBe(0);
        traceAtBarrier.ShouldBe(["Assign:attempt", "Assign:success"],
            "The private job must already hold the root before the first Resume attempt.");
        activeAtBarrier.ShouldBe(1u, "The suspended root must already be a job member at the pre-resume barrier.");
        faults.Trace.ShouldBe(["Assign:attempt", "Assign:success", "Resume:attempt", "Resume:success"]);
        await owner.TerminateAsync(deadline.Token);
        await owner.ConfirmDeadAsync(deadline.Token);
        owner.CloseStreams();
        faults.JobHandleCopy?.Dispose();
        DeletePaths(request);
    }

    [Test]
    public async Task Assignment_failure_never_resumes_child()
    {
        var request = Request("Passing");
        var faults = new WindowsFaultInjection { FailAssign = true };
        var error = Should.Throw<Win32Exception>(() => new WindowsScriptHarnessProcess(request, faults));
        error.Message.ShouldContain("AssignProcessToJobObject");
        faults.Trace.ShouldBe(["Assign:attempt"], "A failed Assign must never reach a Resume attempt.");
        faults.RootProcessIds.Count.ShouldBe(1);
        var pid = faults.RootProcessIds[0];
        await WaitUntilExitedAsync(pid);
        IsRunning(pid).ShouldBeFalse("The still-suspended root must be terminated, not left running, on Assign failure.");
        using var jobCopy = faults.JobHandleCopy!;
        WindowsScriptHarnessProcess.QueryActiveProcessesForTesting(jobCopy).ShouldBe(0u);
        DeletePaths(request);
    }

    [Test]
    public async Task Resume_failure_terminates_suspended_child()
    {
        var request = Request("Passing");
        var faults = new WindowsFaultInjection { FailResume = true };
        var error = Should.Throw<Win32Exception>(() => new WindowsScriptHarnessProcess(request, faults));
        error.Message.ShouldContain("ResumeThread");
        faults.Trace.ShouldBe(["Assign:attempt", "Assign:success", "Resume:attempt"],
            "A failed Resume must not report success.");
        faults.RootProcessIds.Count.ShouldBe(1);
        var pid = faults.RootProcessIds[0];
        await WaitUntilExitedAsync(pid);
        IsRunning(pid).ShouldBeFalse("A resume failure must explicitly terminate the still-suspended root.");
        using var jobCopy = faults.JobHandleCopy!;
        WindowsScriptHarnessProcess.QueryActiveProcessesForTesting(jobCopy).ShouldBe(0u);
        DeletePaths(request);
    }

    [Test]
    public async Task Closing_private_job_kills_owned_tree()
    {
        var request = Request("LiveRoot");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var owner = new WindowsScriptHarnessProcess(request);
        ScriptHarnessProcessFixture.ObservedTree? tree = null;
        try
        {
            (owner.QueryLimitFlagsForTesting() & WindowsScriptHarnessProcess.JobObjectLimitKillOnJobClose)
                .ShouldBe(WindowsScriptHarnessProcess.JobObjectLimitKillOnJobClose,
                    "The private job must carry kill-on-close before relying on it as a safety net.");
            var root = owner.StartAndWaitForRootAsync(deadline.Token);
            tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, root);
            owner.Dispose();
            var clock = Stopwatch.StartNew();
            while ((tree.Root.Executing() || tree.Child.Executing() || tree.Grandchild.Executing()) &&
                   clock.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(20);
            // Direct construction bypasses ScriptHarnessProcess.RunAsync's own results-directory
            // deletion, so only process death is this owner's contract; DeletePaths below is the
            // test's own cleanup, not evidence of the coordinator's cleanup path.
            tree.Root.Executing().ShouldBeFalse("Script root remained executing after job close.");
            tree.Child.Executing().ShouldBeFalse("Fixture child remained executing after job close.");
            tree.Grandchild.Executing().ShouldBeFalse("Fixture grandchild remained executing after job close.");
            _ = await ScriptHarnessProcessFixture.CaptureAsync(root);
        }
        finally { tree?.EmergencyStop(); owner.Dispose(); DeletePaths(request); }
    }

    [Test]
    public async Task Nested_job_timeout_kills_owned_descendants_only()
    {
        using var outerJob = CreateJobObjectW(IntPtr.Zero, null);
        if (outerJob.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObjectW (outer) failed");
        if (!AssignProcessToJobObject(outerJob, GetCurrentProcess()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject (outer, current process) failed");

        var sentinelStart = new ProcessStartInfo("pwsh") { UseShellExecute = false };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 15" })
            sentinelStart.ArgumentList.Add(arg);
        using var sentinel = Process.Start(sentinelStart) ?? throw new InvalidOperationException("Sentinel did not start.");
        try
        {
            ScriptProcessRequest? request = null;
            WindowsScriptHarnessProcess? owner = null;
            var options = ScriptHarnessProcessFixture.Options() with
            {
                OwnerFactory = value => { request = value; return owner = new WindowsScriptHarnessProcess(value); }
            };
            var run = ScriptHarnessProcess.RunAsync("fixture", "C806", "LiveRoot", ScriptHarnessProcessFixture.ScriptPath,
                options, CancellationToken.None);
            var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, run);
            var flags = owner!.QueryLimitFlagsForTesting();
            (flags & WindowsScriptHarnessProcess.JobObjectLimitBreakawayOk).ShouldBe(0u,
                "The private job must never permit explicit breakaway.");
            (flags & WindowsScriptHarnessProcess.JobObjectLimitSilentBreakawayOk).ShouldBe(0u,
                "The private job must never permit silent breakaway.");
            (await ScriptHarnessProcessFixture.CaptureAsync(run)).ShouldBeOfType<TimeoutException>();
            tree.AssertDeadBeforeEmergencySweep();
            sentinel.HasExited.ShouldBeFalse("A separately owned nested-job member outside the private job must survive its timeout.");
            WindowsScriptHarnessProcess.QueryActiveProcessesForTesting(outerJob).ShouldBeGreaterThan(0u);
        }
        finally { if (!sentinel.HasExited) sentinel.Kill(); await sentinel.WaitForExitAsync(); }
    }

    [Test]
    public async Task Private_job_handle_is_not_inherited()
    {
        var request = Request("Passing");
        using var owner = new WindowsScriptHarnessProcess(request);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var root = owner.StartAndWaitForRootAsync(deadline.Token);
        var stdout = owner.Stdout.ReadToEndAsync();
        var stderr = owner.Stderr.ReadToEndAsync();
        GetHandleInformation(owner.JobHandleForTesting, out var flags).ShouldBeTrue();
        (flags & HandleFlagInherit).ShouldBe(0u, "The private job handle must not be inheritable by a spawned child.");
        (await root.WaitAsync(deadline.Token)).ShouldBe(0);
        (await stdout.WaitAsync(deadline.Token)).ShouldContain("C487 HARNESS EXIT CODE: 0");
        (await stderr.WaitAsync(deadline.Token)).ShouldBe("");
        await owner.TerminateAsync(deadline.Token);
        await owner.ConfirmDeadAsync(deadline.Token);
        owner.CloseStreams();
        DeletePaths(request);
    }

    [Test]
    public async Task Only_standard_handles_are_inherited()
    {
        var unrelated = CreateEventW(IntPtr.Zero, true, false, null);
        if (unrelated.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateEventW failed");
        try
        {
            SetHandleInformation(unrelated, HandleFlagInherit, HandleFlagInherit).ShouldBeTrue();
            var request = Request("Passing");
            using var owner = new WindowsScriptHarnessProcess(request);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var root = owner.StartAndWaitForRootAsync(deadline.Token);
            owner.InheritedHandlesForTesting.Count.ShouldBe(3,
                "The explicit startup handle list must carry exactly child stdin/stdout/stderr.");
            owner.InheritedHandlesForTesting.ShouldNotContain(unrelated.DangerousGetHandle(),
                "An unrelated inheritable handle must never appear in the explicit startup handle list.");
            (await root.WaitAsync(deadline.Token)).ShouldBe(0);
            await owner.TerminateAsync(deadline.Token);
            await owner.ConfirmDeadAsync(deadline.Token);
            owner.CloseStreams();
            DeletePaths(request);
        }
        finally { unrelated.Dispose(); }
    }

    [Test]
    public async Task Parent_closes_child_pipe_write_handles()
    {
        var request = Request("Passing");
        using var owner = new WindowsScriptHarnessProcess(request);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var root = owner.StartAndWaitForRootAsync(deadline.Token);
        var stdout = owner.Stdout.ReadToEndAsync();
        var stderr = owner.Stderr.ReadToEndAsync();
        (await stdout.WaitAsync(deadline.Token)).ShouldContain("C487 HARNESS EXIT CODE: 0");
        (await stderr.WaitAsync(deadline.Token)).ShouldBe("");
        (await root.WaitAsync(deadline.Token)).ShouldBe(0);
        await owner.TerminateAsync(deadline.Token);
        await owner.ConfirmDeadAsync(deadline.Token);
        owner.CloseStreams();
        DeletePaths(request);
    }

    [Test]
    public async Task App_execution_alias_is_refused_before_launch()
    {
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "pwsh.exe");
        var aliasRequest = Request("Passing", executable: alias);
        var error = Should.Throw<InvalidOperationException>(() => new WindowsScriptHarnessProcess(aliasRequest));
        error.Message.ShouldContain("App Execution Alias");

        var request = Request("Passing");
        using var owner = new WindowsScriptHarnessProcess(request);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var root = owner.StartAndWaitForRootAsync(deadline.Token);
        var stdout = owner.Stdout.ReadToEndAsync();
        (await root.WaitAsync(deadline.Token)).ShouldBe(0);
        (await stdout.WaitAsync(deadline.Token)).ShouldContain("C487 HARNESS EXIT CODE: 0");
        await owner.TerminateAsync(deadline.Token);
        await owner.ConfirmDeadAsync(deadline.Token);
        owner.CloseStreams();
        DeletePaths(request);
    }

    [Test]
    public async Task Job_accounting_must_confirm_no_active_processes()
    {
        {
            ScriptProcessRequest? request = null;
            var faults = new WindowsFaultInjection { SkipTerminateKill = true };
            var options = ScriptHarnessProcessFixture.Options() with
            {
                OwnerFactory = value => { request = value; return new WindowsScriptHarnessProcess(value, faults); }
            };
            var run = ScriptHarnessProcess.RunAsync("fixture", "C806", "LiveRoot", ScriptHarnessProcessFixture.ScriptPath,
                options, CancellationToken.None);
            var tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => request, run);
            try
            {
                var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
                error.ShouldBeOfType<TimeoutException>();
                error!.Message.ShouldContain("cleanup:");
                // The coordinator's own cleanup already disposed owner's job handle by the time
                // CaptureAsync returns; query the independently retained duplicate instead.
                WindowsScriptHarnessProcess.QueryActiveProcessesForTesting(faults.JobHandleCopy!).ShouldBeGreaterThan(0u);
                Directory.Exists(tree.ResultsDirectory).ShouldBeTrue(
                    "Cleanup must not delete owned paths while a real member remains alive.");
            }
            finally { tree.EmergencyStop(); faults.JobHandleCopy?.Dispose(); DeletePaths(request!); }
        }
        {
            var request = Request("Passing");
            var faults = new WindowsFaultInjection { FailAccountingQuery = true };
            using var owner = new WindowsScriptHarnessProcess(request, faults);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var root = owner.StartAndWaitForRootAsync(deadline.Token);
            var stdout = owner.Stdout.ReadToEndAsync();
            var stderr = owner.Stderr.ReadToEndAsync();
            (await root.WaitAsync(deadline.Token)).ShouldBe(0);
            (await stdout.WaitAsync(deadline.Token)).ShouldContain("C487 HARNESS EXIT CODE: 0");
            (await stderr.WaitAsync(deadline.Token)).ShouldBe("");
            await owner.TerminateAsync(deadline.Token);
            var error = await Should.ThrowAsync<Win32Exception>(() => owner.ConfirmDeadAsync(deadline.Token));
            error.Message.ShouldContain("QueryInformationJobObject");
            owner.CloseStreams();
            DeletePaths(request);
        }
    }

    private static ScriptProcessRequest Request(string caseName, string? executable = null)
    {
        var id = Guid.NewGuid().ToString("N");
        return new ScriptProcessRequest(executable ?? ScriptHarnessProcess.ResolvePowerShell(),
            ScriptHarnessProcessFixture.ScriptPath, caseName,
            Path.Combine(Path.GetTempPath(), "c806-win-results-" + id),
            Path.Combine(Path.GetTempPath(), "c806-win-control-" + id),
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2),
            ["-HelperPath", ScriptHarnessProcessFixture.HelperPath]);
    }

    private static void DeletePaths(ScriptProcessRequest request)
    {
        if (Directory.Exists(request.ResultsDirectory)) Directory.Delete(request.ResultsDirectory, true);
        if (Directory.Exists(request.ControlDirectory)) Directory.Delete(request.ControlDirectory, true);
    }

    private static bool IsRunning(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static async Task WaitUntilExitedAsync(int pid)
    {
        var clock = Stopwatch.StartNew();
        while (IsRunning(pid) && clock.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetHandleInformation(SafeFileHandle handle, out uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "CreateEventW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateEventW(IntPtr attributes, bool manualReset, bool initialState, string? name);
}
