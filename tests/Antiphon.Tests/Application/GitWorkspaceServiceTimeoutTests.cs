using System.Diagnostics;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0223: a git child that outlives <c>Git:TimeoutSeconds</c> is killed, with its whole
/// process tree, and <see cref="GitWorkspaceService.RunAsync"/> answers <c>(-1, "", "timeout")</c>
/// on the deadline rather than after the child would have ended on its own.
/// The executable under test is a shell standing in for git (<c>GitSettings.ExecutableName</c>),
/// so this class carries no <c>GitIntegration</c> category and no git probe; it spawns a process,
/// so it carries the assembly's spawn limiter. The deadline is the product's own real timer
/// (<c>RunCoreAsync</c> has no <c>TimeProvider</c> seam); the test polls for the child's exit and
/// never sleeps for a fixed interval.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GitWorkspaceServiceTimeoutTests
{
    [Test]
    [Timeout(60_000)]
    public async Task Timed_out_git_child_is_killed_with_its_tree()
    {
        var tempDir = Directory.CreateTempSubdirectory("antiphon-c223-");
        var pidFile = Path.Combine(tempDir.FullName, "sleeper.pid");
        var (exe, args, timeoutSeconds) = Sleeper(pidFile);
        var service = new GitWorkspaceService(
            NullLogger<GitWorkspaceService>.Instance,
            new GitProcessGate(1),
            Options.Create(new GitSettings { ExecutableName = exe, TimeoutSeconds = timeoutSeconds }));
        var pid = 0;
        try
        {
            var sw = Stopwatch.StartNew();
            var result = await service.RunAsync(tempDir.FullName, CancellationToken.None, args);
            sw.Stop();

            result.ShouldBe((-1, "", "timeout"));

            // The sleeper writes its pid before it starts sleeping; a missing or unparsable pid file
            // means the witness never came into being, which is a failure, not a vacuous pass.
            File.Exists(pidFile).ShouldBeTrue(
                $"sleeper never recorded its pid before the {timeoutSeconds} s deadline (no file at {pidFile})");
            var recorded = File.ReadAllText(pidFile).Trim();
            int.TryParse(recorded, out pid).ShouldBeTrue($"sleeper pid file holds '{recorded}', not a pid");
            pid.ShouldBeGreaterThan(0, $"sleeper pid file holds '{recorded}', not a positive pid");

            (await GoneWithinAsync(pid, TimeSpan.FromSeconds(2))).ShouldBeTrue(
                $"sleeper pid {pid} is still alive 2 s after RunAsync returned on the deadline; the timed-out child (tree) was not killed");

            sw.Elapsed.ShouldBeLessThan(
                TimeSpan.FromSeconds(timeoutSeconds + 5),
                $"RunAsync took {sw.Elapsed}; it should return on the {timeoutSeconds} s deadline, not after the sleeper's 60 s");
        }
        finally
        {
            TryKill(pid);
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Linux: <c>sh -c</c> forks <c>sleep 60</c> into the background, records the grandchild's pid
    /// and waits, so the recorded pid witnesses <c>Kill(entireProcessTree: true)</c> and not only
    /// the direct child. Windows: pwsh records its own pid then sleeps; start-up is the slow part,
    /// so the deadline is 3 s to keep the pid on disk before it fires. The direct child is the
    /// witness there.
    /// </summary>
    private static (string Exe, string[] Args, int TimeoutSeconds) Sleeper(string pidFile)
    {
        if (OperatingSystem.IsWindows())
        {
            var escaped = pidFile.Replace("'", "''");
            return (
                "pwsh",
                ["-NoProfile", "-NonInteractive", "-Command", $"$PID | Set-Content -LiteralPath '{escaped}'; Start-Sleep -Seconds 60"],
                3);
        }

        return ("sh", ["-c", "sleep 60 & echo $! > \"$1\"; wait", "sleeper", pidFile], 1);
    }

    private static async Task<bool> GoneWithinAsync(int pid, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            if (!IsAlive(pid))
                return true;
            if (deadline.Elapsed >= timeout)
                return false;
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// After the tree kill the Linux grandchild is reparented to the container's PID 1, which may
    /// not reap it; <c>/proc/&lt;pid&gt;/stat</c> state <c>Z</c>/<c>X</c> or a missing entry counts
    /// as gone. <see cref="Process.GetProcessById"/> would report a zombie as present.
    /// </summary>
    private static bool IsAlive(int pid)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        string stat;
        try
        {
            stat = File.ReadAllText($"/proc/{pid}/stat");
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        // "<pid> (<comm>) <state> ..."; comm may itself contain spaces or parentheses, so the
        // state is the first field after the last ')'.
        var close = stat.LastIndexOf(')');
        if (close < 0 || close + 2 >= stat.Length)
            return false;
        var state = stat[close + 2];
        return state is not ('Z' or 'X');
    }

    private static void TryKill(int pid)
    {
        if (pid <= 0)
            return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone, or never ours to begin with.
        }
    }
}
