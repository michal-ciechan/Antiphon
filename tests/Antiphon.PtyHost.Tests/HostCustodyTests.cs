using Antiphon.Agents.Pty;
using Antiphon.PtyHost;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using System.Diagnostics;
using System.Text.Json;
using System.IO.Pipes;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.PtyHost.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class HostCustodyTests
{
    [Test]
    public async Task C478_G213_VersionNegotiation() =>
        await Legacy_peer_receives_no_custody_or_tracked_launch_messages(missingFeature: true);

    [Test]
    public async Task C478_V15_ReceiptBeforeShutdown() =>
        await Durable_receipt_precedes_reply_and_shutdown_requires_runner_acceptance();

    [Test]
    public async Task C478_G201_DrainBeforeReceipt() =>
        await Output_write_failure_cannot_certify_drain();

    [Test]
    public async Task C478_G204_ReceiptBeforeAck() =>
        await Durable_receipt_precedes_reply_and_shutdown_requires_runner_acceptance();

    [Test]
    public async Task C478_G211_TerminalConflict() =>
        await Producer_store_failure_never_acknowledges_exited_and_can_retry_same_job();

    [Test]
    public async Task C478_G214_NeverStarted() =>
        await A_native_start_failure_retains_unknown_and_cannot_relaunch();

    [Test]
    public void C478_V14_HostIntermediaryBreakawayDeniedFallback()
    {
        // kernel32 CreateProcessW job breakaway. Windows stays admitted; Linux has no job object.
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("Requires Windows job-object breakaway");
        var pid = Win32ProcessSpawner.StartDetachedWithFallback(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "ping -n 2 127.0.0.1 > nul"]);
        pid.ShouldBeGreaterThan(0);
        try
        {
            using var child = Process.GetProcessById(pid);
            child.WaitForExit(15000).ShouldBeTrue();
        }
        catch (ArgumentException)
        {
            // The intermediary already exited after a successful detached spawn.
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Legacy_peer_receives_no_custody_or_tracked_launch_messages(bool missingFeature)
    {
        await using var fixture = CreateHost();
        var (store, binding) = Reserve(fixture);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pipeName = fixture.Options.PipeName + "-legacy";
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var peer = Task.Run(async () =>
        {
            await pipe.WaitForConnectionAsync(timeout.Token);
            (await PtyHostFraming.ReadAsync(pipe, timeout.Token)).ShouldBeOfType<HelloMessage>();
            await PtyHostFraming.WriteAsync(pipe, new HelloAckMessage(1, "old-host", binding.Generation.SessionId,
                PtyHostStatus.WaitingForLaunch, missingFeature ? null : ["verificationCustodyV1"],
                missingFeature ? Guid.NewGuid() : null), timeout.Token);
            var ordinary = (await PtyHostFraming.ReadAsync(pipe, timeout.Token)).ShouldBeOfType<LaunchMessage>();
            ordinary.VerificationBinding.ShouldBeNull("neither rejected request may reach the legacy peer");
            await PtyHostFraming.WriteAsync(pipe, new LaunchedMessage(1, DateTime.UtcNow), timeout.Token);
        }, timeout.Token);
        try
        {
            await using var client = await PtyHostClient.ConnectAsync(pipeName, TimeSpan.FromSeconds(10), timeout.Token);
            (await Should.ThrowAsync<VerificationCustodyException>(() => client.GetCustodyAsync(binding, timeout.Token))).Code
                .ShouldBe("verification_custody_unsupported_backend");
            await Should.ThrowAsync<VerificationCustodyException>(() => client.LaunchAsync(
                Launch(fixture, store, binding, "exit /b 0"), timeout.Token));
            (await client.LaunchAsync(fixture.CmdLaunch("exit /b 0"), timeout.Token)).ChildPid.ShouldBe(1);
            await peer;
        }
        finally
        {
            timeout.Cancel();
            try { await peer; } catch (OperationCanceledException) { }
        }
    }

    [Test]
    public async Task Tracking_store_failure_prevents_the_first_provider_payload()
    {
        RequireModern();
        var files = new FailingHostFiles("tracking.json");
        await using var host = HostHarness.Start(o => o with
        { PtyBackend = "modern", CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
          // CARD-0604 D-17: production always states the backend on the host command line;
          // a host that was not told which mechanism to perform advertises none and
          // refuses every binding.
          CustodyBackend = VerificationCustodyBackends.WindowsJob }, files);
        var (store, binding) = Reserve(host);
        try
        {
            var launch = Launch(host, store, binding, "echo forbidden > payload-marker");
            (await host.Session.LaunchAsync(launch, CancellationToken.None)).ShouldBeOfType<ErrorMessage>();
            files.SuspendedRoot.ShouldNotBeNull();
            await files.SuspendedRoot.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            File.Exists(Path.Combine(binding.Creation.WorktreePath, "payload-marker")).ShouldBeFalse();
            (await host.Session.GetCustodyAsync(binding, CancellationToken.None)).State.ShouldBe(VerificationCustodyState.Unknown);
            File.Exists(store.PathFor(binding.ExecutionId, "tracking.json")).ShouldBeFalse();
        }
        finally { files.SuspendedRoot?.Dispose(); }
    }

    [Test]
    public async Task Producer_store_failure_never_acknowledges_exited_and_can_retry_same_job()
    {
        RequireModern();
        var files = new FailingHostFiles("producer-receipt.json");
        await using var host = HostHarness.Start(o => o with
        { PtyBackend = "modern", CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
          // CARD-0604 D-17: production always states the backend on the host command line;
          // a host that was not told which mechanism to perform advertises none and
          // refuses every binding.
          CustodyBackend = VerificationCustodyBackends.WindowsJob }, files);
        var (store, binding) = Reserve(host);
        await using var client = await PtyHostClient.ConnectAsync(host.Options.PipeName, TimeSpan.FromSeconds(10), CancellationToken.None);
        await client.LaunchAsync(Launch(host, store, binding, "echo durable-output", "exit /b 0"), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        VerificationCustodyStatus status;
        do
        {
            status = await client.GetCustodyAsync(binding, true, timeout.Token);
            status.Receipt.ShouldBeNull();
            status.State.ShouldNotBe(VerificationCustodyState.Exited);
            if (files.Failures == 0) await Task.Delay(50, timeout.Token);
        } while (files.Failures == 0);
        File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
        files.Enabled = false;
        var final = await WaitForReceipt(client, binding);
        final.Host.ShouldBe(status.Host);
        final.State.ShouldBe(VerificationCustodyState.Exited);
        File.ReadAllText(host.AnsiLogPath).ShouldContain("durable-output");
    }

    [Test]
    public async Task Durable_receipt_precedes_reply_and_shutdown_requires_runner_acceptance()
    {
        RequireModern();
        await using var host = CreateHost();
        var (store, binding) = Reserve(host);
        await using var client = await PtyHostClient.ConnectAsync(host.Options.PipeName,
            TimeSpan.FromSeconds(10), CancellationToken.None);
        client.Hello.Features!.ShouldContain("verificationCustodyV1");
        await client.LaunchAsync(Launch(host, store, binding, "echo custody-output-complete", "exit /b 0"), CancellationToken.None);
        var final = await WaitForReceipt(client, binding);
        var identity = final.Host!;
        final.State.ShouldBe(VerificationCustodyState.Exited);
        var independent = new VerificationCustodyStore(store.Root, store.StoreId);
        independent.ReadReceipt(binding, identity, accepted: false).ShouldBe(final.Receipt);
        File.ReadAllText(host.AnsiLogPath).ShouldContain("custody-output-complete");
        var receipt = independent.ValidateReceipt(final.Receipt!, binding, identity);
        receipt.ActiveProcesses.ShouldBe(0u);
        receipt.OutputDrained.ShouldBeTrue();
        receipt.RootPid!.Value.ShouldBeGreaterThan(0);
        Should.Throw<VerificationCustodyException>(host.Session.Shutdown).Code
            .ShouldBe("verification_custody_receipt_not_accepted");
        host.Session.ExitRequested.IsCompleted.ShouldBeFalse();
        independent.AcceptReceipt(final.Receipt!, binding, identity);
        host.Session.Shutdown();
        (await host.Session.ExitRequested).ShouldBe("shutdown ack from runner");
        independent.ReadReceipt(binding, identity, accepted: true).ShouldBe(final.Receipt);
        File.Exists(host.ManifestPath).ShouldBeFalse();
    }

    [Test]
    public async Task Live_job_seals_without_killing_and_late_input_and_launch_are_refused()
    {
        RequireModern();
        await using var host = CreateHost();
        var (store, binding) = Reserve(host);
        var launch = host.InteractiveCmdLaunch() with
        { Cwd = binding.Creation.WorktreePath, VerificationBinding = binding, RunnerStoreId = store.StoreId };
        (await host.Session.LaunchAsync(launch, CancellationToken.None)).ShouldBeOfType<LaunchedMessage>();
        var beforeSeal = await host.Session.GetCustodyAsync(binding, CancellationToken.None);
        beforeSeal.State.ShouldBe(VerificationCustodyState.Tracking);
        File.Exists(store.PathFor(binding.ExecutionId, "producer-seal.json")).ShouldBeFalse();
        var status = await host.Session.GetCustodyAsync(binding, true, CancellationToken.None);
        status.State.ShouldBe(VerificationCustodyState.Draining);
        status.Receipt.ShouldBeNull();
        host.Session.Status.ShouldBe(PtyHostStatus.Running);
        File.Exists(store.PathFor(binding.ExecutionId, "producer-seal.json")).ShouldBeTrue();
        await Should.ThrowAsync<InvalidOperationException>(() => host.Session.WriteInputAsync("late", CancellationToken.None));
        (await host.Session.LaunchAsync(launch, CancellationToken.None)).ShouldBeOfType<ErrorMessage>();
        await host.Session.KillAsync(TimeSpan.FromSeconds(10));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while ((status = await host.Session.GetCustodyAsync(binding, timeout.Token)).Receipt is null)
            await Task.Delay(50, timeout.Token);
        status.State.ShouldBe(VerificationCustodyState.Exited);
    }

    /// <summary>
    /// CARD-1614. Unchanged Windows release: a tracked root exits while a descendant stays in the
    /// kill-on-close job, no receipt is accepted, and the short linger exits the host. The retained
    /// descendant handle is then dead because the job closed with the host. Linux skips.
    /// </summary>
    [Test]
    public async Task C1614_Windows_tracked_linger_releases_job()
    {
        RequireModern();
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "antiphon-c1614-win-linger", Guid.NewGuid().ToString("N")));
        var worktree = Path.Combine(root, "snapshot");
        var custodyRoot = Path.Combine(root, "custody");
        Directory.CreateDirectory(worktree);
        var sessionId = Guid.NewGuid();
        var pipeName = $"antiphon-c1614-linger-{sessionId:N}";
        var (store, binding) = ReserveWindowsLingerExecution(root, sessionId);
        var launcher = new PtyHostLauncher(new ShadowCopyStore(Path.Combine(root, "bin")), AppContext.BaseDirectory);
        var hostPid = 0;
        Process? host = null;
        Process? held = null;
        try
        {
            hostPid = await launcher.LaunchDetachedAsync(
                sessionId, Path.Combine(root, "manifests"), Path.Combine(root, "host.log"), pipeName,
                launchTimeout: TimeSpan.FromSeconds(20), lingerTtl: TimeSpan.FromSeconds(2),
                ptyBackend: "modern", custodyStoreRoot: custodyRoot,
                custodyBackend: VerificationCustodyBackends.WindowsJob);
            host = Process.GetProcessById(hostPid);
            // Retain the native handle before an early launch rejection exits the process, so
            // EOF diagnostics can still read its exit code after Windows removes its PID.
            _ = host.Handle;
            await using var client = await PipeTestClient.ConnectWithRetryAsync(pipeName, TimeSpan.FromSeconds(15),
                () => PipeTestClient.DescribeHost(host, Path.Combine(root, "host.log")));
            await client.SendAsync(new HelloMessage(PtyHostProtocol.Version));
            (await client.ExpectAsync<HelloAckMessage>()).Features.ShouldNotBeNull()
                .ShouldContain("verificationCustodyV1");
            var script = Path.Combine(root, "hold.ps1");
            var childIdentityPath = Path.Combine(root, "child.identity");
            var releaseRootPath = Path.Combine(root, "release-root");
            File.WriteAllText(script, """
                param([string]$IdentityPath, [string]$ReleasePath)
                $ErrorActionPreference = 'Stop'
                $child = Start-Process -FilePath "$env:SystemRoot\System32\PING.EXE" -ArgumentList '-n','40','127.0.0.1' -NoNewWindow -PassThru
                [IO.File]::WriteAllText(($IdentityPath + '.tmp'), ($child.Id.ToString() + '|' + $child.StartTime.ToUniversalTime().Ticks.ToString()))
                [IO.File]::Move(($IdentityPath + '.tmp'), $IdentityPath)
                while (-not [IO.File]::Exists($ReleasePath)) { Start-Sleep -Milliseconds 20 }
                """);
            await client.SendAsync(new LaunchMessage(
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                    childIdentityPath, releaseRootPath], new Dictionary<string, string>(), worktree,
                120, 30, 0, false, Path.Combine(root, "session.ansi.log"),
                VerificationBinding: binding, RunnerStoreId: store.StoreId));
            var launched = await client.ExpectAsync<LaunchedMessage>(TimeSpan.FromSeconds(20));
            launched.ChildPid.ShouldBeGreaterThan(0);

            var findBy = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            while (!File.Exists(childIdentityPath) && DateTime.UtcNow < findBy && host is { HasExited: false })
                await Task.Delay(50);
            File.Exists(childIdentityPath).ShouldBeTrue("the tracked root must identify its own descendant");
            var identity = File.ReadAllText(childIdentityPath).Split('|');
            var descendant = Process.GetProcessById(int.Parse(identity[0]));
            if (descendant.StartTime.ToUniversalTime().Ticks != long.Parse(identity[1]))
            {
                descendant.Dispose();
                throw new InvalidOperationException("The descendant PID was reused; no kill is authorized.");
            }
            held = descendant;
            host.HasExited.ShouldBeFalse("the descendant must still be observed while the host is alive");
            // Hold the root until the client owns the exact descendant handle, so the two-second
            // linger measures job release rather than racing process discovery on a loaded host.
            File.WriteAllText(releaseRootPath, "release");
            var rootGone = false;
            try
            {
                using var rootProc = Process.GetProcessById(launched.ChildPid);
                rootGone = rootProc.HasExited || rootProc.WaitForExit(5000);
            }
            catch (ArgumentException)
            {
                rootGone = true;
            }
            rootGone.ShouldBeTrue("the tracked root exits while its descendant remains");
            held.HasExited.ShouldBeFalse("kill-on-close has not run before the host leaves linger");
            File.Exists(store.PathFor(binding.ExecutionId, "accepted-receipt.json")).ShouldBeFalse();
            File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
            host.WaitForExit(8000).ShouldBeTrue("the short tracked linger exits the host");
            var reapBy = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (!held.HasExited && DateTime.UtcNow < reapBy)
                await Task.Delay(50);
            held.HasExited.ShouldBeTrue("closing the host closes the kill-on-close job and releases the descendant");
            held.Refresh();
            File.Exists(store.PathFor(binding.ExecutionId, "accepted-receipt.json")).ShouldBeFalse();
        }
        finally
        {
            try
            {
                if (held is { HasExited: false })
                    held.Kill(entireProcessTree: true);
            }
            catch { }
            held?.Dispose();
            try
            {
                if (host is { HasExited: false })
                    host.Kill(entireProcessTree: true);
            }
            catch
            {
                if (hostPid > 0)
                {
                    try { Process.GetProcessById(hostPid).Kill(entireProcessTree: true); } catch { }
                }
            }
            host?.Dispose();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Test]
    public void C1614_Linger_fixture_is_admitted_before_native_start()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c1614-admission", Guid.NewGuid().ToString("N"));
        try
        {
            var (store, binding) = ReserveWindowsLingerExecution(root, Guid.NewGuid());
            var journal = new HostCustodyJournal(store, binding, Guid.NewGuid());
            journal.Binding.ShouldBe(binding);
            File.Exists(store.PathFor(binding.ExecutionId, "native-start-intent.json")).ShouldBeFalse();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static (VerificationCustodyStore Store, VerificationExecutionBinding Binding)
        ReserveWindowsLingerExecution(string root, Guid sessionId)
    {
        var worktree = Path.Combine(root, "snapshot");
        var custodyRoot = Path.Combine(root, "custody");
        Directory.CreateDirectory(worktree);
        var store = new VerificationCustodyStore(custodyRoot);
        var now = DateTime.UtcNow;
        var binding = new VerificationExecutionBinding(Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(sessionId, new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
            new(root, Path.Combine(root, ".git"), worktree,
                Path.Combine(root, ".git", "worktrees", "snapshot"), "feat/custody-test", Guid.NewGuid()),
            VerificationCustodyBackends.WindowsJob, store.StoreId);
        store.Reserve(binding);
        // Reserve alone is not launch authority: mirror the runner's durable pre-launch boundary.
        store.WriteRecord(binding, "runner-start-intent.json", new CustodyStamp(1, DateTime.UtcNow));
        return (store, binding);
    }

    [Test]
    public async Task Inbox_is_unsupported_and_never_becomes_no_start_proof()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("Requires Windows job-object inbox custody.");
        await using var host = CreateHost("inbox");
        var (store, binding) = Reserve(host);
        var marker = Path.Combine(binding.Creation.WorktreePath, "payload-ran");
        (await host.Session.LaunchAsync(Launch(host, store, binding, "echo ran > payload-ran"), CancellationToken.None))
            .ShouldBeOfType<ErrorMessage>().Code.ShouldBe("verification_custody_unsupported_backend");
        var status = await host.Session.GetCustodyAsync(binding, CancellationToken.None);
        status.State.ShouldBe(VerificationCustodyState.UnsupportedBackend);
        status.Receipt.ShouldBeNull();
        File.Exists(marker).ShouldBeFalse();
        File.Exists(store.PathFor(binding.ExecutionId, "native-start-intent.json")).ShouldBeFalse();
        host.Session.GetHelloAck("test").Features!.ShouldNotContain("verificationCustodyV1");
    }

    [Test]
    public async Task A_native_start_failure_retains_unknown_and_cannot_relaunch()
    {
        RequireModern();
        await using var host = CreateHost();
        var (store, binding) = Reserve(host);
        var launch = Launch(host, store, binding, "exit /b 0") with
        { Exe = Path.Combine(host.TempDir, "does-not-exist.exe") };
        (await host.Session.LaunchAsync(launch, CancellationToken.None)).ShouldBeOfType<ErrorMessage>();
        File.Exists(store.PathFor(binding.ExecutionId, "native-start-intent.json")).ShouldBeTrue();
        (await host.Session.GetCustodyAsync(binding, CancellationToken.None)).State.ShouldBe(VerificationCustodyState.Unknown);
        (await host.Session.LaunchAsync(Launch(host, store, binding, "exit /b 0"), CancellationToken.None))
            .ShouldBeOfType<ErrorMessage>();
        File.Exists(store.PathFor(binding.ExecutionId, "producer-receipt.json")).ShouldBeFalse();
    }

    [Test]
    public async Task Output_write_failure_cannot_certify_drain()
    {
        RequireModern();
        await using var host = CreateHost();
        var (store, binding) = Reserve(host);
        var blocked = Path.Combine(host.TempDir, "blocked-ansi");
        Directory.CreateDirectory(blocked);
        var launch = Launch(host, store, binding, "echo output-must-be-recorded", "exit /b 0") with { AnsiLogPath = blocked };
        (await host.Session.LaunchAsync(launch, CancellationToken.None)).ShouldBeOfType<LaunchedMessage>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        VerificationCustodyStatus status;
        do
        {
            status = await host.Session.GetCustodyAsync(binding, true, timeout.Token);
            if (status.State == VerificationCustodyState.Draining) await Task.Delay(50, timeout.Token);
        } while (status.State == VerificationCustodyState.Draining);
        status.State.ShouldBe(VerificationCustodyState.Unknown);
        status.Reason.ShouldBe("host_output_drain_failed");
        status.Receipt.ShouldBeNull();
    }

    [Test]
    public async Task Snapshot_local_host_files_refuse_before_native_launch()
    {
        RequireModern();
        await using var host = CreateHost();
        var (store, binding) = Reserve(host);
        var launch = Launch(host, store, binding, "echo should-not-run") with
        { AnsiLogPath = Path.Combine(binding.Creation.WorktreePath, "local.log") };
        (await host.Session.LaunchAsync(launch, CancellationToken.None)).ShouldBeOfType<ErrorMessage>();
        File.Exists(store.PathFor(binding.ExecutionId, "native-start-intent.json")).ShouldBeFalse();
        File.Exists(launch.AnsiLogPath).ShouldBeFalse();
    }

    private static HostHarness CreateHost(string backend = "modern") => HostHarness.Start(o => o with
    { PtyBackend = backend, CustodyStoreRoot = Path.Combine(Path.GetDirectoryName(o.ManifestDir)!, "custody"),
      CustodyBackend = VerificationCustodyBackends.WindowsJob });

    internal static (VerificationCustodyStore Store, VerificationExecutionBinding Binding) Reserve(HostHarness host)
    {
        var store = new VerificationCustodyStore(host.Options.CustodyStoreRoot!);
        var snapshot = Path.Combine(host.TempDir, "snapshot");
        Directory.CreateDirectory(snapshot);
        var now = DateTime.UtcNow;
        var binding = new VerificationExecutionBinding(Guid.NewGuid(),
            new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(host.SessionId, new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
            new(host.TempDir, Path.Combine(host.TempDir, ".git"), snapshot,
                Path.Combine(host.TempDir, ".git", "worktrees", "snapshot"), "feat/custody-test", Guid.NewGuid()),
            VerificationCustodyBackends.WindowsJob, RunnerStoreId: store.StoreId);
        store.Reserve(binding);
        store.WriteRecord(binding, "runner-start-intent.json", new CustodyStamp(1, DateTime.UtcNow));
        return (store, binding);
    }

    private static LaunchMessage Launch(HostHarness host, VerificationCustodyStore store,
        VerificationExecutionBinding binding, params string[] lines) => host.CmdLaunch(lines) with
        { Cwd = binding.Creation.WorktreePath, VerificationBinding = binding, RunnerStoreId = store.StoreId };

    private static async Task<VerificationCustodyStatus> WaitForReceipt(PtyHostClient client, VerificationExecutionBinding binding)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            var result = await client.GetCustodyAsync(binding, timeout.Token);
            if (result.Receipt is not null) return result;
            await Task.Delay(50, timeout.Token);
        }
    }

    private static void RequireModern()
    {
        if (!OperatingSystem.IsWindows() || PtyBackendPolicy.Resolve("modern").Backend != PtyBackend.ModernConPty)
            throw new SkipTestException("Requires Windows and the shipped modern ConPTY backend");
    }

    private sealed class FailingHostFiles(string fileName) : IVerificationCustodyFiles
    {
        private readonly VerificationCustodyFiles _files = new();
        public bool Enabled { get; set; } = true;
        public int Failures { get; private set; }
        public Process? SuspendedRoot { get; private set; }
        public byte[]? Read(string path) => _files.Read(path);
        public void CommitImmutable(string path, byte[] bytes)
        {
            if (Enabled && Path.GetFileName(path) == fileName)
            {
                if (fileName == "tracking.json")
                {
                    var root = JsonSerializer.Deserialize<CustodyRoot>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                    SuspendedRoot = Process.GetProcessById(root.Pid);
                }
                Failures++;
                throw new IOException("Injected host ledger commit failure");
            }
            _files.CommitImmutable(path, bytes);
        }
    }
}
