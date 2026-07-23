using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
