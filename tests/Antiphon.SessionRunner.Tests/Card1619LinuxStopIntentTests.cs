using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[NativeLinuxOnly("CARD-1619 Linux runner stop routes")]
[ParallelLimiter<ProcessSpawnLimit>]
public class Card1619LinuxStopIntentTests
{
    [Test]
    public async Task Own_stop_preserves_SIGKILL_and_intent()
    {
        await using var f = await Fixture.StartAsync();
        var mismatch = await f.Runtime.KillGenerationAsync(f.Id, f.Generation.AddSeconds(1), TimeSpan.FromSeconds(5), default);
        mismatch.Killed.ShouldBeFalse();
        f.Child.HasExited.ShouldBeFalse();
        var stopped = await f.Runtime.KillAsync(f.Id, TimeSpan.FromSeconds(5), default, "CpuSpinKilled");
        stopped.ExitSignal.ShouldBe(9);
        stopped.ExitCode.ShouldBe(0);
        stopped.ExitReason.ShouldBe("CpuSpinKilled");
        AssertIssued(stopped.ExitStopIntent, f.Id, f.Generation);
    }

    [Test]
    public async Task Restart_recovers_requested_intent()
    {
        await using var f = await Fixture.StartAsync();
        var manifest = PtyHostManifest.TryLoad(f.ManifestPath)!;
        var planted = Request(f.Id, f.Generation, f.Child) with { Outcome = ProcessStopOutcome.Issued,
            Provenance = ProcessStopProvenance.LiveIssuer, Durable = true };
        await f.Runtime.DisposeAsync();
        f.Child.Kill();
        await f.Child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        f.Host.Kill();
        await f.Host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        (manifest with { ExitCode = 0, ExitSignal = 9, ExitStopIntent = planted, ExitedAtUtc = DateTime.UtcNow }).SaveAtomic(f.ManifestPath);
        await using var recovered = f.NewRuntime();
        await recovered.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), default);
        recovered.Get(f.Id).ExitStopIntent!.Provenance.ShouldBe(ProcessStopProvenance.RecoveredUnverified);
        recovered.Get(f.Id).ExitSignal.ShouldBe(9);
    }

    [Test]
    public async Task Kill_all_records_each_owned_generation()
    {
        await using var a = await Fixture.StartAsync();
        var bId = Guid.NewGuid();
        var b = await a.LaunchAsync(bId);
        using var childB = Process.GetProcessById(b.Pid!.Value);
        using var hostB = Process.GetProcessById(b.HostPid!.Value);
        try
        {
            var rows = await a.Runtime.KillAllAsync(TimeSpan.FromSeconds(5), default);
            rows.Count.ShouldBe(2);
            foreach (var row in rows) { row.ExitSignal.ShouldBe(9); AssertIssued(row.ExitStopIntent, row.SessionId, a.Generation); }
            rows.Select(x => x.ExitStopIntent!.RequestId).Distinct().Count().ShouldBe(2);
        }
        finally
        {
            foreach (var owned in new[] { childB, hostB })
            { if (!owned.HasExited) owned.Kill(true); await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        }
    }

    [Test]
    public async Task Orphan_and_pending_kills_keep_identity()
    {
        await using var f = await Fixture.StartAsync();
        // The PID boundary used after orphan/pending sidecar authorization repeats start validation.
        f.Runtime.StopOwnedProcess(f.Child.Id, f.Child.StartTime.ToUniversalTime().AddSeconds(1), f.Id, f.Generation, ProcessStopTarget.Child);
        f.Child.HasExited.ShouldBeFalse();
        f.Runtime.StopOwnedProcess(f.Child.Id, f.Child.StartTime.ToUniversalTime(), f.Id, f.Generation, ProcessStopTarget.Child,
            LinuxProcessIdentity.TryRead(f.Child.Id));
        await f.Child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        AssertIssued(f.Store.Snapshot(f.Id, f.Generation, 9), f.Id, f.Generation);
    }

    [Test]
    public async Task Reused_pid_generation_missing_identity_and_previous_boot_are_refused()
    {
        await using var f = await Fixture.StartAsync();
        var identity = LinuxProcessIdentity.TryRead(f.Child.Id);
        identity.ShouldNotBeNull();
        // Replay a stale generation against the same PID, and a matching tick from an old boot.
        foreach (var stale in new LinuxProcessIdentity?[] { identity with { StartTicks = identity.StartTicks + 1 },
                     identity with { BootId = Guid.NewGuid().ToString("D") }, null })
        {
            f.Runtime.StopOwnedProcess(f.Child.Id, f.Child.StartTime.ToUniversalTime(), f.Id, f.Generation,
                ProcessStopTarget.Child, stale);
            f.Child.HasExited.ShouldBeFalse("an unproven or reused PID must never be signalled");
            f.Store.Snapshot(f.Id, f.Generation, 9).ShouldBeNull();
        }
        LinuxProcessIdentity.TryRead(int.MaxValue).ShouldBeNull("gone/unreadable proc identity is not a PID guess");
    }

    [Test]
    public void Proc_stat_parser_handles_spaces_and_parentheses_in_comm()
    {
        var stat = "42 (name (with) spaces)) S " + string.Join(' ', Enumerable.Range(4, 19));
        LinuxProcessIdentity.TryParseStartTicks(stat, out var ticks).ShouldBeTrue();
        ticks.ShouldBe(22);
        LinuxProcessIdentity.TryParseStartTicks("42 (bad) S 1", out _).ShouldBeFalse();
        LinuxProcessIdentity.TryParseStartTicks("42 missing comm", out _).ShouldBeFalse();
    }

    [Test]
    public async Task Cross_process_recorded_identity_kills_pending_child()
    {
        await using var f = await Fixture.StartAsync();
        var psi = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("$p = Get-Process -Id " + f.Child.Id + "; $s = [IO.File]::ReadAllText('/proc/" + f.Child.Id + "/stat'); " +
            "$fields = $s.Substring($s.LastIndexOf(')') + 2).Split(' ', [StringSplitOptions]::RemoveEmptyEntries); " +
            "@{ start = $p.StartTime.ToUniversalTime().ToString('O'); identity = @{ startTicks = [long]$fields[19]; bootId = [IO.File]::ReadAllText('/proc/sys/kernel/random/boot_id').Trim() } } | ConvertTo-Json -Compress");
        using var helper = Process.Start(psi)!;
        var output = await helper.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        helper.ExitCode.ShouldBe(0);
        var recorded = JsonNode.Parse(output)!;
        var recordedStart = DateTime.Parse(recorded["start"]!.GetValue<string>()).ToUniversalTime();
        Console.WriteLine($"cross-process start delta us={(recordedStart - f.Child.StartTime.ToUniversalTime()).TotalMicroseconds}");
        var sidecar = new HerdrPaneSidecar { SessionId = f.Id, WorkspaceKey = "owned", WorkspaceId = "owned",
            TabId = "owned", PaneId = "owned", ChildPid = f.Child.Id, ChildStartedAtUtc = recordedStart,
            LaunchedAtUtc = recordedStart, Origin = HerdrPaneOrigins.Launched, AcceptedStartedAt = f.Generation, Cwd = f.Root };
        var json = JsonSerializer.SerializeToNode(sidecar, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        json["childLinuxIdentity"] = recorded["identity"]!.DeepClone();
        var path = HerdrPaneSidecar.PathFor(f.Root, f.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json.ToJsonString());
        var persisted = HerdrPaneSidecar.TryLoad(path)!;
        await using var pending = f.NewRuntime();
        pending.AdoptSessionForTests(SessionRunnerRuntime.RunnerSession.CreatePendingHerdr(persisted,
            new SessionRunnerSettings { SessionLogPath = f.Root, CpuWatchdogEnabled = false },
            new SessionRunnerEventHub(), NullLogger.Instance, new SystemProcessLivenessProbe()));
        var result = await pending.KillAsync(f.Id, TimeSpan.FromSeconds(5), default);
        result.Status.ShouldBe("Exited", "cross-process identity must send the signal and observe child death");
        f.Child.HasExited.ShouldBeTrue("no signal sent / child alive");
        AssertIssued(pending.StopIntents.Snapshot(f.Id, f.Generation, 9), f.Id, f.Generation);
    }

    [Test]
    public async Task Custody_invocation_publishes_memory_before_helper_and_records_after()
    {
        await using var world = await RunnerCustodyReleaseTests.ReleaseWorld.Open(adopt: false);
        var binding = world.Binding;
        using var store = new ProcessStopIntentStore(Path.Combine(world.Root, "intent"));
        world.Runtime.CustodyForTests.StopIntents = store;
        world.Recovery.BeforeKill = () => store.Snapshot(binding.Generation.SessionId, binding.Generation.AcceptedStartedAt)!
            .Outcome.ShouldBe(ProcessStopOutcome.Requested);
        var status = await world.Runtime.ReadCustodyAsync(binding, true, default);
        status.State.ShouldBe(VerificationCustodyState.Unknown);
        status.Receipt.ShouldBeNull();
        var intent = store.Snapshot(binding.Generation.SessionId, binding.Generation.AcceptedStartedAt, 9);
        intent.ShouldNotBeNull();
        intent.Target.ShouldBe(ProcessStopTarget.Custody);
        intent.Outcome.ShouldBe(ProcessStopOutcome.Issued);
        intent.CustodyContainer.ShouldBe(world.ContainerId.ToString("D"));
        (await store.LastRecord).ShouldBeTrue();
    }

    [Test]
    public async Task Unobserved_or_failed_kill_retains_stop_debt()
    {
        await using (var f = await Fixture.StartAsync())
        await using (var pending = f.NewRuntime())
        {
            var settings = new SessionRunnerSettings { SessionLogPath = f.Root, CpuWatchdogEnabled = false };
            var sidecar = new HerdrPaneSidecar { SessionId = f.Id, WorkspaceKey = "owned", WorkspaceId = "owned",
                TabId = "owned", PaneId = "owned", ChildPid = f.Child.Id,
                ChildStartedAtUtc = f.Child.StartTime.ToUniversalTime().AddSeconds(1),
                LaunchedAtUtc = f.Child.StartTime.ToUniversalTime(), Origin = HerdrPaneOrigins.Launched,
                AcceptedStartedAt = f.Generation, Cwd = f.Root };
            var sidecarPath = HerdrPaneSidecar.PathFor(f.Root, f.Id);
            sidecar.SaveAtomic(sidecarPath);
            pending.AdoptSessionForTests(SessionRunnerRuntime.RunnerSession.CreatePendingHerdr(sidecar,
                settings, new SessionRunnerEventHub(), NullLogger.Instance, new SystemProcessLivenessProbe()));
            var requested = await pending.KillAsync(f.Id, TimeSpan.FromSeconds(5), default);
            requested.Status.ShouldBe("Starting", "an unobserved/refused PID stop must retain the pending owner");
            f.Child.HasExited.ShouldBeFalse("wrong creation identity must send nothing");
            File.Exists(sidecarPath).ShouldBeTrue("unobserved stop debt retains its sidecar");
            await f.Runtime.KillAsync(f.Id, TimeSpan.FromSeconds(5), default);
            await f.Child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var observed = await pending.KillAsync(f.Id, TimeSpan.FromSeconds(5), default);
            observed.Status.ShouldBe("Exited", "only the later real observed death discharges the pending stop");
            File.Exists(sidecarPath).ShouldBeFalse();
        }
        await using var world = await RunnerCustodyReleaseTests.ReleaseWorld.Open(adopt: false);
        using var store = new ProcessStopIntentStore(Path.Combine(world.Root, "intent"));
        world.Runtime.CustodyForTests.StopIntents = store;
        world.Recovery.Result = new(false, 13);
        world.Recovery.Pids = [42];
        var status = await world.Runtime.ReadCustodyAsync(world.Binding, true, default);
        status.State.ShouldBe(VerificationCustodyState.Unknown);
        status.Receipt.ShouldBeNull();
        status.Reason.ShouldBe("descendants_remain");
        store.Snapshot(world.Binding.Generation.SessionId, world.Binding.Generation.AcceptedStartedAt, 9)!
            .Outcome.ShouldBe(ProcessStopOutcome.Failed);
        File.Exists(world.Store.PathFor(world.Binding.ExecutionId, "accepted-receipt.json")).ShouldBeFalse();
    }

    [Test]
    public async Task Unwritable_record_directory_does_not_block_stop()
    {
        await using var f = await Fixture.StartAsync();
        f.Runtime.StopIntents = new("/proc/card1619-unwritable-" + f.Id.ToString("N"));
        f.Runtime.StopOwnedProcess(f.Child.Id, f.Child.StartTime.ToUniversalTime(), f.Id, f.Generation, ProcessStopTarget.Child,
            LinuxProcessIdentity.TryRead(f.Child.Id));
        await f.Child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        (await f.Runtime.StopIntents.LastRecord).ShouldBeFalse();
        AssertIssued(f.Runtime.StopIntents.Snapshot(f.Id, f.Generation, 9), f.Id, f.Generation);
    }

    [Test]
    public async Task Blocked_record_write_still_signals_within_bound()
    {
        await using var f = await Fixture.StartAsync();
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new FakeTimeProvider();
        f.Runtime.StopIntents = new(Path.Combine(f.Root, "blocked"), (_, _) => { entered.TrySetResult(); return pending.Task; }, clock);
        try
        {
            var timer = Stopwatch.StartNew();
            f.Runtime.StopOwnedProcess(f.Child.Id, f.Child.StartTime.ToUniversalTime(), f.Id, f.Generation, ProcessStopTarget.Child,
                LinuxProcessIdentity.TryRead(f.Child.Id));
            timer.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(250));
            await f.Child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            clock.Advance(TimeSpan.FromMilliseconds(100));
            (await f.Runtime.StopIntents.LastRecord.WaitAsync(TimeSpan.FromSeconds(1))).ShouldBeFalse();
            pending.Task.IsCompleted.ShouldBeFalse();
            AssertIssued(f.Runtime.StopIntents.Snapshot(f.Id, f.Generation, 9), f.Id, f.Generation);
        }
        finally { pending.TrySetResult(false); }
    }

    private static void AssertIssued(ProcessStopIntentDto? intent, Guid id, DateTime generation)
    {
        intent.ShouldNotBeNull();
        intent.SessionId.ShouldBe(id);
        intent.AcceptedStartedAt.ShouldBe(generation);
        intent.AttemptedSignal.ShouldBe(9);
        intent.Outcome.ShouldBe(ProcessStopOutcome.Issued);
        intent.Provenance.ShouldBe(ProcessStopProvenance.LiveIssuer);
    }
    private static ProcessStopIntentDto Request(Guid id, DateTime generation, Process child) => new() {
        SessionId = id, AcceptedStartedAt = generation, RequestId = Guid.NewGuid(), IssuerBootId = Guid.NewGuid(),
        HostInstanceId = Guid.NewGuid(), TargetPid = child.Id, TargetStartedAtUtc = child.StartTime.ToUniversalTime(),
        AttemptedSignal = 9, Target = ProcessStopTarget.Child, Path = ProcessStopPath.ProcessTree };

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "card1619-runner-stop-" + Guid.NewGuid().ToString("N"));
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Generation { get; } = SessionGeneration.Normalize(DateTime.UtcNow);
        public SessionRunnerRuntime Runtime { get; private set; } = null!;
        public ProcessStopIntentStore Store { get; private set; } = null!;
        public Process Child { get; private set; } = null!;
        public Process Host { get; private set; } = null!;
        public string ManifestPath => PtyHostManifest.PathFor(Path.Combine(Root, "pty-hosts", "manifests"), Id);
        public SessionRunnerRuntime NewRuntime() => new(Options.Create(new SessionRunnerSettings {
            SessionLogPath = Root, CpuWatchdogEnabled = false, PtyHostLingerHours = 0.01 }), NullLogger<SessionRunnerRuntime>.Instance);
        public static async Task<Fixture> StartAsync()
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.ProcessSpawn, "card1619-runner-stop-readiness");
            var f = new Fixture();
            try
            {
                Directory.CreateDirectory(f.Root);
                f.Runtime = f.NewRuntime();
                f.Runtime.StopIntents = f.Store = new(Path.Combine(f.Root, "stop-intents"));
                var dto = await f.LaunchAsync(f.Id);
                f.Child = Process.GetProcessById(dto.Pid!.Value);
                f.Host = Process.GetProcessById(dto.HostPid!.Value);
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public async Task<RunnerSessionDto> LaunchAsync(Guid id)
        {
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var dto = await Runtime.StartAsync(new(id, "/bin/sh", ["-c", "printf 'ready\\n'; read line"],
                new Dictionary<string, string>(), Root, 80, 24, AcceptedStartedAt: Generation), ct.Token);
            while (!Runtime.GetBuffer(id).Buffer.Contains("ready")) await Task.Delay(10, ct.Token);
            return dto;
        }
        public async ValueTask DisposeAsync()
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "card1619-runner-stop-owned-join");
            if (Runtime is not null) await Runtime.DisposeAsync();
            foreach (var owned in new[] { Child, Host })
            { if (owned is null) continue; if (!owned.HasExited) owned.Kill(true); await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); owned.Dispose(); }
            if (Runtime is not null) await Runtime.StopIntents.WorkerCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            if (Store is not null) { Store.Dispose(); await Store.WorkerCompletion.WaitAsync(TimeSpan.FromSeconds(5)); }
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
