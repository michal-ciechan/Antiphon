using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Pins the reconciliation backstop: DB sessions/agents must converge to runner truth even when
/// exit events were missed entirely (the "Running for a week on a dead PID" incident).
///
/// Globally serial (parameterless NotInParallel): ScanAsync sweeps EVERY live session/Working
/// agent in the shared test database, so running concurrently with other suites would flip their
/// in-flight agents. Assertions are row-scoped for the same reason — other tests' leftovers may
/// legitimately get corrected during our scan.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class SessionReconciliationServiceTests
{
    [Test]
    public async Task Session_unknown_to_runner_is_failed_and_its_agent_reset()
    {
        var marker = NewMarker();
        try
        {
            var (agent, session) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var eventBus = new MockEventBus();
            var service = BuildService(db, new FakeRunnerClient { Sessions = [] }, eventBus);

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == session);
            dbSession.Status.ShouldBe(SessionStatus.Failed);
            dbSession.FailureReason.ShouldNotBeNull();
            dbSession.FailureReason.ShouldContain("does not know this session");
            dbSession.EndedAt.ShouldNotBeNull();

            var dbAgent = await verify.Agents.SingleAsync(a => a.Id == agent);
            dbAgent.Status.ShouldBe(AgentStatus.Failed);

            eventBus.PublishedEvents.ShouldContain(e => e.EventName == "SessionExited");
            eventBus.PublishedEvents.ShouldContain(e => e.EventName == "AgentChanged");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Runner_reported_exit_is_mirrored_to_the_db_session()
    {
        var marker = NewMarker();
        try
        {
            var (_, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient
            {
                Sessions =
                [
                    new SessionRunnerSessionDto(
                        sessionId, Pid: 4242, StartedAt: DateTime.UtcNow.AddHours(-1),
                        Status: "Exited", ExitCode: 0, ExitReason: AgentExitReason.Unknown, LastSequence: 10)
                ]
            };
            var service = BuildService(db, runner, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var dbSession = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            dbSession.Status.ShouldBe(SessionStatus.Stopped); // exit code 0 → clean stop
            dbSession.ExitCode.ShouldBe(0);
            dbSession.EndedAt.ShouldNotBeNull();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Starting_session_within_grace_is_left_alone()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Starting, staleAgent: false);

            await using var db = CreateContext();
            var service = BuildService(db, new FakeRunnerClient { Sessions = [] }, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Starting);
            (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Working);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Working_agent_within_grace_is_left_alone_even_without_live_session()
    {
        var marker = NewMarker();
        try
        {
            // Agent flipped to Working just now, session already closed — e.g. the launch queue is
            // between "session row created" and "process running". Must not be touched yet.
            var agentId = await SeedAgentAsync(marker, AgentStatus.Working, sessionId: Guid.NewGuid(), updatedAt: DateTime.UtcNow);

            await using var db = CreateContext();
            var service = BuildService(db, new FakeRunnerClient { Sessions = [] }, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Working);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Unreachable_runner_skips_the_session_pass()
    {
        var marker = NewMarker();
        try
        {
            var (agentId, sessionId) = await SeedWorkingAgentWithSessionAsync(
                marker, SessionStatus.Running, staleAgent: true);

            await using var db = CreateContext();
            var runner = new FakeRunnerClient { ListError = new HttpRequestException("connection refused") };
            var service = BuildService(db, runner, new MockEventBus());

            await service.ScanAsync(CancellationToken.None);

            // Sessions untouched (runner may just be restarting) — and because the session is
            // still live in the DB, the agent stays Working too. No guessing while blind.
            await using var verify = CreateContext();
            (await verify.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
            (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Working);
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    // ---------- helpers ----------

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static SessionReconciliationService BuildService(
        AppDbContext db, ISessionRunnerClient runnerClient, MockEventBus eventBus) =>
        new(
            db,
            runnerClient,
            eventBus,
            new NoOpAlertService(),
            new RunnerReachabilityState(),
            Options.Create(new SessionReconciliationSettings
            {
                Enabled = true,
                StartingGraceMs = 90_000,
                AgentGraceMs = 120_000
            }),
            TimeProvider.System,
            NullLogger<SessionReconciliationService>.Instance);

    private sealed class NoOpAlertService : Antiphon.Server.Application.Interfaces.IAlertService
    {
        public Task RaiseAsync(
            Antiphon.Server.Application.Interfaces.AlertRaise alert, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private static string NewMarker() => $"antiphon-reconciliation-tests-{Guid.NewGuid():N}";

    private static async Task<(Guid AgentId, Guid SessionId)> SeedWorkingAgentWithSessionAsync(
        string marker, SessionStatus sessionStatus, bool staleAgent)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var startedAt = sessionStatus == SessionStatus.Starting ? now : now.AddHours(-1);

        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            CardId = null,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = sessionStatus,
            Cwd = Path.Combine(Path.GetTempPath(), marker),
            Cols = 120,
            Rows = 30,
            CreatedAt = startedAt,
            StartedAt = startedAt,
            LastSeenAt = startedAt
        });
        var agentId = Guid.NewGuid();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = marker,
            Slug = marker,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            Status = AgentStatus.Working,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now.AddHours(-2),
            UpdatedAt = staleAgent ? now.AddHours(-1) : now
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task<Guid> SeedAgentAsync(
        string marker, AgentStatus status, Guid sessionId, DateTime updatedAt)
    {
        var agentId = Guid.NewGuid();
        await using var db = CreateContext();
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = marker,
            Slug = marker,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            Status = status,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = updatedAt.AddHours(-2),
            UpdatedAt = updatedAt
        });
        await db.SaveChangesAsync();
        return agentId;
    }

    private static async Task CleanupAsync(string marker)
    {
        await using var db = CreateContext();
        await db.Agents.Where(a => a.Name == marker).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => s.Cwd.EndsWith(marker)).ExecuteDeleteAsync();
    }

    private sealed class FakeRunnerClient : ISessionRunnerClient
    {
        public IReadOnlyList<SessionRunnerSessionDto> Sessions { get; set; } = [];
        public Exception? ListError { get; set; }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            ListError is not null ? Task.FromException<IReadOnlyList<SessionRunnerSessionDto>>(ListError) : Task.FromResult(Sessions);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
