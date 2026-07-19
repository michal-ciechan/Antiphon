using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.PtyHost.Protocol;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

/// <summary>
/// The load-bearing spike for the pty-host split: a host spawned via a short-lived intermediary
/// (broken parent chain, no console) must keep its ConPTY child alive and serviceable after the
/// spawner is gone — this is what lets sessions survive a runner restart.
/// </summary>
[Category("PtyHost")]
public class ParentDeathSpikeTests
{
    private static string HostExe => Path.Combine(AppContext.BaseDirectory, "Antiphon.PtyHost.exe");

    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
    }

    [Test]
    public async Task Detached_host_and_conpty_child_survive_spawner_death()
    {
        SkipIfNotWindows();
        File.Exists(HostExe).ShouldBeTrue($"host exe not found at {HostExe}");

        var sessionId = Guid.NewGuid();
        var tempDir = Path.Combine(Path.GetTempPath(), "antiphon-ptyhost-spike", sessionId.ToString("N"));
        Directory.CreateDirectory(tempDir);
        var pipeName = $"antiphon-pty-spike-{sessionId:N}";
        var manifestDir = Path.Combine(tempDir, "manifests");
        var manifestPath = PtyHostManifest.PathFor(manifestDir, sessionId);

        int hostPid = 0;
        int childPid = 0;
        try
        {
            // Intermediary: cmd's `start /b` launches the host and cmd exits immediately,
            // leaving the host with a dead parent — the same shape the runner will use.
            var spawner = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[]
            {
                "/d", "/c", "start", "\"\"", "/b", HostExe,
                "--session", sessionId.ToString(),
                "--pipe", pipeName,
                "--manifest-dir", manifestDir,
                "--log", Path.Combine(tempDir, "host.log"),
                "--launch-timeout-sec", "60",
            })
                spawner.ArgumentList.Add(arg);

            using var intermediary = Process.Start(spawner);
            intermediary.ShouldNotBeNull();
            intermediary.WaitForExit(10_000).ShouldBeTrue("intermediary cmd.exe should exit immediately");

            await using var client = await PipeTestClient.ConnectWithRetryAsync(pipeName, TimeSpan.FromSeconds(15));
            await client.SendAsync(new HelloMessage(PtyHostProtocol.Version));
            var helloAck = await client.ExpectAsync<HelloAckMessage>();
            helloAck.SessionId.ShouldBe(sessionId);

            var batPath = Path.Combine(tempDir, "spike.cmd");
            File.WriteAllText(batPath, "@echo off\r\necho spike-marker\r\nping -n 60 127.0.0.1 > nul\r\n");
            await client.SendAsync(new LaunchMessage(
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/c", batPath],
                new Dictionary<string, string>(),
                tempDir,
                120,
                30,
                MemoryLimitMb: 0,
                TranscriptEnabled: false,
                Path.Combine(tempDir, "session.ansi.log")));
            var launched = await client.ExpectAsync<LaunchedMessage>();
            childPid = launched.ChildPid;

            await client.SendAsync(new AttachMessage(0));
            await client.CollectOutputUntilAsync(text => text.Contains("spike-marker"));

            // The chain is broken and everything still works:
            var manifest = PtyHostManifest.TryLoad(manifestPath);
            manifest.ShouldNotBeNull();
            hostPid = manifest.HostPid;
            intermediary.HasExited.ShouldBeTrue();                     // spawner gone
            Process.GetProcessById(hostPid).HasExited.ShouldBeFalse(); // host alive
            Process.GetProcessById(childPid).HasExited.ShouldBeFalse();// ConPTY child alive

            // And the host remains controllable: kill the child, ack, and the host exits.
            await client.SendAsync(new KillMessage(5000));
            var exited = await client.ExpectAsync<ExitedMessage>();
            exited.ExitReason.ShouldBe("KilledByRequest");
            await client.SendAsync(new ShutdownMessage());

            await WaitForProcessExitAsync(hostPid, TimeSpan.FromSeconds(10));
            File.Exists(manifestPath).ShouldBeFalse();
        }
        finally
        {
            TryKill(childPid);
            TryKill(hostPid);
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup.
            }
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
