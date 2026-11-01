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

    [Test]
    public async Task Cleanup_retries_staged_hold_for_five_seconds_then_deletes()
    {
        var (root, staged, hold) = StageHeldAssembly();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var lines = new List<string>();
        try
        {
            var run = OwnedProcessDirectoryCleanup.RunAsync(
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => [],
                () =>
                {
                    attempts++;
                    // Windows refuses the staged image while any handle remains. Linux would
                    // unlink it, so this witness throws the same UnauthorizedAccessException.
                    if (!release.Task.IsCompleted)
                        throw new UnauthorizedAccessException($"Access to the path '{staged}' is denied.");
                    hold.Dispose();
                    Directory.Delete(root, true);
                },
                deleteTrace: lines.Add);
            var delay = Task.Delay(TimeSpan.FromSeconds(5));
            if (await Task.WhenAny(run, delay) != run)
                release.TrySetResult();
            await run;
            attempts.ShouldBeGreaterThan(1, "cleanup must retry while the staged file is held");
            Directory.Exists(root).ShouldBeFalse();
            lines.Any(line => line.Contains($"pid={Environment.ProcessId}", StringComparison.Ordinal)
                    && line.Contains("Antiphon.Agents.Pty.dll", StringComparison.Ordinal))
                .ShouldBeTrue("held diagnostic names the process and staged assembly");
            OwnedProcessDirectoryCleanup.StagedDeleteBudget.ShouldBe(TimeSpan.FromSeconds(75));
            Console.WriteLine($"C1022_HOLD_WITNESS attempts={attempts} pid={Environment.ProcessId} lines={lines.Count}");
        }
        finally
        {
            hold.Dispose();
            release.TrySetResult();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task Cleanup_names_holder_and_preserves_original_when_staged_file_stays_open()
    {
        OwnedProcessDirectoryCleanup.StagedDeleteBudget.ShouldBe(TimeSpan.FromSeconds(75));
        var (root, staged, hold) = StageHeldAssembly();
        var original = new InvalidOperationException("configured body");
        var lines = new List<string>();
        Exception? error = null;
        try
        {
            await OwnedProcessDirectoryCleanup.RunAsync(
                () => Task.FromException(original),
                () => Task.CompletedTask,
                () => [],
                () => throw new UnauthorizedAccessException($"Access to the path '{staged}' is denied."),
                TimeSpan.FromSeconds(2),
                lines.Add);
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            hold.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        var aggregate = error.ShouldBeOfType<AggregateException>();
        aggregate.InnerExceptions[0].ShouldBeSameAs(original);
        var cleanup = aggregate.InnerExceptions[1];
        cleanup.Message.ShouldContain($"pid={Environment.ProcessId}");
        cleanup.Message.ShouldContain("Antiphon.Agents.Pty.dll");
        cleanup.Message.ShouldContain("budgetSeconds=2");
        cleanup.InnerException.ShouldBeOfType<UnauthorizedAccessException>();
        lines.Any(line => line.Contains($"pid={Environment.ProcessId}", StringComparison.Ordinal))
            .ShouldBeTrue("each denied attempt records the holder");
        Console.WriteLine($"C1022_HOLD_FOREVER pid={Environment.ProcessId} error={cleanup.Message}");
    }

    private static (string Root, string Staged, FileStream Hold) StageHeldAssembly()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1022-hold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var staged = Path.Combine(root, "Antiphon.Agents.Pty.dll");
        File.WriteAllText(staged, "staged-host");
        var hold = new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.None);
        return (root, staged, hold);
    }
}
