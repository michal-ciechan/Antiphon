using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

[Category("Integration")]
[NativeLinuxOnly("CARD-1619 real Linux stop boundary")]
[ParallelLimiter<ProcessSpawnLimit>]
public class Card1619StopIntentTests
{
    [Test]
    public async Task Signal_precedes_durable_record_when_storage_works()
    {
        await using var f = await Fixture.StartAsync();
        var stopped = await f.StopAsync();
        stopped.ExitSignal.ShouldBe(9);
        var intent = stopped.ExitStopIntent;
        intent.ShouldNotBeNull();
        intent.Outcome.ShouldBe(ProcessStopOutcome.Issued);
        intent.Provenance.ShouldBe(ProcessStopProvenance.LiveIssuer);
        (await f.Store.LastRecord).ShouldBeTrue();
        f.Store.Snapshot(f.Id, f.Generation, 9)!.Durable.ShouldBeTrue();
        var path = Path.Combine(f.RecordRoot, ProcessStopIntentStore.FileNameFor(intent));
        var record = JsonSerializer.Deserialize<ProcessStopIntentDto>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        record.Durable.ShouldBeTrue("only post-rename directory-fsync records are durable");
    }

    [Test]
    public async Task Blocked_record_write_still_signals_within_bound()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var f = await Fixture.StartAsync((_, _) => { entered.TrySetResult(); return pending.Task; });
        try
        {
            var timer = Stopwatch.StartNew();
            var stop = f.Host.KillAsync(TimeSpan.FromSeconds(5));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            (await stop.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
            timer.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
            var exit = await f.Exit.Task.WaitAsync(TimeSpan.FromSeconds(1));
            exit.ExitSignal.ShouldBe(9);
            exit.ExitStopIntent.ShouldNotBeNull();
            exit.ExitStopIntent.Provenance.ShouldBe(ProcessStopProvenance.LiveIssuer);
            (await f.Store.LastRecord.WaitAsync(TimeSpan.FromSeconds(1))).ShouldBeFalse();
            pending.Task.IsCompleted.ShouldBeFalse("deadline must contain a writer that never finishes");
            f.Store.Snapshot(f.Id, f.Generation, 9)!.Durable.ShouldBeFalse();
            f.SignalLatency.ShouldBeLessThan(TimeSpan.FromMilliseconds(250));
        }
        finally { pending.TrySetResult(false); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Write_failure_still_stops_and_preserves_live_issuer_attribution(bool unwritable)
    {
        await using var f = await Fixture.StartAsync(unwritable ? null : (_, _) => throw new IOException("ENOSPC"), unwritable);
        var exit = await f.StopAsync();
        exit.ExitSignal.ShouldBe(9);
        exit.ExitStopIntent.ShouldNotBeNull();
        exit.ExitStopIntent.Outcome.ShouldBe(ProcessStopOutcome.Issued);
        exit.ExitStopIntent.Provenance.ShouldBe(ProcessStopProvenance.LiveIssuer);
        (await f.Store.LastRecord).ShouldBeFalse();
        f.Store.Snapshot(f.Id, f.Generation, 9)!.Durable.ShouldBeFalse();
    }

    [Test]
    public async Task Issued_update_follows_syscall()
    {
        await using var f = await Fixture.StartAsync();
        var request = f.Request(9);
        ProcessStopIntentDto? during = null;
        var result = f.Store.Attempt(request, () => {
            during = f.Store.Snapshot(f.Id, f.Generation);
            Kill(f.Child.Id, 9).ShouldBe(0);
            return true;
        });
        var exit = await f.Exit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        exit.ExitSignal.ShouldBe(9);
        during.ShouldNotBeNull();
        during.Outcome.ShouldBe(ProcessStopOutcome.Requested);
        result.Outcome.ShouldBe(ProcessStopOutcome.Issued);
    }

    [Test]
    public async Task Crash_before_send_is_only_requested()
    {
        await using var f = await Fixture.StartAsync();
        var result = f.Store.Attempt(f.Request(9), () => null);
        result.Outcome.ShouldBe(ProcessStopOutcome.Requested);
        f.Store.Snapshot(f.Id, f.Generation)!.Outcome.ShouldBe(ProcessStopOutcome.Requested);
        f.Child.HasExited.ShouldBeFalse();
        await f.StopAsync();
    }

    [Test]
    public async Task Crash_after_send_before_update_is_unknown()
    {
        await using var f = await Fixture.StartAsync();
        var result = f.Store.Attempt(f.Request(9), () => { Kill(f.Child.Id, 9).ShouldBe(0); return null; });
        var exit = await f.Exit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        exit.ExitSignal.ShouldBe(9);
        result.Outcome.ShouldBe(ProcessStopOutcome.Requested);
        f.Store.Snapshot(f.Id, f.Generation, 9)!.Outcome.ShouldBe(ProcessStopOutcome.Requested);
    }

    [Test]
    public async Task Fallback_records_SIGHUP_separately()
    {
        await using var f = await Fixture.StartAsync();
        f.Store.Attempt(f.Request(9), () => false).Outcome.ShouldBe(ProcessStopOutcome.Failed);
        f.Store.Attempt(f.Request(1) with { Path = ProcessStopPath.PortaFallback }, () => Kill(f.Child.Id, 1) == 0);
        (await f.Exit.Task.WaitAsync(TimeSpan.FromSeconds(5))).ExitSignal.ShouldBe(1);
        f.Store.Snapshot(f.Id, f.Generation, 1)!.AttemptedSignal.ShouldBe(1);
        f.Store.Snapshot(f.Id, f.Generation, 9)!.Outcome.ShouldBe(ProcessStopOutcome.Failed);
    }

    [Test]
    public async Task Natural_exit_race_is_not_our_kill()
    {
        await using var f = await Fixture.StartAsync();
        await f.Host.SendLineAsync("exit", default);
        (await f.Exit.Task.WaitAsync(TimeSpan.FromSeconds(5))).ExitSignal.ShouldBe(0);
        await f.Host.KillAsync(TimeSpan.FromSeconds(5));
        f.Store.Snapshot(f.Id, f.Generation, 9).ShouldBeNull();
    }

    [Test]
    public async Task Host_kill_is_not_child_wait_evidence()
    {
        await using var f = await Fixture.StartAsync();
        // Same recorder, separately identified host/helper targets cannot become child wait evidence.
        var attempt = f.Store.Attempt(f.Request(9) with { Target = ProcessStopTarget.Host }, () => true);
        attempt.Outcome.ShouldBe(ProcessStopOutcome.Issued);
        attempt.Target.ShouldBe(ProcessStopTarget.Host);
        f.Host.GetStatus().ExitSignal.ShouldBeNull();
        f.Host.GetStatus().Status.ShouldBe(PtyHostStatus.Running);
        f.Child.HasExited.ShouldBeFalse();
        await f.StopAsync();
    }

    [Test]
    public async Task Planted_issued_record_and_self_signal_remain_unconfirmed()
    {
        await using var f = await Fixture.StartAsync();
        var planted = f.Request(9) with { Outcome = ProcessStopOutcome.Issued, Provenance = ProcessStopProvenance.LiveIssuer, Durable = true };
        Directory.CreateDirectory(f.RecordRoot);
        File.WriteAllText(Path.Combine(f.RecordRoot, ProcessStopIntentStore.FileNameFor(planted)),
            JsonSerializer.Serialize(planted, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await f.Host.SendLineAsync("self", default);
        var exit = await f.Exit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        exit.ExitSignal.ShouldBe(9);
        exit.ExitStopIntent.ShouldNotBeNull();
        exit.ExitStopIntent.Provenance.ShouldBe(ProcessStopProvenance.RecoveredUnverified);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "card1619-stop-" + Guid.NewGuid().ToString("N"));
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Generation { get; } = SessionGeneration.Normalize(DateTime.UtcNow);
        public string RecordRoot { get; private set; } = "";
        public HostSession Host { get; private set; } = null!;
        public ProcessStopIntentStore Store { get; private set; } = null!;
        public Process Child { get; private set; } = null!;
        public TaskCompletionSource<ExitedMessage> Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _reader;
        private readonly Stopwatch _signalClock = new();
        public TimeSpan SignalLatency { get; private set; }
        public static async Task<Fixture> StartAsync(Func<ProcessStopIntentDto, CancellationToken, Task<bool>>? writer = null, bool unwritable = false)
        {
            var f = new Fixture();
            try
            {
                Directory.CreateDirectory(f.Root);
                f.RecordRoot = unwritable ? "/proc/card1619-unwritable-" + f.Id.ToString("N") : Path.Combine(f.Root, "stop-intents");
                f.Store = new(f.RecordRoot, writer);
                f.Host = new(new PtyHostOptions { SessionId = f.Id, PipeName = "unused", ManifestDir = Path.Combine(f.Root, "manifests"), LingerTtl = TimeSpan.FromSeconds(1) }, new HostLog(null)) { StopIntents = f.Store };
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var channel = Channel.CreateUnbounded<PtyHostMessage>();
                f.Host.Attach(0, channel.Writer);
                f._reader = Task.Run(async () => {
                    await foreach (var value in channel.Reader.ReadAllAsync())
                    {
                        if (value is OutputMessage output && output.Chunk.Contains("ready")) ready.TrySetResult();
                        if (value is ExitedMessage exit) { f.SignalLatency = f._signalClock.Elapsed; f.Exit.TrySetResult(exit); return; }
                    }
                });
                var launched = (LaunchedMessage)await f.Host.LaunchAsync(new LaunchMessage("/bin/sh",
                    ["-c", "printf 'ready\n'; read line; if [ \"$line\" = self ]; then kill -9 $$; fi; exit 0"],
                    new Dictionary<string, string>(), f.Root, 80, 24, 0, false, null, AcceptedStartedAt: f.Generation), default);
                f.Child = Process.GetProcessById(launched.ChildPid);
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
                f._signalClock.Start();
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public ProcessStopIntentDto Request(int signal) => new() { SessionId = Id, AcceptedStartedAt = Generation,
            RequestId = Guid.NewGuid(), IssuerBootId = Store.BootId, HostInstanceId = Guid.NewGuid(), RequestedAtUtc = DateTime.UtcNow,
            TargetPid = Child.Id, TargetStartedAtUtc = Child.StartTime.ToUniversalTime(), Target = ProcessStopTarget.Child,
            AttemptedSignal = signal, Path = ProcessStopPath.ProcessTree };
        public async Task<ExitedMessage> StopAsync()
        {
            _signalClock.Restart();
            (await Host.KillAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
            return await Exit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        public async ValueTask DisposeAsync()
        {
            if (Child is not null)
            {
                if (!Child.HasExited) Child.Kill(true);
                await Child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Child.Dispose();
            }
            if (Host is not null) await Host.DisposeAsync();
            if (_reader is not null) await _reader.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)] private static extern int Kill(int pid, int signal);
}
