using System.ComponentModel;
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
            hooks.BeforeResume = (job, root) =>
            {
                assigned = true;
                ScriptHarnessWindowsProcessFixture.IsInJob(root, job).ShouldBeTrue();
                fixture.Windows!.Root!.Executing().ShouldBeTrue();
            };
            var error = await ScriptHarnessProcessFixture.CaptureAsync(fixture.Start("Passing"));
            error.ShouldBeOfType<Win32Exception>().Message.ShouldContain("injected ResumeThread failure");
            assigned.ShouldBeTrue();
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
}
