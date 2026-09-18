using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentSessionRuntimeTests
{
    [Test]
    public async Task C561_a_NUL_in_tool_result_text_persists_as_U_FFFD()
    {
        await using var f = await PersistFixture.CreateAsync();
        var poison = "No Instance(s) Available.\r\r\n\r\0\n\0";
        await f.Runtime.ObserveTranscriptAsync(
            TranscriptEvent(f.SessionId, 1, TranscriptKinds.ToolResult, "u-nul", poison), default);

        var rows = await f.RowsAsync();
        rows.ShouldHaveSingleItem();
        rows[0].Text.ShouldBe("No Instance(s) Available.\r\r\n\r\uFFFD\n\uFFFD");
        rows[0].Text.ShouldNotContain('\0');
        f.Logs.ShouldNotContain(l => l.Contains("Failed to persist"));
        GetPayloadValue<string>(
            f.EventBus.PublishedEvents.Single(e => e.EventName == "SessionTranscript").Payload,
            "text").ShouldContain('\0');
        (await f.Runtime.PersistTranscriptAsync(f.SessionId, [
            TranscriptEvent(f.SessionId, 2, TranscriptKinds.ToolResult, "u-nul-2", poison)
        ])).LastStoredSeq.ShouldBe(2);
    }

    [Test]
    public async Task C561_NUL_in_every_string_member_is_replaced_and_the_sanitised_uuid_dedups()
    {
        await using var f = await PersistFixture.CreateAsync();
        var raw = new SessionRunnerTranscriptEvent(
            f.SessionId, 1, TranscriptKinds.ToolCall, "u\0id", "p\0", DateTimeOffset.UtcNow,
            "us\0er", "t\0", "Ba\0sh", "{\"x\":\"\0\"}", null, null, "end\0",
            Model: "m\0");
        (await f.Runtime.PersistTranscriptAsync(f.SessionId, [raw])).LastStoredSeq.ShouldBe(1);
        var row = (await f.RowsAsync()).ShouldHaveSingleItem();
        row.Uuid.ShouldBe("u\uFFFDid");
        row.ParentUuid.ShouldBe("p\uFFFD");
        row.Role.ShouldBe("us\uFFFDer");
        row.ToolName.ShouldBe("Ba\uFFFDsh");
        row.ToolInput.ShouldBe("{\"x\":\"\uFFFD\"}");
        row.StopReason.ShouldBe("end\uFFFD");
        row.Model.ShouldBe("m\uFFFD");
        row.Text.ShouldBe("t\uFFFD");
        (await f.Runtime.PersistTranscriptAsync(f.SessionId, [raw])).LastStoredSeq.ShouldBeNull();
        (await f.RowsAsync()).ShouldHaveSingleItem();
        f.Logs.ShouldNotContain(l => l.Contains("Failed to persist"));
    }

    [Test]
    public async Task C561_a_row_that_exceeds_a_column_lands_as_a_clipped_stub_and_its_siblings_land_verbatim()
    {
        await using var f = await PersistFixture.CreateAsync();
        var result = await f.Runtime.PersistTranscriptAsync(f.SessionId, [
            TranscriptEvent(f.SessionId, 1, TranscriptKinds.UserPrompt, "u1", "hello"),
            new SessionRunnerTranscriptEvent(
                f.SessionId, 2, TranscriptKinds.ToolCall, "u2", null, DateTimeOffset.UtcNow,
                "assistant", null, new string('x', 201), "SECRET-INPUT-MARKER {...}", null, null, null),
            TurnEndEvent(TranscriptKinds.StopReasons.EndTurn, f.SessionId, "u3") with { Sequence = 3 }
        ]);
        var rows = await f.RowsAsync();
        rows.Count.ShouldBe(3);
        rows[0].Text.ShouldBe("hello");
        rows[2].Kind.ShouldBe(TranscriptKinds.TurnEnd);
        rows[2].StopReason.ShouldBe("end_turn");
        var stub = rows[1];
        stub.ShouldNotBeNull();
        stub.Uuid.ShouldBe("u2");
        stub.Kind.ShouldBe(TranscriptKinds.ToolCall);
        stub.ToolName!.Length.ShouldBe(200);
        stub.ToolName.ShouldEndWith("…");
        stub.ToolName[..199].ShouldBe(new string('x', 199));
        stub.Text.ShouldBe("[transcript text not persistable: 22001]");
        stub.ToolInput.ShouldBeNull();
        stub.Sequence.ShouldBe(2);
        result.AddedTurnBoundary.ShouldBeTrue();
        result.LastStoredSeq.ShouldBe(3);
        var warning = f.Logs.Where(l => l.StartsWith("[Warning]")).ShouldHaveSingleItem();
        warning.ShouldContain(f.SessionId.ToString());
        warning.ShouldContain("u2");
        warning.ShouldContain("ToolCall");
        warning.ShouldContain("22001");
        warning.ShouldContain(" 2");
        warning.ShouldNotContain("SECRET-INPUT-MARKER");
        warning.ShouldNotContain(new string('x', 201));
        f.Runtime.TryGetTranscriptPersistFailure(f.SessionId, out var mark).ShouldBeTrue();
        mark.Failures.ShouldBe(1);
        mark.Detail.ShouldBe("22001");
    }

    [Test]
    public async Task C561_a_row_that_fails_as_a_stub_is_skipped_and_the_result_reflects_landed_rows_only()
    {
        var fault = new RowFailure { Uuid = "u2" };
        await using var f = await PersistFixture.CreateAsync(fault);
        var result = await f.Runtime.PersistTranscriptAsync(f.SessionId, [
            TranscriptEvent(f.SessionId, 1, TranscriptKinds.UserPrompt, "u1", "hello"),
            TurnEndEvent(TranscriptKinds.StopReasons.EndTurn, f.SessionId, "u2") with { Sequence = 2 },
            TranscriptEvent(f.SessionId, 3, TranscriptKinds.AssistantText, "u3", "answer")
        ]);
        var rows = await f.RowsAsync();
        rows.Select(r => r.Uuid).ShouldBe(["u1", "u3"]);
        result.AddedTurnBoundary.ShouldBeFalse();
        result.AddedAssistantText.ShouldBeTrue();
        result.LastStoredSeq.ShouldBe(3);
        fault.Hits.ShouldBe(3);
        var warning = f.Logs.Where(l => l.StartsWith("[Warning]")).ShouldHaveSingleItem();
        warning.ShouldContain("u2");
        warning.ShouldContain("skipped");
        warning.ShouldContain("XX000");
        f.Runtime.TryGetTranscriptPersistFailure(f.SessionId, out var mark).ShouldBeTrue();
        mark.Failures.ShouldBe(1);
        mark.Detail.ShouldBe("XX000");
    }

    [Test]
    public async Task C561_a_unique_violation_from_a_concurrent_catch_up_is_skipped_without_a_stub_or_a_mark()
    {
        var fault = new RacingCatchUp { Uuid = "u2" };
        await using var f = await PersistFixture.CreateAsync(fault);
        var result = await f.Runtime.PersistTranscriptAsync(f.SessionId, [
            TranscriptEvent(f.SessionId, 1, TranscriptKinds.UserPrompt, "u1", "one"),
            TranscriptEvent(f.SessionId, 2, TranscriptKinds.UserPrompt, "u2", "ours"),
            TranscriptEvent(f.SessionId, 3, TranscriptKinds.AssistantText, "u3", "three")
        ]);
        var rows = await f.RowsAsync();
        rows.Count.ShouldBe(3);
        rows.Single(r => r.Uuid == "u2").Text.ShouldBe("racing copy");
        rows.Count(r => r.Uuid == "u2").ShouldBe(1);
        fault.SightingsOf("u2").ShouldBe(2);
        f.Runtime.TryGetTranscriptPersistFailure(f.SessionId, out _).ShouldBeFalse();
        f.Logs.ShouldNotContain(l => l.StartsWith("[Warning]"));
        f.Logs.ShouldContain(l => l.StartsWith("[Debug]") && l.Contains("u2") && l.Contains("23505"));
        result.LastStoredSeq.ShouldBe(3);
    }

    [Test]
    public async Task C561_a_transient_failure_on_the_stub_stops_the_loop_and_a_later_delivery_recovers()
    {
        var fault = new RowFailure { Uuid = "u2", TransientOnStub = true };
        await using var f = await PersistFixture.CreateAsync(fault);
        var batch = new[]
        {
            TranscriptEvent(f.SessionId, 1, TranscriptKinds.UserPrompt, "u1", "hello"),
            TurnEndEvent(TranscriptKinds.StopReasons.EndTurn, f.SessionId, "u2") with { Sequence = 2 },
            TranscriptEvent(f.SessionId, 3, TranscriptKinds.AssistantText, "u3", "answer")
        };
        var first = await f.Runtime.PersistTranscriptAsync(f.SessionId, batch);
        first.LastStoredSeq.ShouldBe(1);
        first.AddedTurnBoundary.ShouldBeFalse();
        first.AddedAssistantText.ShouldBeFalse();
        (await f.RowsAsync()).Select(r => r.Uuid).ShouldBe(["u1"]);
        f.Runtime.TryGetTranscriptPersistFailure(f.SessionId, out var mark).ShouldBeTrue();
        mark.Failures.ShouldBe(1);
        mark.Detail.ShouldContain("SyntheticTransientDbException");
        f.Logs.ShouldNotContain(l => l.Contains("skipped") && l.Contains("u2"));
        fault.Armed = false;
        var second = await f.Runtime.PersistTranscriptAsync(f.SessionId, batch);
        second.LastStoredSeq.ShouldBe(3);
        second.AddedTurnBoundary.ShouldBeTrue();
        var rows = await f.RowsAsync();
        rows.Select(r => r.Uuid).ShouldBe(["u1", "u2", "u3"]);
        rows.Single(r => r.Uuid == "u2").Text.ShouldBeNull();
        f.Runtime.TryGetTranscriptPersistFailure(f.SessionId, out _).ShouldBeFalse();
    }

    [Test]
    public async Task C561_a_non_database_persist_failure_records_the_mark()
    {
        var fault = new NonDatabaseFailure();
        await using var f = await PersistFixture.CreateAsync(fault);
        var first = await f.Runtime.PersistTranscriptAsync(
            f.SessionId, [TranscriptEvent(f.SessionId, 1, TranscriptKinds.UserPrompt, "u1", "hello")]);
        first.LastStoredSeq.ShouldBeNull();
        f.Logs.ShouldContain(l => l.Contains("Failed to persist"));
        f.Runtime.TryGetTranscriptPersistFailure(f.SessionId, out var mark).ShouldBeTrue();
        mark.Failures.ShouldBe(1);
        mark.Detail.ShouldBe("InvalidOperationException");
        var second = await f.Runtime.PersistTranscriptAsync(
            f.SessionId, [TranscriptEvent(f.SessionId, 1, TranscriptKinds.UserPrompt, "u1", "hello")]);
        second.LastStoredSeq.ShouldBe(1);
        f.Runtime.TryGetTranscriptPersistFailure(f.SessionId, out _).ShouldBeFalse();
    }

    [Test]
    public async Task C561_disposing_the_session_removes_the_persist_failure_mark()
    {
        var fault = new NonDatabaseFailure();
        await using var f = await PersistFixture.CreateAsync(fault);
        await f.Runtime.PersistTranscriptAsync(
            f.SessionId, [TranscriptEvent(f.SessionId, 1, TranscriptKinds.UserPrompt, "u1", "hello")]);
        f.Runtime.TryGetTranscriptPersistFailure(f.SessionId, out _).ShouldBeTrue();
        await f.Runtime.DisposeSessionAsync(f.SessionId);
        f.Runtime.TryGetTranscriptPersistFailure(f.SessionId, out _).ShouldBeFalse();
    }

    [Test]
    public async Task C561_a_stubbed_manual_compaction_boundary_still_reports_the_boundary()
    {
        var fault = new RowFailure { Uuid = "u2", FailOnceThenStub = true };
        await using var f = await PersistFixture.CreateAsync(fault);
        var result = await f.Runtime.PersistTranscriptAsync(f.SessionId, [
            TranscriptEvent(f.SessionId, 1, TranscriptKinds.AssistantText, "u1", "before"),
            new SessionRunnerTranscriptEvent(
                f.SessionId, 2, TranscriptKinds.CompactBoundary, "u2", null, DateTimeOffset.UtcNow,
                "system", "Compacted (manual) summary…", null, null, null, null, null)
        ]);
        var rows = await f.RowsAsync();
        rows.Count.ShouldBe(2);
        rows[1].Text.ShouldBe("[transcript text not persistable: XX000]");
        result.AddedManualCompactBoundary.ShouldBeTrue();
        result.AddedTurnBoundary.ShouldBeFalse();
    }

    [Test]
    public async Task C561_a_redelivered_unpersistable_row_dedups_against_its_stub()
    {
        await using var f = await PersistFixture.CreateAsync();
        var poison = new SessionRunnerTranscriptEvent(
            f.SessionId, 2, TranscriptKinds.ToolCall, "u2", null, DateTimeOffset.UtcNow,
            "assistant", null, new string('x', 201), "SECRET-INPUT-MARKER {...}", null, null, null);
        await f.Runtime.PersistTranscriptAsync(f.SessionId, [
            TranscriptEvent(f.SessionId, 1, TranscriptKinds.UserPrompt, "u1", "hello"),
            poison,
            TurnEndEvent(TranscriptKinds.StopReasons.EndTurn, f.SessionId, "u3") with { Sequence = 3 }
        ]);
        await f.Runtime.ObserveTranscriptAsync(poison, default);
        await f.Runtime.ObserveTranscriptAsync(poison, default);
        (await f.Runtime.PersistTranscriptAsync(f.SessionId, [poison])).LastStoredSeq.ShouldBeNull();
        var rows = await f.RowsAsync();
        rows.Count(r => r.Uuid == "u2").ShouldBe(1);
        rows.Count.ShouldBe(3);
        f.Logs.Count(l => l.StartsWith("[Warning]")).ShouldBe(1);
        f.Runtime.TryGetTranscriptPersistFailure(f.SessionId, out var mark).ShouldBeTrue();
        mark.Failures.ShouldBe(1);
    }

    [Test]
    public async Task C561_sync_over_a_runner_snapshot_with_the_poison_line_persists_once_and_never_retries()
    {
        var sid = Guid.NewGuid();
        var snapshot = new SessionRunnerTranscriptDto(sid, [
            TranscriptEvent(sid, 1, TranscriptKinds.UserPrompt, "u1", "hi"),
            TranscriptEvent(sid, 2, TranscriptKinds.ToolResult, "u2", "No Instance(s) Available.\r\r\n\r\0\n\0"),
            TurnEndEvent(TranscriptKinds.StopReasons.EndTurn, sid, "u3") with { Sequence = 3 }
        ], 3);
        await using var f = await PersistFixture.CreateAsync(runner: new SnapshotRunnerClient(snapshot), sessionId: sid);
        await f.Runtime.SyncTranscriptAsync(sid, default);
        await f.Runtime.SyncTranscriptAsync(sid, default);
        var rows = await f.RowsAsync();
        rows.Count.ShouldBe(3);
        rows.Single(r => r.Uuid == "u2").Text.ShouldBe("No Instance(s) Available.\r\r\n\r\uFFFD\n\uFFFD");
        f.Logs.ShouldNotContain(l => l.Contains("Failed to persist"));
        f.Logs.ShouldNotContain(l => l.Contains("Transcript sync skipped"));
    }

    private sealed class PersistFixture(
        ServiceProvider provider,
        string logPath,
        Guid sessionId,
        AgentSessionRuntime runtime,
        MockEventBus eventBus,
        List<string> logs) : IAsyncDisposable
    {
        public Guid SessionId { get; } = sessionId;
        public AgentSessionRuntime Runtime { get; } = runtime;
        public MockEventBus EventBus { get; } = eventBus;
        public List<string> Logs { get; } = logs;

        public static async Task<PersistFixture> CreateAsync(
            IInterceptor? interceptor = null,
            ISessionRunnerClient? runner = null,
            Guid? sessionId = null)
        {
            var sid = sessionId ?? Guid.NewGuid();
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sid,
                    DefinitionName = "claude",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running,
                    Cwd = Path.GetTempPath(),
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = DateTime.UtcNow,
                    StartedAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var logs = new List<string>();
            var eventBus = new MockEventBus();
            var logPath = Path.Combine(Path.GetTempPath(), $"antiphon-c561-{Guid.NewGuid():N}");
            var services = new ServiceCollection();
            services.AddSingleton<ILogger<AgentSessionRuntime>>(new ListLogger<AgentSessionRuntime>(logs));
            services.AddDbContext<AppDbContext>(o =>
            {
                o.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                });
                if (interceptor is not null)
                    o.AddInterceptors(interceptor);
            });
            var provider = services.BuildServiceProvider();
            var runtime = new AgentSessionRuntime(
                runner ?? new StaticSessionRunnerClient(new SessionRunnerBufferDto(sid, "", 0)),
                eventBus,
                Options.Create(new AgentSessionSettings { SessionLogPath = logPath }),
                provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                provider.GetRequiredService<ILogger<AgentSessionRuntime>>());
            return new PersistFixture(provider, logPath, sid, runtime, eventBus, logs);
        }

        public async Task<List<TranscriptEntry>> RowsAsync()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            return await db.TranscriptEntries
                .Where(t => t.AgentSessionId == SessionId)
                .OrderBy(t => t.Sequence)
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeSessionAsync(SessionId);
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
            await provider.DisposeAsync();
            DeleteDirectoryBestEffort(logPath);
        }
    }

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (sink)
                sink.Add($"[{logLevel}] {formatter(state, exception)}");
        }
    }

    private sealed class SnapshotRunnerClient(SessionRunnerTranscriptDto transcript) : ISessionRunnerClient
    {
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, string.Empty, 0));
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(transcript.SessionId == sessionId
                ? transcript
                : new SessionRunnerTranscriptDto(sessionId, [], 0));
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class SyntheticTransientDbException : DbException
    {
        public override bool IsTransient => true;
    }

    private sealed class RowFailure : SaveChangesInterceptor
    {
        public required string Uuid { get; init; }
        public bool TransientOnStub { get; init; }
        public bool FailOnceThenStub { get; init; }
        public bool Armed { get; set; } = true;
        public int Hits { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed)
                return ValueTask.FromResult(result);
            var tracked = eventData.Context!.ChangeTracker.Entries<TranscriptEntry>()
                .Where(e => e.State == EntityState.Added && e.Entity.Uuid == Uuid)
                .Select(e => e.Entity)
                .FirstOrDefault();
            if (tracked is null)
                return ValueTask.FromResult(result);
            Hits++;
            var isStub = (tracked.Text ?? "").StartsWith(AgentSessionRuntime.TranscriptStubPrefix, StringComparison.Ordinal);
            if (isStub && TransientOnStub)
                throw new DbUpdateException("transient stub", new SyntheticTransientDbException());
            if (isStub && FailOnceThenStub)
                return ValueTask.FromResult(result);
            throw new DbUpdateException("row failure", new PostgresException("internal error", "ERROR", "ERROR", "XX000"));
        }
    }

    private sealed class RacingCatchUp : SaveChangesInterceptor
    {
        public required string Uuid { get; init; }
        private readonly Dictionary<string, int> _sightings = new(StringComparer.Ordinal);
        private bool _inserted;

        public int SightingsOf(string uuid) => _sightings.GetValueOrDefault(uuid);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var tracked = eventData.Context!.ChangeTracker.Entries<TranscriptEntry>()
                .Where(e => e.State == EntityState.Added && e.Entity.Uuid == Uuid)
                .Select(e => e.Entity)
                .FirstOrDefault();
            if (tracked is null)
                return result;
            _sightings[Uuid] = _sightings.GetValueOrDefault(Uuid) + 1;
            if (_inserted)
                return result;
            _inserted = true;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = tracked.AgentSessionId,
                Sequence = tracked.Sequence,
                Kind = tracked.Kind,
                Uuid = tracked.Uuid,
                Text = "racing copy",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
            return result;
        }
    }

    private sealed class NonDatabaseFailure : SaveChangesInterceptor
    {
        private bool _thrown;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_thrown)
                return ValueTask.FromResult(result);
            _thrown = true;
            throw new InvalidOperationException("synthetic persist failure");
        }
    }
}
