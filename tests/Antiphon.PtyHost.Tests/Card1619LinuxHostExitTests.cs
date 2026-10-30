using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Antiphon.TestSupport;

namespace Antiphon.PtyHost.Tests;

[Category("PtyHost")]
[Category("Integration")]
[NativeLinuxOnly("CARD-1619 requires real detached Linux PTY exit observation")]
[ParallelLimiter<ProcessSpawnLimit>]
public class Card1619LinuxHostExitTests
{
    [Test]
    public Task Live_SIGKILL_matches_manifest() => VerifyAsync(9, false);
    [Test]
    public Task Live_SIGTERM_matches_manifest() => VerifyAsync(15, false);
    [Test]
    public Task Exited_attach_and_status_replay_identical_evidence() => VerifyAsync(9, true);
    [Test]
    public Task Normal_exit_code_survives_detached_host() => VerifyAsync(0, true);

    private static async Task VerifyAsync(int signal, bool detached)
    {
        if (!OperatingSystem.IsLinux()) throw new SkipTestException("Real Linux PTY witness");
        var root = Path.Combine(Path.GetTempPath(), "card1619-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Process? host = null;
        Process? child = null;
        PtyHostClient? client = null;
        try
        {
            var id = Guid.NewGuid();
            var pipe = PtyHostProtocol.PipeNameFor(id);
            var dir = Path.Combine(root, "manifests");
            var log = Path.Combine(root, "host.log");
            var launcher = new PtyHostLauncher(new ShadowCopyStore(Path.Combine(root, "bin")), AppContext.BaseDirectory);
            var pid = await launcher.LaunchDetachedAsync(id, dir, hostLogFile: log, pipeName: pipe,
                launchTimeout: TimeSpan.FromSeconds(15));
            host = Process.GetProcessById(pid);
            client = await PtyHostClient.ConnectAsync(pipe, TimeSpan.FromSeconds(10), default);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var exit = new TaskCompletionSource<ExitedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.OnOutput += (_, chunk) => { if (chunk.Contains("card1619-ready")) ready.TrySetResult(); };
            client.OnExited += value => exit.TrySetResult(value);
            var generation = SessionGeneration.Normalize(DateTime.UtcNow);
            var script = signal == 0
                ? "printf 'card1619-ready\\n'; while [ ! -f release ]; do sleep 0.02; done; exit 23"
                : "printf 'card1619-ready\\n'; read line; exit 23";
            var launched = await client.LaunchAsync(new LaunchMessage("/bin/sh",
                ["-c", script], new Dictionary<string, string>(),
                root, 80, 24, 0, false, Path.Combine(root, "session.ansi.log"), AcceptedStartedAt: generation), default);
            child = Process.GetProcessById(launched.ChildPid);
            var childIdentity = StartTicks(child.Id);
            PtyHostManifest.TryLoad(PtyHostManifest.PathFor(dir, id))!.ChildStartTimeUtc
                .ShouldBe(launched.ChildStartTimeUtc);
            await client.AttachAsync(0, default);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (detached)
            {
                await client.DisposeAsync();
                client = null;
            }
            if (signal > 0)
            {
                child.HasExited.ShouldBeFalse();
                StartTicks(child.Id).ShouldBe(childIdentity, "owned PID/start ticks before external signal");
                Kill(child.Id, signal).ShouldBe(0);
            }
            else
            {
                // Release only after disconnect, without racing the old Unix listener's teardown.
                File.WriteAllText(Path.Combine(root, "release"), "release");
            }
            var path = PtyHostManifest.PathFor(dir, id);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            PtyHostManifest? manifest;
            do
            {
                manifest = PtyHostManifest.TryLoad(path);
                if (manifest?.ExitedAtUtc is not null) break;
                await Task.Delay(20);
            } while (DateTime.UtcNow < deadline);
            manifest.ShouldNotBeNull();
            manifest.ExitedAtUtc.ShouldNotBeNull();
            if (client is null)
            {
                client = await PtyHostClient.ConnectAsync(pipe, TimeSpan.FromSeconds(5), default);
                client.OnExited += value => exit.TrySetResult(value);
                await client.AttachAsync(0, default);
            }
            var live = await exit.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var status = await client.GetStatusAsync(default);
            live.ExitSignal.ShouldBe(signal);
            live.ExitCode.ShouldBe(signal > 0 ? 0 : 23);
            status.ExitSignal.ShouldBe(live.ExitSignal);
            status.ExitCode.ShouldBe(live.ExitCode);
            status.LastSeq.ShouldBe(live.LastSeq);
            manifest.ExitSignal.ShouldBe(live.ExitSignal);
            manifest.ExitCode.ShouldBe(live.ExitCode);
            manifest.AcceptedStartedAt.ShouldBe(generation);
            File.ReadAllText(log).ShouldContain($"signal {signal}");
            await client.ShutdownAsync(default);
            await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (client is not null) await client.DisposeAsync();
            foreach (var owned in new[] { child, host })
            {
                if (owned is null) continue;
                try { if (!owned.HasExited) owned.Kill(true); await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                finally { owned.Dispose(); }
            }
            Directory.Delete(root, true);
        }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);

    private static string StartTicks(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19];
    }
}
