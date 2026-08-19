using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
[NotInParallel("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class SessionRunnerRuntimeTests
{
    [Test]
    public async Task Session_runner_starts_shell_accepts_input_and_keeps_buffer()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-runner-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = Path.Combine(tempRoot, "logs")
            }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();

        try
        {
            var session = await runtime.StartAsync(
                new RunnerLaunchRequest(
                    sessionId,
                    Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    ["/d", "/q", "/k", "@echo off & prompt $G"],
                    new Dictionary<string, string>(),
                    tempRoot,
                    120,
                    30),
                CancellationToken.None);

            session.SessionId.ShouldBe(sessionId);
            session.Status.ShouldBe("Running");

            await runtime.SendInputAsync(sessionId, "echo SESSION_RUNNER_OK\r", CancellationToken.None);
            await WaitUntilAsync(() =>
                runtime.GetBuffer(sessionId).Buffer.Contains("SESSION_RUNNER_OK", StringComparison.OrdinalIgnoreCase));

            var buffer = runtime.GetBuffer(sessionId);
            buffer.LastSequence.ShouldBeGreaterThan(0);
            buffer.Buffer.ShouldContain("SESSION_RUNNER_OK", Case.Insensitive);

            var killed = await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
            killed.Status.ShouldBe("Exited");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Session_id_can_be_relaunched_after_exit_but_not_while_running()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-runner-relaunch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = Path.Combine(tempRoot, "logs")
            }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        RunnerLaunchRequest Request() => new(
            sessionId,
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/q", "/k", "@echo off & prompt $G"],
            new Dictionary<string, string>(),
            tempRoot,
            120,
            30);

        try
        {
            var first = await runtime.StartAsync(Request(), CancellationToken.None);
            first.Status.ShouldBe("Running");

            // A live session keeps its id reserved.
            await Should.ThrowAsync<InvalidOperationException>(() =>
                runtime.StartAsync(Request(), CancellationToken.None));

            (await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None))
                .Status.ShouldBe("Exited");

            // After exit the same id is relaunchable (claude --resume reuses the original id).
            var second = await runtime.StartAsync(Request(), CancellationToken.None);
            second.SessionId.ShouldBe(sessionId);
            second.Status.ShouldBe("Running");

            await runtime.SendInputAsync(sessionId, "echo RELAUNCH_OK\r", CancellationToken.None);
            await WaitUntilAsync(() =>
                runtime.GetBuffer(sessionId).Buffer.Contains("RELAUNCH_OK", StringComparison.OrdinalIgnoreCase));

            await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    /// <summary>
    /// CARD-0086: a cancelled StartAsync still runs Process.Start (not ct-gated) and used to
    /// detach from the empty host in the outer catch. The host must be gone in a few seconds —
    /// not after the 30s launch-timeout backstop.
    /// </summary>
    [Test]
    public async Task Cancelled_StartAsync_after_the_host_exists_leaves_no_pty_host()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-runner-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = Path.Combine(tempRoot, "logs")
            }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sw = Stopwatch.StartNew();

        try
        {
            await Should.ThrowAsync<OperationCanceledException>(() =>
                runtime.StartAsync(CmdRequest(sessionId, tempRoot), cts.Token));

            var leftover = await WaitUntilNoHostsUnderAsync(tempRoot, TimeSpan.FromSeconds(5));
            leftover.ShouldBeEmpty(
                "cancelled StartAsync leaked Antiphon.PtyHost pid(s) "
                + string.Join(", ", leftover));
            sw.Elapsed.ShouldBeLessThan(
                TimeSpan.FromSeconds(10),
                "cleanup must not wait out the 30s host launch timeout");
        }
        finally
        {
            foreach (var pid in HostPidsUnder(tempRoot))
                TryKill(pid);
            await runtime.DisposeAsync();
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    /// <summary>
    /// CARD-0086 / CARD-0056 pin: once the host is connected, a Launch the child rejects must
    /// still kill the host. The inner catch already does this; the test is the lock so a later
    /// edit cannot drop it.
    /// </summary>
    [Test]
    public async Task StartAsync_that_fails_after_the_host_exists_leaves_no_pty_host()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-runner-badexe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = Path.Combine(tempRoot, "logs")
            }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        var missingExe = Path.Combine(tempRoot, "no-such-exe-card0086.exe");
        var sw = Stopwatch.StartNew();

        try
        {
            // Missing exe: PtyAgentRunner.StartAsync throws, the host RequestExit's, and the
            // client may see either "Host launch failed" or EndOfStream if the pipe drops first.
            // Either way StartAsync must fail — the pin is that the host is gone, not the
            // exception type.
            await Should.ThrowAsync<Exception>(() =>
                runtime.StartAsync(
                    new RunnerLaunchRequest(
                        sessionId,
                        missingExe,
                        [],
                        new Dictionary<string, string>(),
                        tempRoot,
                        120,
                        30),
                    CancellationToken.None));

            var leftover = await WaitUntilNoHostsUnderAsync(tempRoot, TimeSpan.FromSeconds(5));
            leftover.ShouldBeEmpty(
                "failed StartAsync leaked Antiphon.PtyHost pid(s) "
                + string.Join(", ", leftover));
            sw.Elapsed.ShouldBeLessThan(
                TimeSpan.FromSeconds(10),
                "cleanup must not wait out the 30s host launch timeout");
        }
        finally
        {
            foreach (var pid in HostPidsUnder(tempRoot))
                TryKill(pid);
            await runtime.DisposeAsync();
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static RunnerLaunchRequest CmdRequest(Guid sessionId, string tempRoot) => new(
        sessionId,
        Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        ["/d", "/q", "/k", "@echo off & prompt $G"],
        new Dictionary<string, string>(),
        tempRoot,
        120,
        30);

    private static int[] HostPidsUnder(string root)
    {
        if (string.IsNullOrEmpty(root))
            return [];

        var found = new List<int>();
        foreach (var process in Process.GetProcessesByName("Antiphon.PtyHost"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    found.Add(process.Id);
            }
            catch
            {
                // Access denied / exited mid-scan.
            }
            finally
            {
                process.Dispose();
            }
        }

        return found.ToArray();
    }

    private static async Task<int[]> WaitUntilNoHostsUnderAsync(string root, TimeSpan bound)
    {
        var appearUntil = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        int[] leftover;
        do
        {
            leftover = HostPidsUnder(root);
            if (leftover.Length > 0)
                break;
            await Task.Delay(50);
        } while (DateTime.UtcNow < appearUntil);

        if (leftover.Length == 0)
            return leftover;

        var deadline = DateTime.UtcNow + bound;
        do
        {
            leftover = HostPidsUnder(root);
            if (leftover.Length == 0)
                return leftover;
            await Task.Delay(100);
        } while (DateTime.UtcNow < deadline);

        return leftover;
    }

    private static void TryKill(int pid)
    {
        if (pid <= 0)
            return;
        try
        {
            Process.GetProcessById(pid).Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(25);
        }

        predicate().ShouldBeTrue();
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for PTY/session runner test directories.
        }
    }
}
