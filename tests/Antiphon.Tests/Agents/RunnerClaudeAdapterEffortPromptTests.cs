using Antiphon.Agents.Pty;
using Antiphon.Agents.Pty.Tests;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;
namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class RunnerClaudeAdapterEffortPromptTests
{
    internal static AgentRegistrySettings Settings(int probe = 12000) => new()
    {
        ClaudeReadyQuietPeriodMs = 100, ClaudeReadyMaxWaitMs = 7000, ClaudeReadyMinTotalWaitMs = 0,
        ClaudeEffortPromptSettleMs = 7000, ClaudeInputProbeTimeoutMs = probe,
        ClaudeInputProbePollIntervalMs = 25, ClaudeInputProbeRetypeIntervalMs = 1000, ClaudeInputProbeClearTimeoutMs = 500
    };
    internal static AgentLaunchSpec Spec() => new("claude", AgentKind.ClaudeCode, "claude.exe",
        ["--model", "fable", "--effort", "xhigh"], new Dictionary<string,string>(), @"C:\test-owned", 120, 30, SessionId: Guid.NewGuid());

    [Test, Arguments(0), Arguments(1), Arguments(2)]
    public async Task Each_captured_dialog_clears_before_the_composer_probe(int capture)
    {
        var fake = new EffortTestScreen(capture);
        var client = new Client(fake);
        await using var adapter = new RunnerClaudeAdapter(client, Options.Create(Settings()));
        await adapter.StartAsync(Spec(), CancellationToken.None);
        var premature = false;
        fake.AfterWrite = (f, key) => { if (key.StartsWith("zz") && f.ClearObservations < 2) premature = true; };
        try
        {
            (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue(fake.Evidence);
            fake.AppliedEffort.ShouldBe("xhigh"); fake.AcceptedOption.ShouldBe(1);
            fake.Writes.First(w => w.Key == "\r").Highlight.ShouldBe(1);
            premature.ShouldBeFalse(); fake.TokenWrites.ShouldBe(1); fake.Composer.ShouldBeEmpty();
            adapter.LaunchBlock.ShouldBeNull();
        }
        finally { await adapter.KillAsync(TimeSpan.FromSeconds(1), CancellationToken.None); await adapter.Exited; }
    }

    [Test]
    public async Task Probe_failure_logs_token_elapsed_writes_and_screen()
    {
        const string screen = "Claude ready\n> \n? for shortcuts\nC449_DIAGNOSTIC_SCREEN";
        var fake = new EffortTestScreen { Dialog = false, Override = screen };
        var client = new Client(fake);
        var logger = new StartupLogger();
        var settings = Settings(1000);
        settings.ClaudeInputProbeRetypeIntervalMs = 100;
        await using var adapter = new RunnerClaudeAdapter(client, Options.Create(settings), logger: logger);
        var spec = Spec();
        await adapter.StartAsync(spec, CancellationToken.None);
        try
        {
            (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
            var entry = logger.Errors.ShouldHaveSingleItem();
            var token = ComposerInputProbe.TokenFor(spec.SessionId!.Value);
            entry.Fields.ShouldContainKey("Token");
            entry.Fields["Token"].ShouldBe(token);
            entry.Fields["Writes"].ShouldBe(fake.Writes.Count(w => w.Key == token));
            ((int)entry.Fields["Writes"]!).ShouldBeGreaterThan(0);
            ((double)entry.Fields["Elapsed"]!).ShouldBeGreaterThan(0);
            entry.Fields["Screen"].ShouldBe(screen);
            entry.Message.ShouldContain(token);
            entry.Message.ShouldContain("write(s)");
            entry.Message.ShouldContain("Screen:\n" + screen);
            adapter.LaunchBlock.ShouldBeNull();
        }
        finally { await adapter.KillAsync(TimeSpan.FromSeconds(1), CancellationToken.None); await adapter.Exited; }
    }

    [Test]
    public async Task Attach_without_launch_effort_preserves_current()
    {
        var fake = new EffortTestScreen { Highlight = 2 };
        var client = new Client(fake);
        var logger = new StartupLogger();
        await using var adapter = new RunnerClaudeAdapter(client, Options.Create(Settings()), logger: logger);
        await adapter.AttachAsync(Guid.NewGuid(), CancellationToken.None);
        try { (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue(fake.Evidence); fake.AppliedEffort.ShouldBe("xhigh"); logger.Messages.ShouldContain(m => m.Contains("absent (Keep option)")); }
        finally { await adapter.KillAsync(TimeSpan.FromSeconds(1), CancellationToken.None); await adapter.Exited; }
    }

    [Test]
    public async Task A_dialog_appearing_during_the_minimum_floor_is_resolved()
    {
        var fake = new EffortTestScreen { Dialog = false };
        var client = new Client(fake);
        var settings = Settings(); settings.ClaudeReadyMinTotalWaitMs = 800;
        await using var adapter = new RunnerClaudeAdapter(client, Options.Create(settings));
        await adapter.StartAsync(Spec(), CancellationToken.None);
        var early = false; var appeared = false;
        fake.OnSnapshot = f => { if (f.Clock.ElapsedMilliseconds < 400) early = true;
            else if (!appeared) { appeared = true; f.Dialog = true; } };
        try
        {
            (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue(fake.Evidence);
            early.ShouldBeTrue(); appeared.ShouldBeTrue(); fake.AppliedEffort.ShouldBe("xhigh");
            fake.Writes.First(w => w.Key.StartsWith("zz")).At.TotalMilliseconds.ShouldBeGreaterThanOrEqualTo(800);
        }
        finally { await adapter.KillAsync(TimeSpan.FromSeconds(1), CancellationToken.None); await adapter.Exited; }
    }

    [Test]
    public async Task An_effort_dialog_that_never_clears_fails_within_its_budget()
    {
        var fake = new EffortTestScreen { SwallowEnters = 100 };
        var client = new Client(fake);
        await using var adapter = new RunnerClaudeAdapter(client, Options.Create(Settings()));
        await adapter.StartAsync(Spec(), CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var task = adapter.WaitForReadyAsync(cts.Token);
        try
        {
            (await Task.WhenAny(task, Task.Delay(9000))).ShouldBe(task, "operation completed before watchdog");
            (await task).ShouldBeFalse(fake.Evidence);
            adapter.LaunchBlock.ShouldNotBeNull(); adapter.LaunchBlock.Kind.ShouldBe(AgentLaunchBlockKind.EffortDialogNotCleared);
            foreach (var field in new[] { "requested=xhigh", "current=xhigh", "suggested=high", "selected=Keep", "Enter=3" }) adapter.LaunchBlock.Reason.ShouldContain(field);
            fake.Writes.Count.ShouldBe(3); fake.Writes.All(w => w.Key == "\r").ShouldBeTrue(); fake.TokenWrites.ShouldBe(0);
        }
        finally { await cts.CancelAsync(); try { await task; } catch (OperationCanceledException) { }
            await adapter.KillAsync(TimeSpan.FromSeconds(1), CancellationToken.None); await adapter.Exited; }
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task Disabling_the_probe_does_not_disable_effort_resolution(bool stuck)
    {
        var fake = new EffortTestScreen { SwallowEnters = stuck ? 100 : 0 };
        var client = new Client(fake);
        await using var adapter = new RunnerClaudeAdapter(client, Options.Create(Settings(0)));
        await adapter.StartAsync(Spec(), CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var task = adapter.WaitForReadyAsync(cts.Token);
        try
        {
            (await Task.WhenAny(task, Task.Delay(9000))).ShouldBe(task, "operation completed before watchdog");
            (await task).ShouldBe(!stuck, fake.Evidence); fake.TokenWrites.ShouldBe(0);
            if (stuck) adapter.LaunchBlock!.Kind.ShouldBe(AgentLaunchBlockKind.EffortDialogNotCleared);
            else fake.AppliedEffort.ShouldBe("xhigh");
        }
        finally { await cts.CancelAsync(); try { await task; } catch (OperationCanceledException) { }
            await adapter.KillAsync(TimeSpan.FromSeconds(1), CancellationToken.None); await adapter.Exited; }
    }

    [Test]
    public async Task Process_exit_during_the_effort_dialog_stops_readiness_input()
    {
        var fake = new EffortTestScreen();
        var client = new Client(fake);
        await using var adapter = new RunnerClaudeAdapter(client, Options.Create(Settings()));
        var spec = Spec();
        await adapter.StartAsync(spec, CancellationToken.None);
        var task = adapter.WaitForReadyAsync(CancellationToken.None);
        await adapter.KillAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(2));
        (await task.WaitAsync(TimeSpan.FromSeconds(2))).ShouldBeFalse();
        fake.Writes.ShouldBeEmpty();
    }

    internal sealed class StartupLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public List<(string Message, Dictionary<string, object?> Fields)> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            Messages.Add(message);
            if (level == LogLevel.Error)
                Errors.Add((message, ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(kv => kv.Key, kv => kv.Value)));
        }
    }

    internal sealed class Client(EffortTestScreen screen) : ISessionRunnerClient
    {
        private readonly DateTime _startedAt = DateTime.UtcNow;
        private long _sequence;
        private bool _exited;
        public int KillCalls { get; private set; }
        public bool KillTokenWasCanceled { get; private set; }
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) => GetAsync(sessionId, ct);
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => Task.FromResult(new SessionRunnerSessionDto(sessionId, 4321,
            _startedAt, _exited ? "Exited" : "Running", _exited ? 0 : null, AgentExitReason.Unknown, _sequence));
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) => Task.FromResult(new SessionRunnerBufferDto(sessionId, screen.Raw, _sequence));
        public async Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            new(sessionId, screen.Raw, await screen.SnapshotAsync(ct), _sequence, _startedAt);
        public async Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) { _sequence++; await screen.WriteAsync(input, ct); }
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        { KillCalls++; KillTokenWasCanceled = ct.IsCancellationRequested; _exited = true; return GetAsync(sessionId, ct); }

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
