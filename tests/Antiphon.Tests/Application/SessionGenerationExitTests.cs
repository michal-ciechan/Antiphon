using System.Collections.Concurrent;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class SessionGenerationExitTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task C502_V1_queued_A_exit_released_after_B_resumed_changes_nothing()
    {
        await using var scenario = await ExitScenario.CreateAsync();
        await scenario.ResumeBAsync();
        scenario.Bus.Clear();
        await scenario.WriteExitAsync(scenario.GenerationA);
        await scenario.WaitForPumpAsync();

        await using var verify = scenario.Db();
        var row = await verify.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
        row.Status.ShouldBe(SessionStatus.Running);
        row.StartedAt.ShouldBe(scenario.GenerationB);
        row.TerminationSource.ShouldBe(SessionTerminationSource.Unknown);
        row.ExitCode.ShouldBeNull();
        row.EndedAt.ShouldBeNull();
        row.FailureReason.ShouldBeNull();
        row.RestartFailureKind.ShouldBeNull();
        var agent = await verify.Agents.SingleAsync(a => a.Id == scenario.AgentId);
        agent.Status.ShouldBe(AgentStatus.Running);
        agent.PersistentSessionId.ShouldBe(scenario.SessionId.ToString("D"));
        scenario.Bus.Published.ShouldNotContain(e => e.Event == "SessionExited");
        scenario.Bus.Published.ShouldNotContain(e => e.Event == "AgentChanged");
        (await verify.AgentIncidents.CountAsync(i => i.SessionId == scenario.SessionId)).ShouldBe(scenario.IncidentCountAtB);
        scenario.ReAdoptions.CountFor(scenario.SessionId).ShouldBe(0);
    }

    [Test]
    public async Task C502_V2_queued_A_exit_released_while_B_is_Starting_changes_nothing()
    {
        await using var scenario = await ExitScenario.CreateAsync();
        scenario.Adapter.ReadyHold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = scenario.ResumeBAsync();
        await WaitUntilAsync(() => scenario.Adapter.Started);
        await using (var mid = scenario.Db())
        {
            (await mid.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId)).Status
                .ShouldBe(SessionStatus.Starting);
        }

        await scenario.WriteExitAsync(scenario.GenerationA);
        await scenario.WaitForPumpAsync();
        await using (var blocked = scenario.Db())
        {
            var row = await blocked.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
            row.Status.ShouldBe(SessionStatus.Starting);
            row.StartedAt.ShouldBe(scenario.GenerationB);
        }

        scenario.Adapter.ReadyHold.SetResult(true);
        await resume;
        await using var verify = scenario.Db();
        var live = await verify.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
        live.Status.ShouldBe(SessionStatus.Running);
        live.StartedAt.ShouldBe(scenario.GenerationB);
    }

    [Test]
    public async Task C502_V3_matching_B_exit_closes_B_after_the_stale_A_exit_was_ignored()
    {
        await using var scenario = await ExitScenario.CreateAsync();
        await scenario.ResumeBAsync();
        scenario.Bus.Clear();
        await scenario.WriteExitAsync(scenario.GenerationA);
        await scenario.WaitForPumpAsync();
        await scenario.WriteExitAsync(scenario.GenerationB, exitCode: 1, reason: "KilledByRequest");
        await WaitUntilAsync(async () =>
        {
            await using var db = scenario.Db();
            return (await db.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId)).Status == SessionStatus.Failed;
        });

        await using var verify = scenario.Db();
        var row = await verify.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
        row.Status.ShouldBe(SessionStatus.Failed);
        row.FailureReason.ShouldStartWith("Process exited (KilledByRequest, code 1)");
        row.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
        row.EndedAt.ShouldNotBeNull();
        (await verify.Agents.SingleAsync(a => a.Id == scenario.AgentId)).Status.ShouldBe(AgentStatus.Failed);
        scenario.Bus.Published.Count(e => e.Event == "SessionExited").ShouldBe(1);
        scenario.Bus.Published.Count(e => e.Event == "AgentChanged").ShouldBe(1);
    }

    [Test]
    [Arguments("same-owner")]
    [Arguments("pointer-moved")]
    public async Task C502_V4_exit_consumer_blocked_on_the_row_lock_sees_B_after_commit(string shape)
    {
        var generationA = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-5));
        var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow);
        var (sessionId, agentId, logPath, runtime, _) = await AgentSessionRuntimeTestsSeed(generationA);
        var otherSessionId = Guid.NewGuid();
        try
        {
            if (shape == "pointer-moved")
            {
                await using var prep = new AppDbContext(TestDbFixture.CreateDbContextOptions());
                prep.AgentSessions.Add(new Antiphon.Server.Domain.Entities.AgentSession
                {
                    Id = otherSessionId,
                    DefinitionName = "claude",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running,
                    Cwd = Path.GetTempPath(),
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = generationB,
                    StartedAt = generationB,
                    LastSeenAt = generationB,
                });
                await prep.SaveChangesAsync();
            }

            await using var holder = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await using var tx = await holder.Database.BeginTransactionAsync();
            await holder.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT * FROM "AgentSessions" WHERE "Id" = {sessionId} FOR UPDATE""");

            var consume = runtime.ObserveExitAsync(
                new SessionRunnerExitedEvent(sessionId, 1, AgentExitReason.KilledByRequest, 0, generationA),
                CancellationToken.None);

            await WaitForLockAsync(sessionId, consume);

            if (shape == "pointer-moved")
            {
                await holder.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "Agents" SET "PersistentSessionId" = {otherSessionId.ToString("D")} WHERE "Id" = {agentId}""");
            }

            await holder.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "AgentSessions" SET "StartedAt" = {generationB}, "Status" = {SessionStatus.Starting}, "TerminationSource" = {SessionTerminationSource.Unknown}, "EndedAt" = NULL WHERE "Id" = {sessionId}""");
            await tx.CommitAsync();

            var disposition = await consume.WaitAsync(TimeSpan.FromSeconds(15));
            disposition.ShouldBe(SessionExitDisposition.Stale);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var row = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            row.Status.ShouldBe(SessionStatus.Starting);
            row.StartedAt.ShouldBe(generationB);
            var agent = await verify.Agents.SingleAsync(a => a.Id == agentId);
            agent.Status.ShouldBe(AgentStatus.Running);
        }
        finally
        {
            await using var cleanup = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await cleanup.AgentIncidents.Where(i => i.SessionId == sessionId || i.AgentId == agentId).ExecuteDeleteAsync();
            await cleanup.Agents.Where(a => a.Id == agentId).ExecuteDeleteAsync();
            await cleanup.AgentSessions.Where(s => s.Id == sessionId || s.Id == otherSessionId).ExecuteDeleteAsync();
            if (Directory.Exists(logPath))
                Directory.Delete(logPath, true);
        }
    }

    private static async Task<(Guid SessionId, Guid AgentId, string LogPath, AgentSessionRuntime Runtime, DateTime StartedAt)>
        AgentSessionRuntimeTestsSeed(DateTime generation)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
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
                CreatedAt = generation,
                StartedAt = generation,
                LastSeenAt = generation,
            });
            db.Agents.Add(new Antiphon.Server.Domain.Entities.Agent
            {
                Id = agentId,
                Name = $"c502-v4-{sessionId:N}"[..40],
                Slug = $"c502-v4-{sessionId:N}",
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentStatus.Running,
                PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = generation,
                UpdatedAt = generation,
            });
            await db.SaveChangesAsync();
        }

        var logPath = Path.Combine(Path.GetTempPath(), $"antiphon-c502-v4-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
        {
            npgsql.MigrationsAssembly("Antiphon.Server");
            npgsql.SetPostgresVersion(16, 0);
        }));
        var provider = services.BuildServiceProvider();
        var runtime = new AgentSessionRuntime(
            new MockEventBus(),
            Options.Create(new Antiphon.Server.Application.Settings.AgentSessionSettings { SessionLogPath = logPath }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
        return (sessionId, agentId, logPath, runtime, generation);
    }

    private static async Task WaitForLockAsync(Guid sessionId, Task<SessionExitDisposition> consume)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (consume.IsCompleted)
            {
                var result = consume.IsFaulted
                    ? consume.Exception!.GetBaseException().ToString()
                    : (await consume).ToString();
                throw new TimeoutException($"consumer finished before taking the row lock: {result}");
            }

            await using var conn = new NpgsqlConnection(TestDbFixture.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT 1
                FROM pg_stat_activity
                WHERE wait_event_type = 'Lock'
                  AND state = 'active'
                """;
            var found = await cmd.ExecuteScalarAsync();
            if (found is not null)
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"consumer did not wait on the AgentSessions row lock ({sessionId})");
    }

    [Test]
    public async Task C502_V5_matching_A_close_commits_first_and_B_resume_still_succeeds()
    {
        await using var scenario = await ExitScenario.CreateAsync(failed: false);
        var disposition = await scenario.Harness.Runtime.ObserveExitAsync(
            new SessionRunnerExitedEvent(scenario.SessionId, 1, AgentExitReason.KilledByRequest, 0, scenario.GenerationA),
            CancellationToken.None);
        disposition.ShouldBe(SessionExitDisposition.Applied);
        await using (var closed = scenario.Db())
        {
            var row = await closed.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
            row.Status.ShouldBe(SessionStatus.Failed);
            row.TerminationSource.ShouldBe(SessionTerminationSource.ProcessExit);
        }

        await scenario.ResumeBAsync();
        await using var verify = scenario.Db();
        var live = await verify.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
        live.Status.ShouldBe(SessionStatus.Running);
        live.StartedAt.ShouldBe(scenario.GenerationB);
        live.StartedAt.ShouldBeGreaterThan(scenario.GenerationA);
        live.TerminationSource.ShouldBe(SessionTerminationSource.Unknown);
        live.ExitCode.ShouldBeNull();
        live.FailureReason.ShouldBeNull();
    }

    [Test]
    public async Task C502_V8_A_exit_already_in_the_stream_behind_a_gated_event_is_consumed_after_B_and_ignored()
    {
        await using var scenario = await ExitScenario.CreateAsync();
        var other = Guid.NewGuid();
        await using (var db = scenario.Db())
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = other,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = scenario.Harness.TempRoot,
                Cols = 120,
                Rows = 30,
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var transcript = new RunnerTranscriptEvent(
            other, 1, TranscriptKinds.AssistantText, Guid.NewGuid().ToString("N"), null,
            DateTimeOffset.UtcNow, "assistant", "gate", null, null, null, null, null);
        scenario.Bus.Hold("SessionTranscript");
        await scenario.Handler.WriteSseAsync(
            SessionRunnerEventNames.SessionTranscript,
            JsonSerializer.Serialize(transcript, WebJson));
        await scenario.Bus.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scenario.WriteExitAsync(scenario.GenerationA);
        await scenario.ResumeBAsync();
        scenario.Bus.Clear();
        scenario.Bus.Release.TrySetResult();
        await scenario.WaitForPumpAsync();

        await using var verify = scenario.Db();
        var row = await verify.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
        row.Status.ShouldBe(SessionStatus.Running);
        row.StartedAt.ShouldBe(scenario.GenerationB);
        scenario.Bus.Published.ShouldNotContain(e => e.Event == "SessionExited");
    }

    [Test]
    public async Task C502_V9_severed_stream_then_a_snapshot_Exited_A_does_not_close_B()
    {
        await using var scenario = await ExitScenario.CreateAsync();
        await scenario.ResumeBAsync();
        await scenario.WriteExitAsync(scenario.GenerationA);
        scenario.Handler.Complete();
        await scenario.Pump.StopAsync(CancellationToken.None);

        scenario.Handler.ListedSessions =
        [
            new RunnerSessionDto(
                scenario.SessionId, 1, scenario.GenerationA, "Exited", 1, "KilledByRequest", 0,
                AcceptedStartedAt: scenario.GenerationA)
        ];
        await using (var scan = scenario.Db())
        {
            var service = SessionReconciliationServiceTests.BuildService(
                scan, scenario.Client, new MockEventBus(), reAdoptions: scenario.ReAdoptions);
            await service.ScanAsync(CancellationToken.None);
        }

        await using (var verify = scenario.Db())
        {
            var row = await verify.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId);
            row.Status.ShouldBe(SessionStatus.Running);
            row.StartedAt.ShouldBe(scenario.GenerationB);
        }

        scenario.Handler.ListedSessions =
        [
            new RunnerSessionDto(
                scenario.SessionId, 1, scenario.GenerationB, "Exited", 1, "KilledByRequest", 0,
                AcceptedStartedAt: scenario.GenerationB)
        ];
        await using (var scan = scenario.Db())
        {
            var service = SessionReconciliationServiceTests.BuildService(
                scan, scenario.Client, new MockEventBus(), reAdoptions: scenario.ReAdoptions);
            await service.ScanAsync(CancellationToken.None);
        }

        await using var closed = scenario.Db();
        (await closed.AgentSessions.SingleAsync(s => s.Id == scenario.SessionId)).Status
            .ShouldBe(SessionStatus.Failed);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        condition().ShouldBeTrue();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException("condition was not met");
    }

    private sealed class ExitScenario : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required BridgeQueueHarness Harness { get; init; }
        public required SseStreamHandler Handler { get; init; }
        public required SessionRunnerHttpClient Client { get; init; }
        public required FakeAgentProtocolAdapter Adapter { get; init; }
        public required RecordingBus Bus { get; init; }
        public required SessionReAdoptionState ReAdoptions { get; init; }
        public required SessionRunnerEventPump Pump { get; init; }
        public required PhaseLogger Trace { get; init; }
        public required string CapturedExitJson { get; init; }
        public required DateTime GenerationA { get; init; }
        public DateTime GenerationB { get; private set; }
        public Guid SessionId => Harness.SessionId;
        public Guid AgentId => Harness.AgentId;
        public int IncidentCountAtB { get; private set; }

        public static async Task<ExitScenario> CreateAsync(bool failed = true)
        {
            var captured = await CaptureKilledExitAsync();
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var handler = new SseStreamHandler();
            var client = handler.Client();
            var adapter = new FakeAgentProtocolAdapter();
            var bus = new RecordingBus();
            var reAdoptions = new SessionReAdoptionState();
            var factory = new OneAdapterFactory(adapter);
            var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                ConnectionString = schema.ConnectionString,
                AlwaysOn = true,
                ConfigureServices = services =>
                {
                    services.AddSingleton<ISessionRunnerClient>(client);
                    services.AddSingleton<IEventBus>(bus);
                    services.AddSingleton<IAgentProtocolAdapterFactory>(factory);
                    services.AddSingleton(reAdoptions);
                    services.AddSingleton(new SessionGenerationCompatState());
                },
            });
            adapter.RegisterOnStart = harness.Runtime;
            harness.Runtime.TryRemove(harness.SessionId, out _);
            var generationA = captured.Generation;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                await db.AgentSessions.Where(s => s.Id == harness.SessionId).ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.StartedAt, generationA)
                    .SetProperty(s => s.Status, failed ? SessionStatus.Failed : SessionStatus.Running)
                    .SetProperty(s => s.EndedAt, failed ? DateTime.UtcNow : (DateTime?)null)
                    .SetProperty(s => s.ExitCode, failed ? 1 : (int?)null)
                    .SetProperty(s => s.TerminationSource, failed ? SessionTerminationSource.ProcessExit : SessionTerminationSource.Unknown)
                    .SetProperty(s => s.FailureReason, failed ? "prior" : null)
                    .SetProperty(s => s.StandingAgentId, harness.AgentId)
                    .SetProperty(s => s.AgentKind, AgentKind.ClaudeCode));
                await db.Agents.Where(a => a.Id == harness.AgentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.Status, AgentStatus.Running)
                    .SetProperty(a => a.Kind, AgentKind.ClaudeCode)
                    .SetProperty(a => a.PersistentSessionId, harness.SessionId.ToString("D"))
                    .SetProperty(a => a.AlwaysOn, true));
            }

            var trace = new PhaseLogger();
            var pump = new SessionRunnerEventPump(
                harness.Provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new Antiphon.Server.Application.Settings.SessionRunnerSettings
                {
                    Enabled = true,
                    BaseUrl = "http://runner.test",
                    EventStreamIdleTimeoutSeconds = 120,
                    EventReconnectDelayMs = 50,
                }),
                trace);
            await pump.StartAsync(CancellationToken.None);
            await Task.Delay(200);
            return new ExitScenario
            {
                Schema = schema,
                Harness = harness,
                Handler = handler,
                Client = client,
                Adapter = adapter,
                Bus = bus,
                ReAdoptions = reAdoptions,
                Pump = pump,
                Trace = trace,
                CapturedExitJson = captured.Json,
                GenerationA = generationA,
            };
        }

        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

        public async Task ResumeBAsync()
        {
            await using var scope = Harness.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentControlService>()
                .StartAsync(AgentId, new StartAgentRequest(), CancellationToken.None);
            await Harness.Provider.GetRequiredService<AgentSessionLaunchQueue>()
                .WaitForIdleAsync(TimeSpan.FromSeconds(20), CancellationToken.None);
            await using var db = Db();
            GenerationB = (await db.AgentSessions.SingleAsync(s => s.Id == SessionId)).StartedAt;
            GenerationB.ShouldBeGreaterThan(GenerationA);
            IncidentCountAtB = await db.AgentIncidents.CountAsync(i => i.SessionId == SessionId);
        }

        public async Task WriteExitAsync(DateTime generation, int exitCode = 1, string? reason = null)
        {
            var captured = JsonSerializer.Deserialize<RunnerSessionExitedEvent>(CapturedExitJson, WebJson)!;
            var rewritten = captured with
            {
                SessionId = SessionId,
                AcceptedStartedAt = generation,
                ExitCode = exitCode,
                ExitReason = reason ?? captured.ExitReason,
            };
            rewritten.ExitReason.ShouldBe("KilledByRequest");
            rewritten.ExitCode.ShouldNotBe(0);
            rewritten.AcceptedStartedAt.ShouldBe(generation);
            await Handler.WriteSseAsync(
                SessionRunnerEventNames.SessionExited,
                JsonSerializer.Serialize(rewritten, WebJson));
        }

        public async Task WaitForPumpAsync()
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (Trace.Lines.Any(l => l.Contains("pump.event", StringComparison.Ordinal)
                                         && l.Contains(SessionId.ToString(), StringComparison.OrdinalIgnoreCase)))
                    return;
                await Task.Delay(50);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Pump.StopAsync(CancellationToken.None);
            Pump.Dispose();
            Handler.Complete();
            await Harness.DisposeAsync();
            await Schema.DisposeAsync();
        }
    }

    private static async Task<(string Json, DateTime Generation)> CaptureKilledExitAsync()
    {
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var logRoot = Path.Combine(Path.GetTempPath(), $"c502-exit-cap-{Guid.NewGuid():N}");
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(new Antiphon.SessionRunner.SessionRunnerSettings
            {
                SessionLogPath = logRoot,
                PtyHostLingerHours = 0.02,
                CpuWatchdogEnabled = false,
            }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var events = runtime.Subscribe(cts.Token);
        await runtime.StartAsync(new RunnerLaunchRequest(
            sessionId,
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/q", "/k", "@echo off & prompt $G"],
            new Dictionary<string, string>(),
            Path.GetTempPath(), 80, 24, AcceptedStartedAt: generation), cts.Token);
        await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), cts.Token);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            while (events.TryRead(out var evt))
            {
                if (evt.EventName != SessionRunnerEventNames.SessionExited)
                    continue;
                var payload = JsonSerializer.Deserialize<RunnerSessionExitedEvent>(evt.Json, WebJson);
                payload.ShouldNotBeNull();
                payload!.ExitReason.ShouldBe("KilledByRequest");
                payload.ExitCode.ShouldNotBe(0);
                payload.AcceptedStartedAt.ShouldBe(generation);
                return (evt.Json, generation);
            }

            await Task.Delay(50, cts.Token);
        }

        throw new TimeoutException("in-proc kill did not publish SessionExited");
    }

    private sealed class RecordingBus : IEventBus
    {
        public readonly List<(string? Group, string Event, object Payload)> Published = [];
        public string? HoldEvent { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Hold(string eventName) => HoldEvent = eventName;
        public void Clear()
        {
            lock (Published) Published.Clear();
        }

        public async Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default)
        {
            if (HoldEvent == eventName)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
            }

            lock (Published)
                Published.Add((group, eventName, payload));
        }

        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
        {
            lock (Published)
                Published.Add((null, eventName, payload));
            return Task.CompletedTask;
        }
    }

    private sealed class PhaseLogger : ILogger<SessionRunnerEventPump>
    {
        public ConcurrentQueue<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Enqueue(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }

    private sealed class OneAdapterFactory(FakeAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }
}
