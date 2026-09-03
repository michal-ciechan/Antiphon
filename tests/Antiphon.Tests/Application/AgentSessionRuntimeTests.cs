using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class AgentSessionRuntimeTests
{
    [Test]
    public async Task SignalR_AgentTextDelta_routes_to_session_group_only_and_chunks_output()
    {
        var eventBus = new MockEventBus();
        var logPath = Path.Combine(Path.GetTempPath(), $"antiphon-runtime-tests-{Guid.NewGuid():N}");
        await using var provider = BuildProvider();
        var runtime = new AgentSessionRuntime(
            eventBus,
            Options.Create(new AgentSessionSettings
            {
                SignalRMaxChunkChars = 4,
                ReplayBufferMaxChars = 8,
                SessionLogPath = logPath
            }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        var otherSessionId = Guid.NewGuid();
        var adapter = new FakeAgentProtocolAdapter();
        runtime.Register(sessionId, adapter);
        runtime.Register(otherSessionId, new FakeAgentProtocolAdapter());

        adapter.Emit("ABCDEFGHIJ");

        await WaitUntilAsync(() => eventBus.PublishedEvents.Count >= 3);

        var events = eventBus.PublishedEvents;
        events.Count.ShouldBe(3);
        events.Select(e => e.Group).ShouldAllBe(g => g == AgentSessionGroups.Session(sessionId));
        events.Select(e => e.EventName).ShouldAllBe(e => e == "AgentTextDelta");
        runtime.GetBufferSnapshot(sessionId).Buffer.ShouldBe("ABCDEFGHIJ");
        runtime.GetBufferSnapshot(sessionId).LastSequence.ShouldBe(1);
        GetPayloadValue<long>(events[0].Payload, "sequence").ShouldBe(1);
        GetPayloadValue<long>(events[1].Payload, "sequence").ShouldBe(1);
        GetPayloadValue<long>(events[2].Payload, "sequence").ShouldBe(1);
        GetPayloadValue<string>(events[0].Payload, "text").ShouldBe("ABCD");
        GetPayloadValue<string>(events[2].Payload, "text").ShouldBe("IJ");

        DeleteDirectoryBestEffort(logPath);
    }

    [Test]
    public async Task Buffer_snapshot_reads_from_session_runner_after_backend_runtime_restart()
    {
        var eventBus = new MockEventBus();
        var sessionId = Guid.NewGuid();
        var runnerClient = new StaticSessionRunnerClient(
            new SessionRunnerBufferDto(sessionId, "0123456789ABCDE", 7));

        await using (var provider = BuildProvider())
        {
            var runtime = new AgentSessionRuntime(
                runnerClient,
                eventBus,
                Options.Create(new AgentSessionSettings
                {
                    SignalRMaxChunkChars = 16,
                    ReplayBufferMaxChars = 10
                }),
                provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                NullLogger<AgentSessionRuntime>.Instance);

            runtime.GetBufferSnapshot(sessionId).Buffer.ShouldBe("0123456789ABCDE");
        }

        await using (var provider = BuildProvider())
        {
            var restartedRuntime = new AgentSessionRuntime(
                runnerClient,
                eventBus,
                Options.Create(new AgentSessionSettings
                {
                    SignalRMaxChunkChars = 16,
                    ReplayBufferMaxChars = 10
                }),
                provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                NullLogger<AgentSessionRuntime>.Instance);

            restartedRuntime.GetBufferSnapshot(sessionId).Buffer.ShouldBe("0123456789ABCDE");
            restartedRuntime.GetBufferSnapshot(sessionId).LastSequence.ShouldBe(7);
        }
    }

    [Test]
    [NotInParallel("Pty")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Backend_runtime_can_send_input_to_live_runner_session_after_restart()
    {
        var eventBus = new MockEventBus();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-runtime-runner-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        await using var runnerClient = new DirectSessionRunnerClient(Path.Combine(tempRoot, "session-runner-logs"));
        try
        {
            var sessionId = Guid.NewGuid();
            var launchSpec = new AgentLaunchSpec(
                "raw-cmd",
                AgentKind.Raw,
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/q", "/k", "@echo off & prompt $G"],
                new Dictionary<string, string>(),
                tempRoot,
                120,
                30,
                SessionId: sessionId);
            await runnerClient.StartAsync(sessionId, launchSpec, CancellationToken.None);

            await using (var provider = BuildProvider())
            {
                var firstRuntime = new AgentSessionRuntime(
                    runnerClient,
                    eventBus,
                    Options.Create(new AgentSessionSettings()),
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    TimeProvider.System,
                    NullLogger<AgentSessionRuntime>.Instance);
                firstRuntime.ListLiveSessions().ShouldContain(sessionId);
            }

            await using (var provider = BuildProvider())
            {
                var restartedRuntime = new AgentSessionRuntime(
                    runnerClient,
                    eventBus,
                    Options.Create(new AgentSessionSettings { FirstDeltaTimeoutMs = 100 }),
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    TimeProvider.System,
                    NullLogger<AgentSessionRuntime>.Instance);

                restartedRuntime.ListLiveSessions().ShouldContain(sessionId);
                await restartedRuntime.SendInputAsync(sessionId, "echo AFTER_BACKEND_RESTART\r", CancellationToken.None);

                await WaitUntilAsync(() =>
                    restartedRuntime.GetBufferSnapshot(sessionId).Buffer.Contains(
                        "AFTER_BACKEND_RESTART",
                        StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // The 2026-07-23 relaunch bug: the runner tailer numbers entries per tailer LIFETIME, so after
    // a session relaunch (same session id, fresh tailer, forked transcript) the new generation
    // re-issues low sequences. Sequence-keyed dedup dropped every new entry — reply routing went
    // silent. Dedup must key on the transcript line uuid, and stored sequences must be rebased to
    // stay session-monotonic so "latest turn" queries keep working.
    [Test]
    public async Task Transcript_entries_from_a_new_tailer_generation_survive_a_sequence_restart()
    {
        var sessionId = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            db.AgentSessions.Add(new Antiphon.Server.Domain.Entities.AgentSession
            {
                Id = sessionId,
                DefinitionName = "claude",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var logPath = Path.Combine(Path.GetTempPath(), $"antiphon-runtime-tests-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        await using var provider = services.BuildServiceProvider();
        var runtime = new AgentSessionRuntime(
            new MockEventBus(),
            Options.Create(new AgentSessionSettings { SessionLogPath = logPath }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);

        try
        {
            // Generation 1 (original tailer).
            await runtime.ObserveTranscriptAsync(TranscriptEvent(sessionId, 1, "UserPrompt", "uuid-g1-a", "hello"), CancellationToken.None);
            await runtime.ObserveTranscriptAsync(TranscriptEvent(sessionId, 2, "AssistantText", "uuid-g1-b", "hi"), CancellationToken.None);

            // Generation 2 (relaunch): numbering restarts at 1, but the line is genuinely new.
            await runtime.ObserveTranscriptAsync(TranscriptEvent(sessionId, 1, "UserPrompt", "uuid-g2-a", "after relaunch"), CancellationToken.None);

            // Replayed history (same uuid, re-numbered) must dedup, not duplicate.
            await runtime.ObserveTranscriptAsync(TranscriptEvent(sessionId, 7, "UserPrompt", "uuid-g1-a", "hello"), CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var rows = await verify.TranscriptEntries
                .Where(t => t.AgentSessionId == sessionId)
                .OrderBy(t => t.Sequence)
                .ToListAsync();

            rows.Select(r => r.Text).ShouldBe(["hello", "hi", "after relaunch"]);
            rows.Select(r => r.Sequence).ShouldBe([1L, 2L, 3L], "the new generation's entry must be rebased past the session max");
        }
        finally
        {
            await using var cleanup = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await cleanup.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync();
            await cleanup.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            DeleteDirectoryBestEffort(logPath);
        }
    }

    [Test]
    public async Task a_clean_process_exit_records_ProcessExit_when_no_prior_source()
    {
        var (sessionId, agentId, logPath, runtime) = await SeedRunningSessionAsync();
        try
        {
            await runtime.ObserveExitAsync(sessionId, 0, AgentExitReason.ProcessExited, CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.Status.ShouldBe(SessionStatus.Stopped);
            session.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
        }
        finally
        {
            await CleanupSessionAsync(sessionId, agentId);
            DeleteDirectoryBestEffort(logPath);
        }
    }

    [Test]
    public async Task an_exit_event_does_not_overwrite_an_OperatorRequest_source()
    {
        var (sessionId, agentId, logPath, runtime) = await SeedRunningSessionAsync(
            SessionTerminationSource.OperatorRequest);
        try
        {
            await runtime.ObserveExitAsync(sessionId, 0, AgentExitReason.ProcessExited, CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.Status.ShouldBe(SessionStatus.Stopped);
            session.TerminationSource.ShouldBe(SessionTerminationSource.OperatorRequest);
        }
        finally
        {
            await CleanupSessionAsync(sessionId, agentId);
            DeleteDirectoryBestEffort(logPath);
        }
    }

    [Test]
    public async Task an_exit_event_backfills_ProcessExit_onto_an_already_closed_row_with_no_source()
    {
        var (sessionId, agentId, logPath, runtime) = await SeedRunningSessionAsync(
            status: SessionStatus.Stopped);
        try
        {
            await runtime.ObserveExitAsync(sessionId, 0, AgentExitReason.ProcessExited, CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.Status.ShouldBe(SessionStatus.Stopped);
            session.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
            session.ExitCode.ShouldBe(0);
        }
        finally
        {
            await CleanupSessionAsync(sessionId, agentId);
            DeleteDirectoryBestEffort(logPath);
        }
    }

    [Test]
    public async Task a_cpu_spin_watchdog_exit_records_SystemRequest()
    {
        var (sessionId, agentId, logPath, runtime) = await SeedRunningSessionAsync();
        try
        {
            await runtime.ObserveExitAsync(sessionId, -1, AgentExitReason.CpuSpinKilled, CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.Status.ShouldBe(SessionStatus.Stopped);
            session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
        }
        finally
        {
            await CleanupSessionAsync(sessionId, agentId);
            DeleteDirectoryBestEffort(logPath);
        }
    }

    [Test]
    public async Task HerdrPaneClosed_exit_is_Failed_not_a_clean_stop()
    {
        var (sessionId, agentId, logPath, runtime) = await SeedRunningSessionAsync();
        try
        {
            await runtime.ObserveExitAsync(sessionId, null, AgentExitReason.HerdrPaneClosed, CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.Status.ShouldBe(SessionStatus.Failed, "pane-closed is not operator Stopped");
            session.FailureReason.ShouldNotBeNull();
            session.FailureReason.ShouldContain("HerdrPaneClosed");
            (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Failed);
        }
        finally
        {
            await CleanupSessionAsync(sessionId, agentId);
            DeleteDirectoryBestEffort(logPath);
        }
    }

    [Test]
    public async Task HerdrPaneLeftOpen_exit_is_Failed_and_records_the_warning_incident()
    {
        var (sessionId, agentId, logPath, runtime) = await SeedRunningSessionAsync();
        try
        {
            await runtime.ObserveExitAsync(sessionId, null, AgentExitReason.HerdrPaneLeftOpen, CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.Status.ShouldBe(SessionStatus.Failed);
            session.FailureReason.ShouldNotBeNull();
            session.FailureReason.ShouldContain("HerdrPaneLeftOpen");
            var incident = await verify.AgentIncidents.SingleAsync(
                i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.HerdrPaneLeftOpen);
            incident.Severity.ShouldBe(AlertSeverity.Warning);
            incident.AgentId.ShouldBe(agentId);
        }
        finally
        {
            await CleanupSessionAsync(sessionId, agentId);
            DeleteDirectoryBestEffort(logPath);
        }
    }

    private static async Task<(Guid SessionId, Guid AgentId, string LogPath, AgentSessionRuntime Runtime)> SeedRunningSessionAsync(
        SessionTerminationSource terminationSource = SessionTerminationSource.Unknown,
        SessionStatus status = SessionStatus.Running)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "claude",
                AgentKind = AgentKind.ClaudeCode,
                Status = status,
                TerminationSource = terminationSource,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = $"herdr-exit-{sessionId:N}"[..40],
                Slug = $"herdr-exit-{sessionId:N}",
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentStatus.Running,
                PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var logPath = Path.Combine(Path.GetTempPath(), $"antiphon-runtime-herdr-exit-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        var provider = services.BuildServiceProvider();
        var runtime = new AgentSessionRuntime(
            new MockEventBus(),
            Options.Create(new AgentSessionSettings { SessionLogPath = logPath }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
        return (sessionId, agentId, logPath, runtime);
    }

    private static async Task CleanupSessionAsync(Guid sessionId, Guid agentId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        await db.AgentIncidents.Where(i => i.SessionId == sessionId || i.AgentId == agentId).ExecuteDeleteAsync();
        await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync();
        await db.Agents.Where(a => a.Id == agentId).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
    }

    [Test]
    public void Error_stop_reason_is_an_idle_boundary_stop_sequence_is_not()
    {
        // CARD-0281: Grok's API-error TurnEnd is an idle boundary so dispatch runs on the
        // tailer poll. Claude's stubs are stop_sequence and must not gain this arm.
        AgentSessionRuntime.IsTurnBoundary(TurnEndEvent(TranscriptKinds.StopReasons.Error))
            .ShouldBeTrue();
        AgentSessionRuntime.IsTurnBoundary(TurnEndEvent(TranscriptKinds.StopReasons.EndTurn))
            .ShouldBeTrue();
        AgentSessionRuntime.IsTurnBoundary(TurnEndEvent(TranscriptKinds.StopReasons.Cancelled))
            .ShouldBeTrue();
        AgentSessionRuntime.IsTurnBoundary(TurnEndEvent("stop_sequence"))
            .ShouldBeFalse("Claude API-error stubs stay on the AssistantText arrival path");
    }

    [Test]
    public async Task Error_turn_end_is_acted_on_once_replay_dedups()
    {
        var sessionId = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            db.AgentSessions.Add(new Antiphon.Server.Domain.Entities.AgentSession
            {
                Id = sessionId,
                DefinitionName = "grok",
                AgentKind = AgentKind.Grok,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var logPath = Path.Combine(Path.GetTempPath(), $"antiphon-runtime-error-boundary-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        await using var provider = services.BuildServiceProvider();
        var runtime = new AgentSessionRuntime(
            new MockEventBus(),
            Options.Create(new AgentSessionSettings { SessionLogPath = logPath }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);

        try
        {
            var first = TurnEndEvent(TranscriptKinds.StopReasons.Error, sessionId, uuid: "error-uuid-1");
            await runtime.ObserveTranscriptAsync(first, CancellationToken.None);
            await runtime.ObserveTranscriptAsync(first with { Sequence = 99 }, CancellationToken.None);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var rows = await verify.TranscriptEntries
                .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd)
                .ToListAsync();
            rows.ShouldHaveSingleItem("replay of the same uuid must not persist a second TurnEnd");
            rows[0].StopReason.ShouldBe(TranscriptKinds.StopReasons.Error);
        }
        finally
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            DeleteDirectoryBestEffort(logPath);
        }
    }

    private static SessionRunnerTranscriptEvent TurnEndEvent(
        string stopReason, Guid? sessionId = null, string? uuid = null) =>
        new(
            sessionId ?? Guid.NewGuid(),
            Sequence: 1,
            Kind: TranscriptKinds.TurnEnd,
            Uuid: uuid ?? Guid.NewGuid().ToString("N"),
            ParentUuid: null,
            Timestamp: DateTimeOffset.UtcNow,
            Role: "assistant",
            Text: null,
            ToolName: null,
            ToolInput: null,
            ToolUseId: null,
            ToolIsError: null,
            StopReason: stopReason);

    private static SessionRunnerTranscriptEvent TranscriptEvent(
        Guid sessionId, long sequence, string kind, string uuid, string text) =>
        new(sessionId, sequence, kind, uuid, null, DateTimeOffset.UtcNow, "user", text, null, null, null, null, null);

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(25);
        }

        predicate().ShouldBeTrue();
    }

    private static T GetPayloadValue<T>(object payload, string propertyName)
    {
        var value = payload.GetType().GetProperty(propertyName)!.GetValue(payload);
        return value.ShouldBeOfType<T>();
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for PTY/session runner test directories.
        }
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        return services.BuildServiceProvider();
    }

    private sealed class StaticSessionRunnerClient : ISessionRunnerClient
    {
        private readonly SessionRunnerBufferDto _buffer;

        public StaticSessionRunnerClient(SessionRunnerBufferDto buffer)
        {
            _buffer = buffer;
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(_buffer);

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
