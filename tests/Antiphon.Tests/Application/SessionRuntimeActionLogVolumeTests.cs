using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1890. Routine coordinator lines are Debug. A retry or deferral stays Information
/// the first time, then at most once per ten minutes. Failure Warnings are not throttled.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class SessionRuntimeActionLogVolumeTests
{
    [Test]
    public async Task Routine_phase_and_worker_lines_are_Debug()
    {
        var log = new RecordingLogger<SessionRuntimeActionCoordinator>();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = services => services.AddSingleton<ILogger<SessionRuntimeActionCoordinator>>(log),
        });
        await h.Runtime.ObserveTranscriptAsync(ToolResult(h, 1), default);
        await h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>().DrainPageAsync(h.SessionId, default);

        var worker = log.Entries.Where(e => e.Message.StartsWith("Action worker ", StringComparison.Ordinal)).ToList();
        var phase = PhaseLines(log.Entries).Where(e => e.Message.Contains("attempts 0", StringComparison.Ordinal)).ToList();
        worker.ShouldNotBeEmpty();
        phase.ShouldNotBeEmpty();
        worker.ShouldAllBe(e => e.Level == LogLevel.Debug);
        phase.ShouldAllBe(e => e.Level == LogLevel.Debug);
    }

    [Test]
    public async Task Debt_query_is_skipped_when_Debug_is_off()
    {
        var off = await DrainCommandCountAsync(debug: false);
        var on = await DrainCommandCountAsync(debug: true);
        off.ShouldBeLessThan(on,
            $"Debug-off drain ran {off} commands and Debug-on ran {on}; the debt query fills only the worker line");
    }

    [Test]
    public async Task Retry_phase_line_is_Information_once_per_interval()
    {
        await AssertRetryWarningOnEveryThrowAsync();

        var clock = Clock();
        var log = new RecordingLogger<SessionRuntimeActionCoordinator>();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await OpenAsync(schema.ConnectionString, clock, log);
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        await SeedDeferredStatusAsync(h, attempts: 31);

        log.Entries.Clear();
        (await actions.DrainPageAsync(h.SessionId, default)).ShouldBeFalse();
        PhaseLines(log.Entries).ShouldAllBe(e => e.Level == LogLevel.Information);
        await using (var db = h.CreateObserverContext())
        {
            var row = await db.SessionRuntimeActions.SingleAsync();
            row.FailureReason.ShouldBe("Deferred");
            row.Attempts.ShouldBe(31);
        }

        clock.Advance(TimeSpan.FromSeconds(31));
        log.Entries.Clear();
        (await actions.DrainPageAsync(h.SessionId, default)).ShouldBeFalse();
        var pass2 = PhaseLines(log.Entries);
        pass2.ShouldNotBeEmpty();
        pass2.ShouldAllBe(e => e.Level == LogLevel.Debug);

        clock.Advance(TimeSpan.FromMinutes(10));
        log.Entries.Clear();
        (await actions.DrainPageAsync(h.SessionId, default)).ShouldBeFalse();
        var pass3 = PhaseLines(log.Entries);
        pass3.ShouldNotBeEmpty();
        pass3.ShouldAllBe(e => e.Level == LogLevel.Information);

        await using (var db = h.CreateObserverContext())
            await db.SessionRuntimeActions.ExecuteDeleteAsync();
        log.Entries.Clear();
        await h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>().RequestAsync(h.SessionId, "status", default);
        (await actions.DrainPageAsync(h.SessionId, default)).ShouldBeFalse();
        var fresh = PhaseLines(log.Entries);
        fresh.ShouldNotBeEmpty();
        fresh.ShouldAllBe(e => e.Level == LogLevel.Debug && e.Message.Contains("attempts 0", StringComparison.Ordinal));

        clock.Advance(TimeSpan.FromSeconds(2));
        log.Entries.Clear();
        (await actions.DrainPageAsync(h.SessionId, default)).ShouldBeFalse();
        var nextDrain = PhaseLines(log.Entries);
        nextDrain.ShouldNotBeEmpty();
        nextDrain.ShouldAllBe(e => e.Level == LogLevel.Information && e.Message.Contains("attempts 1", StringComparison.Ordinal));
    }

    private static async Task AssertRetryWarningOnEveryThrowAsync()
    {
        var clock = Clock();
        var log = new RecordingLogger<SessionRuntimeActionCoordinator>();
        var bus = new ThrowingTranscriptBus();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            TimeProvider = clock,
            ConfigureServices = services =>
            {
                services.AddSingleton<ILogger<SessionRuntimeActionCoordinator>>(log);
                services.AddSingleton<IEventBus>(bus);
            },
        });
        await h.Runtime.ObserveTranscriptAsync(ToolResult(h, 1), default);
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        (await actions.DrainPageAsync(h.SessionId, default)).ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(2));
        (await actions.DrainPageAsync(h.SessionId, default)).ShouldBeFalse();
        bus.Hits.ShouldBe(2);
        log.Entries.Count(e => e.Level == LogLevel.Warning
            && e.Message.Contains("Action phase retained for retry", StringComparison.Ordinal)).ShouldBe(2);
    }

    private static async Task<int> DrainCommandCountAsync(bool debug)
    {
        var counter = new CountingCommandInterceptor();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureDbContext = options => options.AddInterceptors(counter),
            ConfigureServices = services => services.AddSingleton<ILogger<SessionRuntimeActionCoordinator>>(new GateLogger(debug)),
        });
        var before = counter.Commands.Count;
        await h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>().DrainPageAsync(h.SessionId, default);
        return counter.Commands.Count - before;
    }

    private static async Task<BridgeQueueHarness> OpenAsync(string connectionString, FakeTimeProvider clock,
        RecordingLogger<SessionRuntimeActionCoordinator> log) =>
        await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = connectionString,
            TimeProvider = clock,
            ConfigureServices = services => services.AddSingleton<ILogger<SessionRuntimeActionCoordinator>>(log),
        });

    private static async Task SeedDeferredStatusAsync(BridgeQueueHarness h, int attempts)
    {
        await h.InsertTurnAsync("previous complete prompt", "previous complete answer");
        await h.Queue.EnqueueAsync(h.SessionId, "held line", MessageSendMode.WhenIdle, default, deliverIfIdle: false);
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        await actions.RequestAsync(h.SessionId, "status", default);
        h.Runtime.SetTestPending(h.SessionId, HerdrPendingReasons.Unreachable);
        await using var db = h.CreateObserverContext();
        await db.SessionRuntimeActions.ExecuteUpdateAsync(setters => setters
            .SetProperty(a => a.Attempts, attempts)
            .SetProperty(a => a.FailureReason, "Deferred")
            .SetProperty(a => a.Phase, SessionRuntimeActionCoordinator.QueuePhase)
            .SetProperty(a => a.DueAt, h.Now));
    }

    private static List<RecordingLogEntry> PhaseLines(List<RecordingLogEntry> entries) =>
        entries.Where(e => e.Message.StartsWith("Action ", StringComparison.Ordinal)
            && e.Message.Contains("attempts ", StringComparison.Ordinal)
            && !e.Message.StartsWith("Action worker ", StringComparison.Ordinal)
            && !e.Message.StartsWith("Action phase ", StringComparison.Ordinal)
            && !e.Message.StartsWith("Action hints ", StringComparison.Ordinal)).ToList();

    private static SessionRunnerTranscriptEvent ToolResult(BridgeQueueHarness h, long sequence) =>
        new(h.SessionId, sequence, TranscriptKinds.ToolResult, Guid.NewGuid().ToString("N"), null,
            h.Clock.GetUtcNow(), "assistant", "fixture result", null, null, null, null, null);

    private static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));

    private sealed class GateLogger(bool debug) : ILogger<SessionRuntimeActionCoordinator>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => debug || logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) { }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class ThrowingTranscriptBus : IEventBus
    {
        public int Hits;
        public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
        {
            if (eventName == "SessionTranscript")
            {
                Hits++;
                throw new IOException("phase-zero fixture");
            }
            return Task.CompletedTask;
        }

        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
