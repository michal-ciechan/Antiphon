using Win32Exception = System.ComponentModel.Win32Exception;
using Microsoft.Win32.SafeHandles;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ScriptHarnessWindowsOwnershipTests
{
    [Test]
    public Task Child_is_assigned_before_first_instruction()
    {
        var hooks = new WindowsScriptHarnessHooks();
        return ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
        {
            var windows = fixture.Windows!;
            var beforeAssign = false;
            var beforeResume = false;
            hooks.BeforeAssign = (job, root) =>
            {
                beforeAssign = true;
                (hooks.CreateFlags & 0x4).ShouldBe(0x4u);
                ScriptHarnessWindowsProcessFixture.IsInJob(root, job).ShouldBeFalse();
                windows.Root!.Executing().ShouldBeTrue();
                File.Exists(Path.Combine(windows.DirectoryPath, "first-instruction")).ShouldBeFalse();
            };
            hooks.BeforeResume = (job, root) =>
            {
                beforeResume = true;
                ScriptHarnessWindowsProcessFixture.IsInJob(root, job).ShouldBeTrue();
                (ScriptHarnessWindowsProcessFixture.ReadJobLimitFlags(job) & 0x2000).ShouldBe(0x2000u);
                File.Exists(Path.Combine(windows.DirectoryPath, "first-instruction")).ShouldBeFalse();
                hooks.Calls.ShouldNotContain("resume");
                hooks.Calls.ShouldContain("assigned");
            };
            var result = await fixture.Start("Passing");
            result.ExitCode.ShouldBe(0);
            result.Stdout.ShouldContain("PASS C806 C806 fixture passed");
            hooks.StdoutEof.ShouldBeTrue();
            hooks.StderrEof.ShouldBeTrue();
            beforeAssign.ShouldBeTrue();
            beforeResume.ShouldBeTrue();
            hooks.PreviousSuspendCount.ShouldBe(1u);
            Array.IndexOf(hooks.Calls, "resume").ShouldBeGreaterThan(Array.IndexOf(hooks.Calls, "assigned"));
            File.ReadAllText(Path.Combine(windows.DirectoryPath, "first-instruction")).ShouldBe(windows.Nonce);
            fixture.AssertClean();
        }, hooks: hooks, requireWindows: true);
    }

    [Test]
    public Task Assignment_failure_never_resumes_child()
    {
        var hooks = new WindowsScriptHarnessHooks { FailAssignment = true };
        return ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
        {
            var created = false;
            hooks.BeforeAssign = (job, root) =>
            {
                created = true;
                ScriptHarnessWindowsProcessFixture.IsInJob(root, job).ShouldBeFalse();
                fixture.Windows!.Root!.Executing().ShouldBeTrue();
            };
            var error = await ScriptHarnessProcessFixture.CaptureAsync(fixture.Start("Passing"));
            error.ShouldBeOfType<Win32Exception>().Message.ShouldContain("injected AssignProcessToJobObject refusal");
            created.ShouldBeTrue();
            hooks.Calls.Count(call => call == "resume").ShouldBe(0);
            AssertLaunchUnwound(fixture, hooks);
        }, hooks: hooks, requireWindows: true);
    }

    [Test]
    public Task Resume_failure_terminates_suspended_child()
    {
        var hooks = new WindowsScriptHarnessHooks { FailResume = true };
        return ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
        {
            var assigned = false;
            var deadBeforeClose = false;
            hooks.BeforeResume = (job, root) =>
            {
                assigned = true;
                ScriptHarnessWindowsProcessFixture.IsInJob(root, job).ShouldBeTrue();
                fixture.Windows!.Root!.Executing().ShouldBeTrue();
            };
            hooks.BeforeCloseJob = () =>
            {
                fixture.Windows!.Root!.Executing().ShouldBeFalse("Explicit setup unwind must kill the root before job disposal.");
                deadBeforeClose = true;
            };
            var error = await ScriptHarnessProcessFixture.CaptureAsync(fixture.Start("Passing"));
            error.ShouldBeOfType<Win32Exception>().Message.ShouldContain("injected ResumeThread failure");
            assigned.ShouldBeTrue();
            deadBeforeClose.ShouldBeTrue();
            hooks.Calls.Count(call => call == "resume").ShouldBe(1);
            hooks.Calls.ShouldNotContain("resumed");
            var stop = Array.IndexOf(hooks.Calls, "terminate-job");
            stop.ShouldBeGreaterThanOrEqualTo(0);
            Array.IndexOf(hooks.Calls, "close-job").ShouldBeGreaterThan(stop);
            Array.IndexOf(hooks.Calls, "terminate-root").ShouldBeGreaterThan(stop);
            AssertLaunchUnwound(fixture, hooks);
        }, hooks: hooks, requireWindows: true);
    }

    private static void AssertLaunchUnwound(ScriptHarnessProcessFixture.Invocation fixture, WindowsScriptHarnessHooks hooks)
    {
        var windows = fixture.Windows!;
        windows.Root.ShouldNotBeNull();
        windows.Root.Join(TimeSpan.FromSeconds(2));
        windows.Root.Executing().ShouldBeFalse("Suspended root must die before the emergency sweep.");
        File.Exists(Path.Combine(windows.DirectoryPath, "first-instruction")).ShouldBeFalse();
        hooks.Handles.Count.ShouldBe(9);
        foreach (var (name, handle) in hooks.Handles) handle.IsClosed.ShouldBeTrue("Partial handle remained open: " + name);
        Directory.Exists(fixture.Request!.ResultsDirectory).ShouldBeFalse();
    }

    [Test]
    public Task Closing_private_job_kills_owned_tree()
    {
        var hooks = new WindowsScriptHarnessHooks();
        var queried = false;
        hooks.BeforeResume = (job, _) =>
        {
            (ScriptHarnessWindowsProcessFixture.ReadJobLimitFlags(job) & 0x2000).ShouldBe(0x2000u,
                "Independent live job readback must contain kill-on-close.");
            queried = true;
        };
        return ScriptHarnessWindowsProcessFixture.WithOwnerAsync("LiveRoot", hooks, async owned =>
        {
            var tree = await owned.WaitReadyAsync();
            tree.AllExecuting().ShouldBeTrue();
            queried.ShouldBeTrue();
            hooks.Calls.ShouldNotContain("terminate-job");
            owned.DisposeOwner();
            await WaitForAsync(() => !tree.Root.Executing() && !tree.Child.Executing() && !tree.Grandchild.Executing(),
                TimeSpan.FromSeconds(2), "Closing only the private job must signal the whole retained tree.");
            hooks.Calls.ShouldNotContain("terminate-job", "This guard must exercise disposal without explicit termination.");
            owned.DeletePathsAfterConfirmedDeath();
            tree.AssertDeadBeforeEmergencySweep();
        }, drainStreams: false);
    }

    [Test]
    public Task Nested_job_timeout_kills_owned_descendants_only()
    {
        var hooks = new WindowsScriptHarnessHooks();
        return ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
        {
            using var outer = new ScriptHarnessWindowsProcessFixture.OuterJob();
            SafeFileHandle? privateJob = null;
            hooks.BeforeCreate = flags => (flags & 0x01000000).ShouldBe(0u,
                "Actual native create flags must forbid breakaway; never forward an unsafe launch.");
            hooks.BeforeAssign = (job, root) =>
            {
                privateJob = job;
                outer.Assign(root);
                ScriptHarnessWindowsProcessFixture.IsInJob(root, outer.Handle).ShouldBeTrue();
            };
            hooks.BeforeResume = (job, root) =>
            {
                ScriptHarnessWindowsProcessFixture.IsInJob(root, job).ShouldBeTrue();
                ScriptHarnessWindowsProcessFixture.IsInJob(root, outer.Handle).ShouldBeTrue();
                AssertNoBreakaway(job);
                AssertNoBreakaway(outer.Handle);
                outer.Contains(outer.Sentinel).ShouldBeTrue();
                outer.Sentinel.IsInJob(job).ShouldBeFalse();
            };
            var run = fixture.Start("LiveRoot");
            var tree = await fixture.WaitReadyAsync(run);
            privateJob.ShouldNotBeNull();
            tree.Child.WindowsObservation!.IsInJob(privateJob).ShouldBeTrue();
            tree.Grandchild.WindowsObservation!.IsInJob(privateJob).ShouldBeTrue();
            outer.Sentinel.Executing().ShouldBeTrue();
            var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
            error.ShouldBeOfType<TimeoutException>().Message.ShouldContain("execution budget");
            fixture.AssertClean();
            outer.Sentinel.Executing().ShouldBeTrue("Private-job timeout must spare the outer-only sentinel.");
            (hooks.CreateFlags & 0x01000000).ShouldBe(0u);
        }, hooks: hooks, requireWindows: true);
    }

    [Test]
    public Task Private_job_handle_is_not_inherited()
    {
        var hooks = new WindowsScriptHarnessHooks();
        return ScriptHarnessWindowsProcessFixture.WithOwnerAsync("HandleProbe", hooks, async owned =>
        {
            var receipt = await owned.Windows.WaitProbeAsync(owned.RootExit);
            bool.Parse(receipt[1]).ShouldBeFalse("Child cannot query the transported private-job handle.");
            bool.Parse(receipt[2]).ShouldBeFalse("Child cannot retain the transported private job.");
            owned.Windows.Root!.Executing().ShouldBeTrue();
            Handle(hooks, "job").IsClosed.ShouldBeFalse("Parent still owns the private job during the child probe.");
        }, configure: windows => hooks.BeforeResume = (job, _) =>
        {
            (ScriptHarnessWindowsProcessFixture.ReadHandleFlags(job) & 1).ShouldBe(0u,
                "Actual private job handle inheritance flag must be false.");
            windows.WriteProbeInput(job);
        });
    }

    [Test]
    public async Task Only_standard_handles_are_inherited()
    {
        var hooks = new WindowsScriptHarnessHooks();
        var eventName = "Local\\c1047-event-" + Guid.NewGuid().ToString("N");
        using var unrelatedEvent = ScriptHarnessWindowsProcessFixture.CreateInheritableEvent(eventName);
        (ScriptHarnessWindowsProcessFixture.ReadHandleFlags(unrelatedEvent) & 1).ShouldBe(1u);
        await ScriptHarnessWindowsProcessFixture.WithOwnerAsync("HandleProbe", hooks, async owned =>
        {
            var receipt = await owned.Windows.WaitProbeAsync(owned.RootExit);
            bool.Parse(receipt[3]).ShouldBeFalse("Child cannot signal the unrelated inheritable event.");
            ScriptHarnessWindowsProcessFixture.EventIsSignaled(unrelatedEvent).ShouldBeFalse();
            var actualStandard = receipt.Skip(4).Select(value => new IntPtr(long.Parse(value))).ToArray();
            actualStandard.ShouldBe(hooks.StartupHandles);
            owned.Windows.Root!.Executing().ShouldBeTrue();
        }, configure: windows => hooks.BeforeResume = (job, _) =>
        {
            var expected = new[] { Handle(hooks, "stdin-read"), Handle(hooks, "stdout-write"), Handle(hooks, "stderr-write") }
                .Select(handle => handle.DangerousGetHandle()).ToArray();
            hooks.StartupHandles.ShouldBe(expected, "Actual startup handle list must contain exactly child stdin/stdout/stderr.");
            hooks.StartupHandles.ShouldNotContain(Handle(hooks, "stdout-read").DangerousGetHandle());
            hooks.StartupHandles.ShouldNotContain(Handle(hooks, "stderr-read").DangerousGetHandle());
            hooks.StartupHandles.ShouldNotContain(Handle(hooks, "stdin-write").DangerousGetHandle());
            hooks.StartupHandles.ShouldNotContain(unrelatedEvent.DangerousGetHandle());
            windows.WriteProbeInput(job, unrelatedEvent, eventName);
        });
    }

    [Test]
    public Task Parent_closes_child_pipe_write_handles()
    {
        var hooks = new WindowsScriptHarnessHooks();
        return ScriptHarnessWindowsProcessFixture.WithOwnerAsync("Silent", hooks, async owned =>
        {
            var tree = await owned.WaitReadyAsync();
            // Root exits after the observation barrier. Both children close both
            // selected writers but stay alive; the parent owner remains open.
            await owned.RootExit.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForAsync(() => hooks.StdoutEof, TimeSpan.FromSeconds(2),
                "Stdout EOF must arrive with parent owner and silent children alive.");
            AssertLiveWriterClosure(owned, tree, hooks, "stdout-write");
            await WaitForAsync(() => hooks.StderrEof, TimeSpan.FromSeconds(2),
                "Stderr EOF must arrive independently with parent owner and silent children alive.");
            AssertLiveWriterClosure(owned, tree, hooks, "stderr-write");
            owned.StdoutDrain.IsCompletedSuccessfully.ShouldBeTrue();
            owned.StderrDrain.IsCompletedSuccessfully.ShouldBeTrue();
        });
    }

    [Test]
    public Task App_execution_alias_is_refused_before_launch()
    {
        return ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
        {
            var windows = fixture.Windows!;
            var installed = ScriptHarnessProcessFixture.ResolveInstalledPowerShell();
            var shaped = Path.Combine(windows.DirectoryPath, "WindowsApps", "pwsh.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(shaped)!);
            File.WriteAllText(shaped, "Alias-shaped fixture; must never execute.");
            foreach (var reparse in new[] { false, true })
            {
                var path = reparse ? installed : shaped;
                Path.IsPathFullyQualified(path).ShouldBeTrue();
                File.Exists(path).ShouldBeTrue();
                var classified = false;
                var attempts = 0;
                var refused = new WindowsScriptHarnessHooks
                {
                    ExecutableAttributes = executable =>
                    {
                        executable.ShouldBe(installed);
                        classified = true;
                        return File.GetAttributes(executable) | FileAttributes.ReparsePoint;
                    },
                    BeforeCreate = _ =>
                    {
                        attempts++;
                        throw new InvalidOperationException("Guard intercepted an unsafe alias launch.");
                    }
                };
                var request = new ScriptProcessRequest(path, ScriptHarnessProcessFixture.ScriptPath, "Passing",
                    Path.Combine(windows.DirectoryPath, "results"), Path.Combine(windows.DirectoryPath, "control"),
                    TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), windows.Arguments());
                var error = Should.Throw<InvalidOperationException>(() =>
                {
                    using var owner = new WindowsScriptHarnessProcess(request, refused);
                });
                error.Message.ShouldContain("real pwsh.exe path, not an App Execution Alias");
                error.Message.ShouldContain("ExecutablePath");
                classified.ShouldBe(reparse, "WindowsApps path and reparse classification are independent refusal arms.");
                attempts.ShouldBe(0, "Alias refusal must precede any native launch attempt.");
                refused.Calls.ShouldNotContain("create");
                refused.Handles.ShouldBeEmpty();
            }
            var result = await fixture.Start("Passing");
            result.ExitCode.ShouldBe(0);
            result.Stdout.ShouldContain("PASS C806 C806 fixture passed");
            windows.Hooks.Calls.ShouldContain("create", "The real installed executable is the positive ordinary control.");
            fixture.AssertClean();
        }, requireWindows: true);
    }

    [Test]
    public async Task Job_accounting_must_confirm_no_active_processes()
    {
        foreach (var queryFailure in new[] { false, true })
        {
            var hooks = new WindowsScriptHarnessHooks
            {
                AcknowledgeTerminationWithoutKill = !queryFailure,
                FailAccounting = queryFailure
            };
            await ScriptHarnessProcessFixture.WithInvocationAsync(async fixture =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                hooks.BeforeResume = (_, _) =>
                {
                    Directory.CreateDirectory(fixture.Request!.ControlDirectory);
                    File.WriteAllText(Path.Combine(fixture.Request.ControlDirectory, "accounting-evidence"), fixture.Windows!.Nonce);
                };
                // Silent exits the root and closes both streams with live children.
                // A false death confirmation therefore permits genuine success/delete.
                var run = fixture.Start("Silent");
                var observedBeforeClose = false;
                var independentReap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var tree = await fixture.WaitReadyAsync(run, ready =>
                {
                    var job = Handle(hooks, "job");
                    ready.Child.WindowsObservation!.IsInJob(job).ShouldBeTrue();
                    ready.Grandchild.WindowsObservation!.IsInJob(job).ShouldBeTrue();
                    ScriptHarnessWindowsProcessFixture.ReadActiveJobMembers(job).ShouldBeGreaterThanOrEqualTo(2u);
                    hooks.BeforeAccounting = queriedJob =>
                    {
                        hooks.Calls.ShouldNotContain("close-job", "Accounting evidence must precede job disposal.");
                        if (!queryFailure)
                        {
                            ScriptHarnessWindowsProcessFixture.ReadActiveJobMembers(queriedJob).ShouldBeGreaterThanOrEqualTo(2u);
                            ready.Child.Executing().ShouldBeTrue();
                            ready.Grandchild.Executing().ShouldBeTrue();
                        }
                        observedBeforeClose = true;
                    };
                    hooks.BeforeCloseJob = () =>
                    {
                        try
                        {
                            // Serialize fixture reaping before kill-on-close.
                            // Query-failure arm already requested real termination;
                            // only the deliberately acknowledged live arm needs kills.
                            var reap = System.Diagnostics.Stopwatch.StartNew();
                            if (!queryFailure)
                                foreach (var process in new[] { ready.Child, ready.Grandchild })
                                    if (process.Executing()) process.WindowsObservation!.Terminate();
                            foreach (var process in new[] { ready.Root, ready.Child, ready.Grandchild })
                                process.WindowsObservation!.Join(TimeSpan.FromSeconds(5) - reap.Elapsed);
                            independentReap.TrySetResult();
                        }
                        catch (Exception error)
                        {
                            independentReap.TrySetException(error);
                            throw;
                        }
                    };
                });
                var error = await ScriptHarnessProcessFixture.CaptureAsync(run);
                error.ShouldBeOfType<IOException>().Message.ShouldContain("death confirmation:");
                if (queryFailure)
                {
                    error.Message.ShouldContain("QueryInformationJobObject");
                    hooks.Calls.ShouldContain("query-accounting-failed");
                }
                else
                {
                    hooks.Calls.ShouldContain("terminate-ack-without-kill");
                    hooks.ActiveMemberObservations.ShouldNotBeEmpty();
                    hooks.ActiveMemberObservations.All(count => count >= 2).ShouldBeTrue();
                }
                observedBeforeClose.ShouldBeTrue("Accounting evidence must precede kill-on-close.");
                hooks.StdoutEof.ShouldBeTrue();
                hooks.StderrEof.ShouldBeTrue();
                clock.Elapsed.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
                var request = fixture.Request!;
                Directory.Exists(request.ResultsDirectory).ShouldBeTrue("Uncertain cleanup must retain results.");
                Directory.Exists(request.ControlDirectory).ShouldBeTrue("Uncertain cleanup must retain control evidence.");
                error.Message.ShouldContain(request.ResultsDirectory);
                error.Message.ShouldContain(request.ControlDirectory);
                // The expired cleanup token may return before Task.Run(Dispose)
                // runs. W10 owns this deliberate uncertainty: independently reap
                // and join exact known handles under the existing five-second cap.
                var join = System.Diagnostics.Stopwatch.StartNew();
                await independentReap.Task.WaitAsync(TimeSpan.FromSeconds(5));
                foreach (var process in new[] { tree.Root, tree.Child, tree.Grandchild })
                    process.WindowsObservation!.Join(TimeSpan.FromSeconds(5) - join.Elapsed);
                await WaitForAsync(() => hooks.Handles.All(entry => entry.Handle.IsClosed),
                    TimeSpan.FromSeconds(5) - join.Elapsed, "The queued adapter disposal must finish within fixture cleanup.");
                Array.IndexOf(hooks.Calls, "close-job").ShouldBeGreaterThan(Array.IndexOf(hooks.Calls, "query-accounting"));
                fixture.Windows!.AssertStoppedBeforeDispose();
                Directory.Delete(request.ResultsDirectory, true);
                Directory.Delete(request.ControlDirectory, true);
            }, hooks: hooks, requireWindows: true);
        }
    }

    private static void AssertLiveWriterClosure(ScriptHarnessWindowsProcessFixture.DirectOwner owned,
        ScriptHarnessProcessFixture.ObservedTree tree, WindowsScriptHarnessHooks hooks, string writer)
    {
        tree.Child.Executing().ShouldBeTrue();
        tree.Grandchild.Executing().ShouldBeTrue();
        Handle(hooks, "job").IsClosed.ShouldBeFalse();
        hooks.Calls.ShouldNotContain("dispose");
        hooks.Calls.ShouldNotContain("terminate-job");
        Handle(hooks, writer).IsClosed.ShouldBeTrue("Parent must close its child pipe writer before disposal.");
        owned.Windows.Root!.Executing().ShouldBeFalse();
    }

    private static SafeFileHandle Handle(WindowsScriptHarnessHooks hooks, string name) =>
        hooks.Handles.Single(entry => entry.Name == name).Handle;

    private static void AssertNoBreakaway(SafeFileHandle job)
    {
        var flags = ScriptHarnessWindowsProcessFixture.ReadJobLimitFlags(job);
        (flags & 0x800).ShouldBe(0u, "Independent job flags must forbid explicit breakaway.");
        (flags & 0x1000).ShouldBe(0u, "Independent job flags must forbid silent breakaway.");
    }

    private static async Task WaitForAsync(Func<bool> observed, TimeSpan budget, string assertion)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!observed() && clock.Elapsed < budget) await Task.Delay(20);
        observed().ShouldBeTrue(assertion);
    }
}
