using System.Diagnostics;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RunnerCustodyTests
{
    [Test]
    public async Task Real_host_receipt_is_accepted_before_read_and_survives_runtime_replacement()
    {
        await using var fixture = new CustodyFixture();
        var dto = await fixture.StartAsync();
        fixture.OwnHost(dto);
        var replay = await fixture.Runtime.StartAsync(fixture.Request, CancellationToken.None);
        replay.Pid.ShouldBe(dto.Pid);
        await fixture.Runtime.SendInputAsync(fixture.Binding.Generation.SessionId, "exit\r", CancellationToken.None);
        var final = await fixture.WaitFinalAsync();
        final.State.ShouldBe(VerificationCustodyState.Exited);
        var store = new RunnerCustodyLedger(fixture.CustodyRoot);
        store.ReadFinal(fixture.Binding)!.Receipt.ShouldBe(final.Receipt);
        await fixture.ReplaceRuntimeAsync();
        await fixture.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        var afterRestart = await fixture.Runtime.ReadCustodyAsync(fixture.Binding, false, CancellationToken.None);
        afterRestart.Receipt.ShouldBe(final.Receipt);
        await fixture.Host!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        File.Exists(PtyHostManifest.PathFor(fixture.Settings.PtyHostManifestDir, dto.SessionId)).ShouldBeFalse();
        store.ReadFinal(fixture.Binding)!.Receipt.ShouldBe(final.Receipt);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Live_host_adoption_keeps_original_binding_and_rejects_changed_replay(bool loseManifest)
    {
        await using var fixture = new CustodyFixture();
        var dto = await fixture.StartAsync();
        fixture.OwnHost(dto);
        await fixture.ReplaceRuntimeAsync();
        if (loseManifest) File.Delete(PtyHostManifest.PathFor(fixture.Settings.PtyHostManifestDir, dto.SessionId));
        (await fixture.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None)).ShouldBe(1);
        var replay = await fixture.Runtime.StartAsync(fixture.Request, CancellationToken.None);
        replay.Pid.ShouldBe(dto.Pid);
        var changed = fixture.Binding with { Source = fixture.Binding.Source with { SourceOperationId = Guid.NewGuid() } };
        (await Should.ThrowAsync<VerificationCustodyException>(() => fixture.Runtime.StartAsync(
            fixture.Request with { VerificationBinding = changed }, CancellationToken.None))).Code
            .ShouldBe("verification_custody_identity_mismatch");
        await fixture.Runtime.SendInputAsync(dto.SessionId, "echo adopted-custody\r", CancellationToken.None);
        await fixture.Runtime.KillAsync(dto.SessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
        (await fixture.WaitFinalAsync()).State.ShouldBe(VerificationCustodyState.Exited);
    }

    [Test]
    [Arguments(0u, false)]
    [Arguments(8u, false)]
    [Arguments(16u, false)]
    [Arguments(0u, true)]
    public async Task Dead_root_live_orphan_retains_host_and_refuses_generation_reuse(uint flags, bool authorizedKillAll)
    {
        await using var fixture = new CustodyFixture();
        var prefix = "Local\\c478-runner-" + Guid.NewGuid().ToString("N");
        var roles = new[] { "root", "middle", "leaf" };
        var ready = roles.Select(r => new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "-" + r)).ToArray();
        var release = roles.Select(r => new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "-release-" + r)).ToArray();
        try
        {
            fixture.Request = fixture.Request with
            {
                Exe = Path.Combine(AppContext.BaseDirectory, "custody-child", "Antiphon.CustodyTestChild.exe"),
                Args = ["root", prefix, flags.ToString()],
            };
            var dto = await fixture.StartAsync();
            fixture.OwnHost(dto);
            using var root = Process.GetProcessById(dto.Pid!.Value);
            foreach (var signal in ready) signal.WaitOne(TimeSpan.FromSeconds(15)).ShouldBeTrue();
            release[0].Set(); release[1].Set();
            await root.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var draining = await fixture.Runtime.ReadCustodyAsync(fixture.Binding, true, CancellationToken.None);
            draining.State.ShouldBe(VerificationCustodyState.Draining);
            draining.Receipt.ShouldBeNull();
            fixture.Host!.HasExited.ShouldBeFalse();
            var next = fixture.Binding with { ExecutionId = Guid.NewGuid(), Generation = fixture.Binding.Generation with
                { AcceptedStartedAt = fixture.Binding.Generation.AcceptedStartedAt.AddSeconds(1) } };
            (await Should.ThrowAsync<VerificationCustodyException>(() => fixture.Runtime.StartAsync(
                fixture.Request with { VerificationBinding = next }, CancellationToken.None))).Code
                .ShouldBe("verification_custody_session_fenced");
            await fixture.ReplaceRuntimeAsync();
            (await fixture.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None)).ShouldBe(1);
            (await fixture.Runtime.ReadCustodyAsync(fixture.Binding, false, CancellationToken.None)).State
                .ShouldBe(VerificationCustodyState.Draining);
            if (authorizedKillAll)
                (await fixture.Runtime.KillAllAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).Count.ShouldBe(1);
            else
                release[2].Set();
            (await fixture.WaitFinalAsync()).State.ShouldBe(VerificationCustodyState.Exited);
        }
        finally
        {
            foreach (var signal in release) signal.Set();
            foreach (var signal in ready.Concat(release)) signal.Dispose();
        }
    }

    [Test]
    public async Task Actual_host_crash_before_proof_is_unknown_after_restart()
    {
        await using var fixture = new CustodyFixture();
        var dto = await fixture.StartAsync();
        fixture.OwnHost(dto);
        // Exact retained process handle, owned by this fixture. This is a real observer crash,
        // not a seeded missing PID or a graceful HostHarness.Dispose.
        fixture.Host!.Kill();
        await fixture.Host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await fixture.ReplaceRuntimeAsync();
        await fixture.Runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
        var unknown = await fixture.Runtime.ReadCustodyAsync(fixture.Binding, false, CancellationToken.None);
        unknown.State.ShouldBe(VerificationCustodyState.Unknown);
        unknown.Receipt.ShouldBeNull();
        var replay = await fixture.Runtime.StartAsync(fixture.Request, CancellationToken.None);
        replay.HostPid.ShouldBe(dto.HostPid);
        replay.VerificationBinding.ShouldBe(fixture.Binding);
        fixture.Host.HasExited.ShouldBeTrue("replay must not replace the lost original host");
        File.Exists(new RunnerCustodyLedger(fixture.CustodyRoot).Store.PathFor(fixture.Binding.ExecutionId,
            "native-start-intent.json")).ShouldBeTrue();
    }

    [Test]
    public async Task Durable_seal_beats_delayed_launch_and_fences_legacy_and_herdr_requests()
    {
        await using var fixture = new CustodyFixture();
        var status = await fixture.Runtime.ReadCustodyAsync(fixture.Binding, true, CancellationToken.None);
        status.State.ShouldBe(VerificationCustodyState.Unknown);
        await fixture.ReplaceRuntimeAsync();
        (await Should.ThrowAsync<VerificationCustodyException>(() => fixture.StartAsync())).Code
            .ShouldBe("verification_custody_sealed");
        (await Should.ThrowAsync<VerificationCustodyException>(() => fixture.Runtime.StartAsync(
            fixture.Request with { VerificationBinding = null }, CancellationToken.None))).Code
            .ShouldBe("verification_custody_binding_required");
        (await Should.ThrowAsync<VerificationCustodyException>(() => fixture.Runtime.AttachHerdrAsync(
            new(fixture.Binding.Generation.SessionId, "not-a-pane", "claude", "claude", 123, "test"), CancellationToken.None))).Code
            .ShouldBe("verification_custody_binding_required");
        fixture.Runtime.List().ShouldBeEmpty();
        File.Exists(new RunnerCustodyLedger(fixture.CustodyRoot).Store.PathFor(fixture.Binding.ExecutionId,
            "native-start-intent.json")).ShouldBeFalse();
    }

    internal sealed class CustodyFixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "antiphon-runner-custody", Guid.NewGuid().ToString("N"));
        public SessionRunnerSettings Settings { get; }
        public VerificationExecutionBinding Binding { get; }
        public RunnerLaunchRequest Request { get; set; }
        public SessionRunnerRuntime Runtime { get; private set; }
        public Process? Host { get; private set; }
        public string CustodyRoot => Path.Combine(Settings.SessionLogPath, "verification-custody");

        public CustodyFixture()
        {
            if (!OperatingSystem.IsWindows() || PtyBackendPolicy.Resolve("modern").Backend != PtyBackend.ModernConPty)
                throw new SkipTestException("Requires Windows and the shipped modern ConPTY backend");
            Settings = new() { SessionLogPath = Path.Combine(_root, "runtime"), PtyBackend = "modern", PtyHostLingerHours = 0.02 };
            Runtime = NewRuntime();
            var now = DateTime.UtcNow;
            Binding = new(Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
                new(Guid.NewGuid(), new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)),
                new(_root, Path.Combine(_root, ".git"), Path.Combine(_root, "snapshot"),
                    Path.Combine(_root, ".git", "worktrees", "snapshot"), "feat/test", Guid.NewGuid()), RunnerStoreId: Runtime.RunnerStoreId);
            Directory.CreateDirectory(Binding.Creation.WorktreePath);
            Request = new(Binding.Generation.SessionId, Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                [], new Dictionary<string, string>(), Binding.Creation.WorktreePath, 80, 24, VerificationBinding: Binding);
        }
        private SessionRunnerRuntime NewRuntime() => new(Options.Create(Settings), NullLogger<SessionRunnerRuntime>.Instance);
        public Task<RunnerSessionDto> StartAsync() => Runtime.StartAsync(Request, CancellationToken.None);
        public void OwnHost(RunnerSessionDto dto) => Host = Process.GetProcessById(dto.HostPid!.Value);
        public async Task ReplaceRuntimeAsync()
        {
            await Runtime.DisposeAsync();
            Runtime = NewRuntime();
        }
        public async Task<VerificationCustodyStatus> WaitFinalAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (true)
            {
                var result = await Runtime.ReadCustodyAsync(Binding, true, timeout.Token);
                if (result.Receipt is not null) return result;
                await Task.Delay(50, timeout.Token);
            }
        }
        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            if (Host is not null)
            {
                if (!Host.HasExited) Host.Kill();
                await Host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Host.Dispose();
            }
            // Exact fixture-owned path. Child shutdown can briefly hold console files.
            for (var attempt = 0; attempt < 20 && Directory.Exists(_root); attempt++)
            {
                try { Directory.Delete(_root, recursive: true); }
                catch (IOException) { await Task.Delay(100); }
                catch (UnauthorizedAccessException) { await Task.Delay(100); }
            }
        }
    }
}
