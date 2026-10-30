using System.Diagnostics;
using System.Reflection;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Client;
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
        bool signalReturned = false;
        Fixture? fixture = null;
        fixture = await Fixture.StartAsync((intent, ct) => {
            signalReturned.ShouldBeTrue("writer entry must follow the actual successful signal syscall");
            var write = typeof(ProcessStopIntentStore).GetMethod("WriteDiagnosticAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (Task<bool>)write.Invoke(fixture!.Store, [intent, ct])!;
        });
        await using var f = fixture;
        f.Store.Attempt(f.Request(9), () => { Kill(f.Child.Id, 9).ShouldBe(0); signalReturned = true; return true; });
        var stopped = await f.Exit.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
        foreach (var synchronousBlock in new[] { false, true })
        {
            using var release = new ManualResetEventSlim();
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var f = await Fixture.StartAsync((_, _) => { entered.TrySetResult(); if (synchronousBlock) release.Wait(); return pending.Task; });
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
            finally { release.Set(); pending.TrySetResult(false); }
        }
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
        var runner = (PtyAgentRunner)typeof(HostSession).GetField("_runner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Host)!;
        var record = runner.StopSignal;
        if (record is not null) runner.StopSignal = (pid, signal, path, send) =>
            record(pid, signal, path, signal == 9 ? () => false : send);
        (await f.StopAsync()).ExitSignal.ShouldBe(1);
        f.Store.Snapshot(f.Id, f.Generation, 1)!.AttemptedSignal.ShouldBe(1);
        f.Store.Snapshot(f.Id, f.Generation, 9)!.Outcome.ShouldBe(ProcessStopOutcome.Failed);
        await using var disposal = await Fixture.StartAsync();
        await disposal.DisposeHostAsync();
        (await disposal.Exit.Task.WaitAsync(TimeSpan.FromSeconds(5))).ExitSignal.ShouldBe(1);
        var hangup = disposal.Store.Snapshot(disposal.Id, disposal.Generation, 1);
        hangup.ShouldNotBeNull();
        hangup.Path.ShouldBe(ProcessStopPath.Disposal);
        hangup.Outcome.ShouldBe(ProcessStopOutcome.Requested,
            "master closure and upstream swallowed SIGHUP cannot prove a successful syscall");
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
        var id = Guid.NewGuid();
        var launcher = new PtyHostLauncher(new ShadowCopyStore(Path.Combine(f.Root, "bin")), AppContext.BaseDirectory) { StopIntents = f.Store };
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        try
        {
            await Should.ThrowAsync<OperationCanceledException>(() => launcher.LaunchDetachedAsync(id,
                Path.Combine(f.Root, "launch-manifests"), launchTimeout: TimeSpan.FromSeconds(15), ct: cancel.Token));
            var attempt = f.Store.Snapshot(id, DateTime.MinValue, 9);
            attempt.ShouldNotBeNull("actual launch-failure host cleanup must use the recorder");
            attempt.Outcome.ShouldBe(ProcessStopOutcome.Issued);
            attempt.Target.ShouldBe(ProcessStopTarget.Host);
            attempt.Path.ShouldBe(ProcessStopPath.LaunchCleanup);
        }
        finally
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited && process.MainModule?.FileName is { } path && path.StartsWith(launcher.CurrentShadowDir + Path.DirectorySeparatorChar))
                        { process.Kill(true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
            }
        }
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
        // The live observer publishes immediately; recovery reads diagnostics separately and
        // must strip even a planted LiveIssuer claim. CP-11 carries this through actual adoption.
        var recovered = ProcessStopIntentStore.ReadRecovered(f.RecordRoot, f.Id, f.Generation);
        recovered.ShouldNotBeNull();
        recovered.Provenance.ShouldBe(ProcessStopProvenance.RecoveredUnverified);
    }

    private sealed class Phase(string operation) : IDisposable
    {
        private readonly Stopwatch _timer = Stopwatch.StartNew();
        public void Dispose() => Console.WriteLine(JsonSerializer.Serialize(new {
            kind = "card1619.phase", operation, elapsedSeconds = _timer.Elapsed.TotalSeconds,
            testId = TestContext.Current?.Metadata.TestDetails.TestId,
            run = Environment.GetEnvironmentVariable("ANTIPHON_TEST_PHASE_RUN_ID"),
            group = Environment.GetEnvironmentVariable("ANTIPHON_TEST_PHASE_GROUP_ID")
        }));
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
        private bool _hostDisposed;
        private readonly Stopwatch _signalClock = new();
        public TimeSpan SignalLatency { get; private set; }
        public static async Task<Fixture> StartAsync(Func<ProcessStopIntentDto, CancellationToken, Task<bool>>? writer = null, bool unwritable = false)
        {
            using var phase = new Phase("card1619-host-stop-readiness");
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
            using var phase = new Phase("card1619-host-stop-exit");
            _signalClock.Restart();
            (await Host.KillAsync(TimeSpan.FromSeconds(5))).ShouldBeTrue();
            return await Exit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        public async Task DisposeHostAsync()
        {
            if (_hostDisposed || Host is null) return;
            _hostDisposed = true;
            await Host.DisposeAsync();
        }
        public async ValueTask DisposeAsync()
        {
            using var phase = new Phase("card1619-host-owned-join");
            if (Child is not null)
            {
                if (!Child.HasExited) Child.Kill(true);
                await Child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Child.Dispose();
            }
            await DisposeHostAsync();
            if (_reader is not null) await _reader.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)] private static extern int Kill(int pid, int signal);
}
