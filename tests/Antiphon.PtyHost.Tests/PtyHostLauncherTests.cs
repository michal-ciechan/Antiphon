using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// End-to-end detachment: launcher shadow-copies the host, spawns it through the --spawn
/// intermediary (broken parent chain, job breakaway), and the resulting host runs from the
/// shadow dir - never from build output - and is fully serviceable over the pipe.
/// </summary>
[Category("PtyHost")]
public class PtyHostLauncherTests
{
    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
    }

    [Test]
    public async Task Launcher_spawns_detached_host_from_shadow_copy_and_it_is_serviceable()
    {
        SkipIfNotWindows();

        var tempRoot = Path.Combine(Path.GetTempPath(), "antiphon-launcher-tests", Guid.NewGuid().ToString("N"));
        var manifestDir = Path.Combine(tempRoot, "manifests");
        Directory.CreateDirectory(tempRoot);

        var store = new ShadowCopyStore(Path.Combine(tempRoot, "bin"));
        var launcher = new PtyHostLauncher(store, AppContext.BaseDirectory);
        var sessionId = Guid.NewGuid();
        var pipeName = $"antiphon-pty-launcher-{sessionId:N}";

        int hostPid = 0;
        int childPid = 0;
        try
        {
            hostPid = await launcher.LaunchDetachedAsync(
                sessionId,
                manifestDir,
                hostLogFile: Path.Combine(tempRoot, "host.log"),
                pipeName: pipeName,
                launchTimeout: TimeSpan.FromSeconds(60));

            // Host is alive, and runs from the shadow copy - not from build output.
            var host = Process.GetProcessById(hostPid);
            host.HasExited.ShouldBeFalse();
            host.MainModule!.FileName.ShouldStartWith(launcher.CurrentShadowDir);

            // Fully serviceable: pipe + ConPTY child both work from the shadow copy.
            await using var client = await PipeTestClient.ConnectWithRetryAsync(pipeName, TimeSpan.FromSeconds(15));
            await client.SendAsync(new HelloMessage(PtyHostProtocol.Version));
            var helloAck = await client.ExpectAsync<HelloAckMessage>();
            helloAck.SessionId.ShouldBe(sessionId);
            helloAck.Status.ShouldBe(PtyHostStatus.WaitingForLaunch);

            var batPath = Path.Combine(tempRoot, "hold.cmd");
            File.WriteAllText(batPath, "@echo off\r\necho launcher-marker\r\nping -n 60 127.0.0.1 > nul\r\n");
            await client.SendAsync(new LaunchMessage(
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/c", batPath],
                new Dictionary<string, string>(),
                tempRoot,
                120,
                30,
                MemoryLimitMb: 0,
                TranscriptEnabled: false,
                Path.Combine(tempRoot, "session.ansi.log")));
            var launched = await client.ExpectAsync<LaunchedMessage>();
            childPid = launched.ChildPid;

            await client.SendAsync(new AttachMessage(0));
            await client.CollectOutputUntilAsync(text => text.Contains("launcher-marker"));

            await client.SendAsync(new KillMessage(5000));
            await client.ExpectAsync<ExitedMessage>();
            await client.SendAsync(new ShutdownMessage());
            await WaitForProcessExitAsync(hostPid, TimeSpan.FromSeconds(10));
        }
        finally
        {
            TryKill(childPid);
            TryKill(hostPid);
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Test]
    public async Task Launcher_reuses_the_shadow_copy_across_launches()
    {
        SkipIfNotWindows();

        var tempRoot = Path.Combine(Path.GetTempPath(), "antiphon-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var store = new ShadowCopyStore(Path.Combine(tempRoot, "bin"));
        var launcher = new PtyHostLauncher(store, AppContext.BaseDirectory);

        var pids = new List<int>();
        try
        {
            foreach (var _ in Enumerable.Range(0, 2))
            {
                pids.Add(await launcher.LaunchDetachedAsync(
                    Guid.NewGuid(),
                    Path.Combine(tempRoot, "manifests"),
                    launchTimeout: TimeSpan.FromSeconds(5)));
            }

            Directory.GetDirectories(store.BinRoot).Length.ShouldBe(1);

            // Both hosts self-destruct via launch timeout - no manifests were ever written.
            foreach (var pid in pids)
                await WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(20));
            Directory.Exists(Path.Combine(tempRoot, "manifests")).ShouldBeFalse();
        }
        finally
        {
            foreach (var pid in pids)
                TryKill(pid);
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static async Task WaitForProcessExitAsync(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return;
            }
            await Task.Delay(200);
        }

        throw new System.TimeoutException($"Process {pid} did not exit within {timeout}.");
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
}
