using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// CARD-0490 S1: real Linux apphost, Unix pipe, setsid, stdio-to-/dev/null, and canceled-launch
/// cleanup. Windows skips; Linux must execute every method.
/// </summary>
[Category("PtyHost")]
[NotInParallel("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class LinuxPtyHostLauncherTests
{
    private static void RequireLinux()
    {
        if (!OperatingSystem.IsLinux())
            throw new SkipTestException("Linux native pty-host methods execute on Linux only.");
    }

    [Test]
    public async Task Shadow_host_exchanges_bytes_and_exits()
    {
        RequireLinux();
        var tempRoot = NewRoot();
        var owned = new List<int>();
        try
        {
            var launcher = new PtyHostLauncher(new ShadowCopyStore(Path.Combine(tempRoot, "bin")), AppContext.BaseDirectory);
            File.Exists(Path.Combine(launcher.CurrentShadowDir, PtyHostLauncher.HostExeName)).ShouldBeTrue();
            File.Exists(Path.Combine(launcher.CurrentShadowDir, "libporta_pty.so")).ShouldBeTrue();

            var sessionId = Guid.NewGuid();
            var pipeName = $"antiphon-pty-linux-{sessionId:N}";
            var hostPid = await launcher.LaunchDetachedAsync(
                sessionId,
                Path.Combine(tempRoot, "manifests"),
                hostLogFile: Path.Combine(tempRoot, "host.log"),
                pipeName: pipeName,
                launchTimeout: TimeSpan.FromSeconds(60));
            owned.Add(hostPid);
            Process.GetProcessById(hostPid).HasExited.ShouldBeFalse();

            await using var client = await PipeTestClient.ConnectWithRetryAsync(pipeName, TimeSpan.FromSeconds(15));
            await client.SendAsync(new HelloMessage(PtyHostProtocol.Version));
            var hello = await client.ExpectAsync<HelloAckMessage>();
            hello.SessionId.ShouldBe(sessionId);
            hello.Status.ShouldBe(PtyHostStatus.WaitingForLaunch);

            await client.SendAsync(new LaunchMessage(
                "/bin/cat",
                [],
                new Dictionary<string, string>(),
                tempRoot,
                80,
                24,
                0,
                false,
                Path.Combine(tempRoot, "session.ansi.log")));
            var launched = await client.ExpectAsync<LaunchedMessage>();
            owned.Add(launched.ChildPid);
            launched.ChildPid.ShouldBeGreaterThan(0);

            await client.SendAsync(new AttachMessage(0));
            await client.SendAsync(new InputMessage("linux-pty-marker\n"));
            await client.CollectOutputUntilAsync(text => text.Contains("linux-pty-marker"));
            await client.SendAsync(new KillMessage(5000));
            var exited = await client.ExpectAsync<ExitedMessage>();
            exited.ExitReason.ShouldNotBeNullOrWhiteSpace();
            await client.SendAsync(new ShutdownMessage());
            await WaitForProcessExitAsync(hostPid, TimeSpan.FromSeconds(10));
            owned.Remove(hostPid);
        }
        finally
        {
            foreach (var pid in owned)
                TryKill(pid);
            TryDelete(tempRoot);
        }
    }

    [Test]
    public async Task Detach_creates_a_new_session()
    {
        RequireLinux();
        var tempRoot = NewRoot();
        var owned = new List<int>();
        try
        {
            var launcher = new PtyHostLauncher(new ShadowCopyStore(Path.Combine(tempRoot, "bin")), AppContext.BaseDirectory);
            var parentSession = Getsid(Environment.ProcessId);
            parentSession.ShouldNotBe(Environment.ProcessId, "the test process must not already be the host session leader");

            var sessionId = Guid.NewGuid();
            var hostPid = await launcher.LaunchDetachedAsync(
                sessionId,
                Path.Combine(tempRoot, "manifests"),
                pipeName: $"antiphon-pty-linux-sid-{sessionId:N}",
                launchTimeout: TimeSpan.FromSeconds(60));
            owned.Add(hostPid);

            var hostSessionId = Getsid(hostPid);
            hostSessionId.ShouldBe(hostPid);
            hostSessionId.ShouldNotBe(parentSession);
        }
        finally
        {
            foreach (var pid in owned)
                TryKill(pid);
            TryDelete(tempRoot);
        }
    }

    [Test]
    public async Task Detach_failure_exits_without_pipe_readiness()
    {
        RequireLinux();
        // Ordinary fault injection: invoking --detach outside a Unix spawn still fails closed
        // (setsid/open errors exit the child). A successful no-op setsid is the PC-29 mutation,
        // not this arm.
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, PtyHostLauncher.HostExeName),
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add("--detach");
        psi.ArgumentList.Add("--session");
        psi.ArgumentList.Add(Guid.Empty.ToString("D"));
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("failed to start host");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(cts.Token);
        process.ExitCode.ShouldNotBe(0);
        process.HasExited.ShouldBeTrue();
    }

    [Test]
    public async Task Intermediary_pipes_reach_eof_while_host_lives()
    {
        RequireLinux();
        var tempRoot = NewRoot();
        var owned = new List<int>();
        Process? intermediary = null;
        try
        {
            var launcher = new PtyHostLauncher(new ShadowCopyStore(Path.Combine(tempRoot, "bin")), AppContext.BaseDirectory);
            var exe = Path.Combine(launcher.CurrentShadowDir, PtyHostLauncher.HostExeName);
            var sessionId = Guid.NewGuid();
            var pipeName = $"antiphon-pty-linux-eof-{sessionId:N}";
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = launcher.CurrentShadowDir,
            };
            foreach (var arg in new[]
                     {
                         "--spawn", "--session", sessionId.ToString("D"), "--pipe", pipeName,
                         "--manifest-dir", Path.Combine(tempRoot, "manifests"),
                         "--launch-timeout-sec", "60",
                     })
                psi.ArgumentList.Add(arg);

            intermediary = Process.Start(psi) ?? throw new InvalidOperationException("intermediary failed to start");
            var stdoutTask = intermediary.StandardOutput.ReadToEndAsync();
            var stderrTask = intermediary.StandardError.ReadToEndAsync();
            var exitTask = intermediary.WaitForExitAsync();
            var completed = Task.WhenAll(stdoutTask, stderrTask, exitTask);
            var stdoutAndStderrCompletedWithinFiveSeconds = completed.Wait(TimeSpan.FromSeconds(5));
            stdoutAndStderrCompletedWithinFiveSeconds.ShouldBeTrue();

            var stdout = stdoutTask.Result;
            int.TryParse(stdout.Trim(), out var hostPid).ShouldBeTrue(stdout);
            owned.Add(hostPid);
            Process.GetProcessById(hostPid).HasExited.ShouldBeFalse();

            ReadLink($"/proc/{hostPid}/fd/0").ShouldBe("/dev/null");
            ReadLink($"/proc/{hostPid}/fd/1").ShouldBe("/dev/null");
            ReadLink($"/proc/{hostPid}/fd/2").ShouldBe("/dev/null");
        }
        finally
        {
            try { intermediary?.Kill(entireProcessTree: true); } catch { }
            intermediary?.Dispose();
            foreach (var pid in owned)
                TryKill(pid);
            TryDelete(tempRoot);
        }
    }

    [Test]
    public async Task Canceled_launch_leaves_no_owned_host()
    {
        RequireLinux();
        var tempRoot = NewRoot();
        var launcher = new PtyHostLauncher(new ShadowCopyStore(Path.Combine(tempRoot, "bin")), AppContext.BaseDirectory);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var ownedHostsAfterFiveSeconds = Array.Empty<int>();
        try
        {
            await Should.ThrowAsync<OperationCanceledException>(() =>
                launcher.LaunchDetachedAsync(
                    Guid.NewGuid(),
                    Path.Combine(tempRoot, "manifests"),
                    hostLogFile: Path.Combine(tempRoot, "host.log"),
                    launchTimeout: TimeSpan.FromSeconds(60),
                    ct: cts.Token));

            ownedHostsAfterFiveSeconds = await WaitUntilNoHostsUnderAsync(launcher.CurrentShadowDir, TimeSpan.FromSeconds(5));
            ownedHostsAfterFiveSeconds.ShouldBeEmpty();
        }
        finally
        {
            foreach (var pid in ownedHostsAfterFiveSeconds)
                TryKill(pid);
            foreach (var pid in HostPidsUnder(launcher.CurrentShadowDir))
                TryKill(pid);
            TryDelete(tempRoot);
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-linux-ptyhost", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    private static void TryKill(int pid)
    {
        if (pid <= 0) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private static async Task WaitForProcessExitAsync(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try { Process.GetProcessById(pid); }
            catch (ArgumentException) { return; }
            await Task.Delay(200);
        }

        throw new System.TimeoutException($"Process {pid} did not exit within {timeout}.");
    }

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
                if (path is not null && path.StartsWith(root, StringComparison.Ordinal))
                    found.Add(process.Id);
            }
            catch { }
            finally { process.Dispose(); }
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

    private static string ReadLink(string path)
    {
        var psi = new ProcessStartInfo("/bin/readlink", ["-f", path])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("readlink");
        var text = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(2000);
        return text;
    }

    [DllImport("libc", EntryPoint = "getsid", SetLastError = true)]
    private static extern int Getsid(int pid);
}
