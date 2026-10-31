using System.Diagnostics;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public partial class C1022BackendLaunchTests
{
    [Test]
    [Arguments(nameof(Unset_owned_host_runs_modern), false)]
    [Arguments(nameof(Configured_owned_host_runs_modern), false)]
    [Arguments(nameof(Declared_modern_overrides_inherited_inbox), false)]
    [Arguments(nameof(Unset_owned_host_runs_modern), true)]
    [Arguments(nameof(Configured_owned_host_runs_modern), true)]
    [Arguments(nameof(Declared_modern_overrides_inherited_inbox), true)]
    public async Task Cleanup_joins_owned_child_even_when_body_or_stop_fails(string sibling, bool stopFails)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1022-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            WorkingDirectory = root,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
                     "[Console]::WriteLine('READY'); [void][Console]::ReadLine()" })
            start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        try
        {
            (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe("READY");
            var original = new InvalidOperationException(sibling + (stopFails ? " stop" : " body"));
            var identity = TestOwnedPtyHost.CaptureProcess(child.Id);
            using var identityHandle = identity.Handle!;
            var owned = new TestOwnedPtyHost.Owned(root, Guid.NewGuid(), "unused", identity, false, true);
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var deleted = false;
            var run = OwnedProcessDirectoryCleanup.RunAsync(
                () => stopFails ? Task.CompletedTask : Task.FromException(original),
                () =>
                {
                    stopped.SetResult();
                    return stopFails ? Task.FromException(original) : Task.CompletedTask;
                },
                () => OwnedProcessDirectoryCleanup.Capture([owned]),
                () =>
                {
                    child.HasExited.ShouldBeTrue("directory-delete-requires-owned-process-exit");
                    Directory.Delete(root, true);
                    deleted = true;
                });
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // The child is held at stdin, so completing cleanup now is deterministically premature.
            var premature = run.IsCompleted;
            var deletedBeforeExit = deleted;
            await child.StandardInput.WriteLineAsync("release");
            Exception? error = null;
            try { await run; } catch (Exception ex) { error = ex; }
            premature.ShouldBeFalse("cleanup-must-join-even-after-" + (stopFails ? "stop" : "body") + "-failure");
            deletedBeforeExit.ShouldBeFalse();
            child.HasExited.ShouldBeTrue("owned-child-physically-exited");
            deleted.ShouldBe(!stopFails, "stop errors retain the directory; successful stop deletes after exit");
            error.ShouldBeSameAs(original, "original failure is preserved");
            Console.WriteLine($"C1022_CLEANUP_WITNESS sibling={sibling} stopFails={stopFails} pid={child.Id} exited=true deleted={deleted}");
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
