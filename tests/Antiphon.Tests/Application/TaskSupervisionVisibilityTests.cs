using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1610 S1. Janitor, attention and task detail.
/// A Failed task holds a seat only while it is the current proved-generation owner.
/// Created, Stopping, a missing generation, and a pending remote inventory stay.
/// A transcript with rows but no turn boundary stays. A transcript with no rows
/// remains eligible for TTL and cap retirement.
/// </summary>
[Category("Integration")]
[NotInParallel]
public sealed class TaskSupervisionVisibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    public const string SeatHeld = "Failed, session still Running: seat held";
    public const string ReleaseUnknown = "session state unknown: release unconfirmed";

    [Test]
    [Arguments("failed-fresh")]
    [Arguments("failed-unknown")]
    [Arguments("taskless-ttl")]
    [Arguments("taskless-cap")]
    [Arguments("real-idle")]
    [Arguments("restart-resolution")]
    [Arguments("failed-dedup")]
    [Arguments("source-custody")]
    [Arguments("reuse")]
    [Arguments("retain-created")]
    [Arguments("retain-stopping")]
    [Arguments("retain-no-generation")]
    [Arguments("retain-remote-pending")]
    [Arguments("absent-transcript")]
    public async Task C1610_Visibility_and_pool_safety(string arm)
    {
        switch (arm)
        {
            case "failed-fresh":
                await FailedFreshAsync();
                break;
            case "failed-unknown":
                await FailedUnknownAsync();
                break;
            case "taskless-ttl":
                await TasklessTtlAsync();
                break;
            case "taskless-cap":
                await TasklessCapAsync();
                break;
            case "real-idle":
                await RealIdleAsync();
                break;
            case "restart-resolution":
                await RestartResolutionAsync();
                break;
            case "failed-dedup":
                await FailedDedupAsync();
                break;
            case "source-custody":
                await SourceCustodyAsync();
                break;
            case "reuse":
                await ReuseAsync();
                break;
            case "retain-created":
                await RetainUnprovedAsync(SessionStatus.Created, startedAt: null, remotePending: false);
                break;
            case "retain-stopping":
                await RetainUnprovedAsync(SessionStatus.Stopping, startedAt: null, remotePending: false);
                break;
            case "retain-no-generation":
                await RetainUnprovedAsync(SessionStatus.Running, startedAt: DateTime.SpecifyKind(default, DateTimeKind.Utc), remotePending: false);
                break;
            case "retain-remote-pending":
                await RetainUnprovedAsync(SessionStatus.Running, startedAt: null, remotePending: true);
                break;
            case "absent-transcript":
                await AbsentTranscriptAsync();
                break;
            default:
                throw new InvalidOperationException(arm);
        }
    }

    private static async Task FailedFreshAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var (dispatcher, stopper, tasks, provider) = CreateHarness(clock, schema.ConnectionString);
        await using var owned = provider;
        using var workspace = new TempWorkspace();
        var (agent, session) = await SeedWarmAsync(schema.ConnectionString, workspace.Path, idleMinutes: 10, clock);
        await SeedTranscriptAsync(schema.ConnectionString, session, clock, working: false);
        var cardId = Guid.NewGuid();
        var task = await SeedFailedTaskAsync(schema.ConnectionString, agent, session, workspace.Path, cardId, clock);
        var seats = provider.GetRequiredService<SeatOccupancyState>();
        seats.Publish(new SeatOccupancySnapshot(Now.UtcDateTime,
        [
            Host("server2", inventory: "listed", seats:
            [
                Seat(session, agent, task.Id, cardId, SessionGeneration.Normalize(clock.GetUtcNow().UtcDateTime),
                    SeatClass.IdleTerminal, occupies: true),
            ]),
        ]));

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);
        var attention = await ReadAttentionAsync(schema.ConnectionString, clock, seats);
        var row = attention.Items.Where(i => i.Kind == AttentionKind.FailedTaskSessionHeld).ShouldHaveSingleItem();
        row.Title.ShouldBe(SeatHeld);
        row.Headline.ShouldBe("idle");
        row.TaskId.ShouldBe(task.Id);
        row.SessionId.ShouldBe(session);
        row.CardId.ShouldBe(cardId);
        row.Actions.ShouldNotContain(AttentionAction.KillSession);
        row.Evidence.ShouldContain("notice=none");
        row.Evidence.ShouldContain("ModelWaitDeadline");

        var detail = await tasks.GetAsync(task.Id, CancellationToken.None);
        detail.FailedSession.ShouldNotBeNull();
        detail.FailedSession.Summary.ShouldBe(SeatHeld);
        detail.FailedSession.Activity.ShouldBe("idle");
        detail.FailedSession.SessionId.ShouldBe(session);
        detail.FailedSession.NoticeState.ShouldBeNull();

        stopper.Killed.ShouldNotContain(session);
        await AssertSessionRunningAsync(schema.ConnectionString, session);
    }

    private static async Task FailedUnknownAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var (dispatcher, stopper, tasks, provider) = CreateHarness(clock, schema.ConnectionString);
        await using var owned = provider;
        using var workspace = new TempWorkspace();
        var (agent, session) = await SeedWarmAsync(schema.ConnectionString, workspace.Path, idleMinutes: 10, clock);
        await SeedTranscriptAsync(schema.ConnectionString, session, clock, working: false);
        var task = await SeedFailedTaskAsync(schema.ConnectionString, agent, session, workspace.Path, Guid.NewGuid(), clock);
        var seats = provider.GetRequiredService<SeatOccupancyState>();
        seats.Publish(new SeatOccupancySnapshot(Now.UtcDateTime,
        [
            Host("server2", inventory: "unavailable", reason: "list-failed"),
        ]));

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);
        var attention = await ReadAttentionAsync(schema.ConnectionString, clock, seats);
        var row = attention.Items.Where(i => i.Kind == AttentionKind.FailedTaskSessionHeld).ShouldHaveSingleItem();
        row.Title.ShouldBe(ReleaseUnknown);
        row.Headline.ShouldBe("unknown");
        row.SessionId.ShouldBe(session);
        row.Actions.ShouldNotContain(AttentionAction.KillSession);

        var detail = await tasks.GetAsync(task.Id, CancellationToken.None);
        detail.FailedSession.ShouldNotBeNull();
        detail.FailedSession.Summary.ShouldBe(ReleaseUnknown);
        detail.FailedSession.Activity.ShouldBe("unknown");

        stopper.Killed.ShouldNotContain(session);
        await AssertSessionRunningAsync(schema.ConnectionString, session);
    }

    private static async Task TasklessTtlAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var (dispatcher, stopper, _, provider) = CreateHarness(clock, schema.ConnectionString);
        await using var owned = provider;
        using var workspace = new TempWorkspace();
        var (agent, session) = await SeedWarmAsync(schema.ConnectionString, workspace.Path, idleMinutes: 10, clock);
        await SeedTranscriptAsync(schema.ConnectionString, session, clock, working: true);

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        await using var db = Open(schema.ConnectionString);
        var incident = await db.AgentIncidents.SingleAsync(i => i.Kind == AgentIncidentKind.PooledSessionWorkingPastTtl);
        incident.AgentId.ShouldBe(agent);
        incident.SessionId.ShouldBe(session);
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.Message.ShouldContain("never killed");

        var attention = await ReadAttentionAsync(schema.ConnectionString, clock, State());
        var row = attention.Items.Where(i => i.Kind == AttentionKind.PooledSessionWorkingPastTtl).ShouldHaveSingleItem();
        row.SessionId.ShouldBe(session);
        row.AgentId.ShouldBe(agent);
        row.Actions.ShouldNotContain(AttentionAction.KillSession);
        row.Evidence.ShouldContain("never killed");
        stopper.Killed.ShouldNotContain(session);
        await AssertSessionRunningAsync(schema.ConnectionString, session);
        (await db.Agents.AnyAsync(a => a.Id == agent)).ShouldBeTrue();
    }

    private static async Task TasklessCapAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var (dispatcher, stopper, _, provider) = CreateHarness(clock, schema.ConnectionString);
        await using var owned = provider;
        using var workspace = new TempWorkspace();
        var seeded = new List<(Guid Agent, Guid Session)>();
        for (var i = 0; i < 4; i++)
        {
            var row = await SeedWarmAsync(schema.ConnectionString, workspace.Path, idleMinutes: 10 + i, clock);
            await SeedTranscriptAsync(schema.ConnectionString, row.Session, clock, working: true);
            seeded.Add(row);
        }

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        foreach (var row in seeded)
            stopper.Killed.ShouldNotContain(row.Session);
        await using var db = Open(schema.ConnectionString);
        var ids = seeded.Select(s => s.Agent).ToList();
        (await db.Agents.CountAsync(a => ids.Contains(a.Id))).ShouldBe(4);
        (await db.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.PooledSessionWorkingPastTtl))
            .ShouldBeGreaterThan(0);
        foreach (var row in seeded)
            (await db.AgentSessions.SingleAsync(s => s.Id == row.Session)).Status.ShouldBe(SessionStatus.Running);
    }

    private static async Task RealIdleAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var (dispatcher, stopper, _, provider) = CreateHarness(clock, schema.ConnectionString);
        await using var owned = provider;
        using var workspace = new TempWorkspace();
        var (agent, session) = await SeedWarmAsync(schema.ConnectionString, workspace.Path, idleMinutes: 10, clock);
        // Positive idle proof: a turn that ended. An empty transcript is not this guard.
        await SeedTranscriptAsync(schema.ConnectionString, session, clock, working: false);

        var retired = await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        retired.ShouldBeGreaterThanOrEqualTo(1);
        stopper.Killed.ShouldContain(session);
        await using var db = Open(schema.ConnectionString);
        (await db.Agents.AnyAsync(a => a.Id == agent)).ShouldBeFalse();
    }

    /// <summary>
    /// A live session whose transcript has no turn-end cannot prove idle. A title row is
    /// not a turn boundary and is not Working, so the janitor must keep the seat.
    /// </summary>
    private static async Task AbsentTranscriptAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var (dispatcher, stopper, _, provider) = CreateHarness(clock, schema.ConnectionString);
        await using var owned = provider;
        using var workspace = new TempWorkspace();
        var (agent, session) = await SeedWarmAsync(schema.ConnectionString, workspace.Path, idleMinutes: 10, clock);
        await SeedTitleOnlyAsync(schema.ConnectionString, session, clock);

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        stopper.Killed.ShouldNotContain(session);
        await using var db = Open(schema.ConnectionString);
        (await db.AgentSessions.SingleAsync(s => s.Id == session)).Status.ShouldBe(SessionStatus.Running);
        (await db.Agents.AnyAsync(a => a.Id == agent)).ShouldBeTrue();
    }

    private static async Task RestartResolutionAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        using var workspace = new TempWorkspace();
        var (agent, session) = await SeedWarmAsync(schema.ConnectionString, workspace.Path, idleMinutes: 10, clock);
        await SeedTranscriptAsync(schema.ConnectionString, session, clock, working: true);

        var (first, _, _, firstProvider) = CreateHarness(clock, schema.ConnectionString);
        await using var ownedFirst = firstProvider;
        await first.RetireIdleWarmAgentsAsync(CancellationToken.None);

        var (second, _, _, secondProvider) = CreateHarness(clock, schema.ConnectionString);
        await using var ownedSecond = secondProvider;
        await second.RetireIdleWarmAgentsAsync(CancellationToken.None);

        await using (var db = Open(schema.ConnectionString))
        {
            (await db.AgentIncidents.CountAsync(i =>
                i.Kind == AgentIncidentKind.PooledSessionWorkingPastTtl && i.SessionId == session)).ShouldBe(1);
        }

        await using (var db = Open(schema.ConnectionString))
        {
            var stored = await db.AgentSessions.SingleAsync(s => s.Id == session);
            stored.Status = SessionStatus.Stopped;
            stored.EndedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }

        var cleared = await ReadAttentionAsync(schema.ConnectionString, clock, State());
        cleared.Items.ShouldNotContain(i => i.Kind == AttentionKind.PooledSessionWorkingPastTtl && i.SessionId == session);
        await using var verify = Open(schema.ConnectionString);
        (await verify.AgentIncidents.CountAsync(i =>
            i.Kind == AgentIncidentKind.PooledSessionWorkingPastTtl && i.AgentId == agent)).ShouldBe(1);
    }

    private static async Task FailedDedupAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var (dispatcher, stopper, _, provider) = CreateHarness(clock, schema.ConnectionString);
        await using var owned = provider;
        using var workspace = new TempWorkspace();
        var (agent, session) = await SeedWarmAsync(schema.ConnectionString, workspace.Path, idleMinutes: 40, clock);
        await SeedTranscriptAsync(schema.ConnectionString, session, clock, working: true);
        var cardId = Guid.NewGuid();
        var task = await SeedFailedTaskAsync(schema.ConnectionString, agent, session, workspace.Path, cardId, clock);
        var started = SessionGeneration.Normalize(clock.GetUtcNow().UtcDateTime);
        var seats = State(Host("server2", inventory: "listed", idleSeats: 1, seats:
        [
            Seat(session, agent, task.Id, cardId, started, SeatClass.IdleTerminal, occupies: true, orphan: true,
                idleSince: clock.GetUtcNow().UtcDateTime.AddMinutes(-40)),
        ]));

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);
        var attention = await ReadAttentionAsync(schema.ConnectionString, clock, seats);
        attention.Items.Where(i => i.Kind == AttentionKind.FailedTaskSessionHeld).ShouldHaveSingleItem();
        attention.Items.ShouldNotContain(i => i.Kind == AttentionKind.PooledSessionWorkingPastTtl);
        attention.Items.ShouldNotContain(i => i.Kind == AttentionKind.SeatIdle && i.SessionId == session);
        attention.Items.ShouldNotContain(i => i.Kind == AttentionKind.SlotOrphan && i.SessionId == session);
        stopper.Killed.ShouldNotContain(session);
        await using var db = Open(schema.ConnectionString);
        (await db.AgentIncidents.AnyAsync(i => i.Kind == AgentIncidentKind.PooledSessionWorkingPastTtl)).ShouldBeFalse();
    }

    /// <summary>
    /// Historical Failed, then a later Succeeded task on the same session, proved idle past TTL.
    /// The session must retire and the old failure must not keep a seat-held row.
    /// </summary>
    private static async Task ReuseAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var (dispatcher, stopper, tasks, provider) = CreateHarness(clock, schema.ConnectionString);
        await using var owned = provider;
        using var workspace = new TempWorkspace();
        var (agent, session) = await SeedWarmAsync(schema.ConnectionString, workspace.Path, idleMinutes: 10, clock);
        await SeedTranscriptAsync(schema.ConnectionString, session, clock, working: false);
        var cardId = Guid.NewGuid();
        var failed = await SeedFailedTaskAsync(schema.ConnectionString, agent, session, workspace.Path, cardId, clock);
        await SeedLaterSucceededTaskAsync(schema.ConnectionString, agent, session, workspace.Path, clock);

        var retired = await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        retired.ShouldBeGreaterThanOrEqualTo(1);
        stopper.Killed.ShouldContain(session);
        var attention = await ReadAttentionAsync(schema.ConnectionString, clock, State());
        attention.Items.ShouldNotContain(i => i.Kind == AttentionKind.FailedTaskSessionHeld);
        var detail = await tasks.GetAsync(failed.Id, CancellationToken.None);
        detail.FailedSession.ShouldBeNull();
        await using var db = Open(schema.ConnectionString);
        (await db.Agents.AnyAsync(a => a.Id == agent)).ShouldBeFalse();
    }

    /// <summary>
    /// Past TTL with a proved turn-end, so only the unproved-idle state keeps the session.
    /// A mutation that treats Created, Stopping, a missing generation, or a pending remote
    /// inventory as idle kills the session and fails this witness.
    /// </summary>
    private static async Task RetainUnprovedAsync(SessionStatus status, DateTime? startedAt, bool remotePending)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var (dispatcher, stopper, _, provider) = CreateHarness(clock, schema.ConnectionString, remotePending);
        await using var owned = provider;
        using var workspace = new TempWorkspace();
        var (agent, session) = await SeedWarmAsync(
            schema.ConnectionString, workspace.Path, idleMinutes: 10, clock,
            status: status, startedAt: startedAt, runnerId: remotePending ? "server2" : null);
        await SeedTranscriptAsync(schema.ConnectionString, session, clock, working: false);

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        stopper.Killed.ShouldNotContain(session);
        await using var db = Open(schema.ConnectionString);
        var stored = await db.AgentSessions.SingleAsync(s => s.Id == session);
        stored.Status.ShouldBe(status);
        (await db.Agents.AnyAsync(a => a.Id == agent)).ShouldBeTrue();
    }

    private static async Task SourceCustodyAsync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var (dispatcher, stopper, _, provider) = CreateHarness(clock, schema.ConnectionString);
        await using var owned = provider;
        using var workspace = new TempWorkspace();
        var marker = Path.Combine(workspace.Path, "worktrees", "card-task-c1610161");
        Directory.CreateDirectory(marker);
        var keep = Path.Combine(marker, "keep.txt");
        await File.WriteAllTextAsync(keep, "source custody");
        var (agent, session) = await SeedWarmAsync(schema.ConnectionString, marker, idleMinutes: 10, clock);
        await SeedSourcedTaskAsync(schema.ConnectionString, agent, session, marker, clock);

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        stopper.Killed.ShouldNotContain(session);
        Directory.Exists(marker).ShouldBeTrue();
        File.Exists(keep).ShouldBeTrue();
        await using var db = Open(schema.ConnectionString);
        (await db.Agents.AnyAsync(a => a.Id == agent)).ShouldBeTrue();
        (await db.AgentSessions.SingleAsync(s => s.Id == session)).Status.ShouldBe(SessionStatus.Running);
    }

    private static FakeTimeProvider Clock() => new(Now);

    private static async Task AssertSessionRunningAsync(string connectionString, Guid sessionId)
    {
        await using var db = Open(connectionString);
        (await db.AgentSessions.SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running);
    }

    private static async Task<(Guid Agent, Guid Session)> SeedWarmAsync(
        string connectionString, string directory, int idleMinutes, FakeTimeProvider clock,
        SessionStatus status = SessionStatus.Running, DateTime? startedAt = null, string? runnerId = null)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = clock.GetUtcNow().UtcDateTime;
        await using var db = Open(connectionString);
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = status,
            // CK_AgentSessions_RunnerBinding_AllOrNone: RunnerId, RunnerStoreId and RunnerCwd move together.
            RunnerId = runnerId,
            RunnerStoreId = runnerId is null ? null : Guid.NewGuid(),
            RunnerCwd = runnerId is null ? null : directory,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = startedAt ?? now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = $"task-{agentId:N}"[..13],
            Slug = $"task-{agentId:N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            PoolClaudeEffort = ClaudeLaunchArgs.Effort(AgentModelLevel.Medium),
            IsPoolDelegate = true,
            PoolIdleSince = now.AddMinutes(-idleMinutes),
            PersistentSessionId = sessionId.ToString("D"),
            LaunchEnvJson = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task SeedTranscriptAsync(
        string connectionString, Guid sessionId, FakeTimeProvider clock, bool working)
    {
        var at = clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
        await using var db = Open(connectionString);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = TranscriptKinds.UserPrompt,
            Text = "Keep working",
            CreatedAt = at,
        });
        if (!working)
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 2,
                Kind = TranscriptKinds.TurnEnd,
                StopReason = "end_turn",
                CreatedAt = at.AddMinutes(1),
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedTitleOnlyAsync(string connectionString, Guid sessionId, FakeTimeProvider clock)
    {
        var at = clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
        await using var db = Open(connectionString);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = TranscriptKinds.TurnTitle,
            Text = "untitled",
            CreatedAt = at,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<AgentTask> SeedFailedTaskAsync(
        string connectionString, Guid agentId, Guid sessionId, string directory, Guid cardId, FakeTimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "CARD-1610 failed live",
            Goal = "the failed live work",
            Role = AgentTaskRole.Review,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            AgentId = agentId,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Failed,
            CardId = cardId,
            FailureReason = "ModelWaitDeadline",
            Attempt = 1,
            CreatedAt = now.AddMinutes(-30),
            DispatchedAt = now.AddMinutes(-29),
            CompletedAt = now.AddMinutes(-1),
        };
        await using var db = Open(connectionString);
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        db.Projects.Add(new Project { Id = projectId, Name = "c1610", CreatedAt = now, UpdatedAt = now });
        db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "c1610", CreatedAt = now, UpdatedAt = now });
        db.BoardColumns.Add(new BoardColumn
        {
            Id = columnId,
            BoardId = boardId,
            Name = "Doing",
            StateKey = "doing",
            CardStatus = CardStatus.InProgress,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Cards.Add(new Card
        {
            Id = cardId,
            BoardId = boardId,
            BoardColumnId = columnId,
            Identifier = "C1610-" + cardId.ToString("N")[..8],
            Title = "CARD-1610",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task SeedLaterSucceededTaskAsync(
        string connectionString, Guid agentId, Guid sessionId, string directory, FakeTimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        await using var db = Open(connectionString);
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "CARD-1610 later success",
            Goal = "the reused session's later work",
            Role = AgentTaskRole.Review,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            AgentId = agentId,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Succeeded,
            Attempt = 1,
            Result = "done",
            CreatedAt = now.AddMinutes(-20),
            DispatchedAt = now.AddMinutes(-15),
            CompletedAt = now.AddMinutes(-5),
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSourcedTaskAsync(
        string connectionString, Guid agentId, Guid sessionId, string directory, FakeTimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var operation = Guid.NewGuid();
        await using var db = Open(connectionString);
        db.AgentTasks.Add(new AgentTask
        {
            Id = ownerId,
            RootTaskId = ownerId,
            Title = "CARD-1610 source owner",
            Goal = "source owner",
            Status = AgentTaskStatus.Succeeded,
            CreatedAt = now.AddMinutes(-40),
            CompletedAt = now.AddMinutes(-35),
        });
        db.AgentTaskLandings.Add(new AgentTaskLanding
        {
            Id = operation,
            TaskId = ownerId,
            Phase = LandPhase.Inspected,
            OriginalSourceSha = new string('a', 40),
            CreatedAt = now.AddMinutes(-40),
            UpdatedAt = now.AddMinutes(-35),
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "CARD-1610 sourced custody",
            Goal = "sourced custody",
            Role = AgentTaskRole.Mutation,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = directory,
            WorktreePath = directory,
            AgentId = agentId,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Failed,
            SourceLandingOperationId = operation,
            SourceLandingSha = new string('a', 40),
            CreatedAt = now.AddMinutes(-30),
            DispatchedAt = now.AddMinutes(-29),
            CompletedAt = now.AddMinutes(-1),
        });
        await db.SaveChangesAsync();
    }

    private static AppDbContext Open(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static (AgentTaskDispatcher Dispatcher, RecordingSessionStopper Stopper, AgentTaskService Tasks, ServiceProvider Provider)
        CreateHarness(FakeTimeProvider clock, string connectionString, bool remotePending = false)
    {
        var stopper = new RecordingSessionStopper { StopsSessionsIn = connectionString };
        var services = new ServiceCollection();
        if (remotePending)
        {
            var client = new FakeSessionRunnerClient();
            services.AddSingleton<ISessionRunnerClient>(client);
            services.AddSingleton<ISessionRunnerDirectory>(new PendingInventoryDirectory(client));
        }
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(Settings()));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c1610-wt"),
        });
        services.AddSingleton(new SeatOccupancyState());
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<AgentTaskReplyService>();
        services.AddScoped<AgentTaskDispatcher>();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return (
            scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>(),
            stopper,
            scope.ServiceProvider.GetRequiredService<AgentTaskService>(),
            provider);
    }

    private static DelegationSettings Settings() => new()
    {
        PoolReservedForCallerMinutes = 2,
        PoolIdleRetireMinutes = 5,
        PoolMaxIdlePerDirectory = 3,
        MaxConcurrentTasks = 512,
    };

    private static async Task<AttentionDto> ReadAttentionAsync(
        string connectionString, FakeTimeProvider clock, SeatOccupancyState? seats)
    {
        await using var db = Open(connectionString);
        return await new AttentionService(
            db,
            new AttentionServiceTests.FakeRunnerClient(),
            Options.Create(new SupervisionSettings()),
            Options.Create(Settings()),
            clock,
            NullLogger<AttentionService>.Instance,
            seats: seats,
            attention: Options.Create(new AttentionSettings { SeatWatchEnabled = true }))
            .GetAsync(CancellationToken.None, includeProgressProbe: false);
    }

    private static SeatOccupancyState State(params HostOccupancyObservation[] hosts)
    {
        var state = new SeatOccupancyState();
        state.Publish(new SeatOccupancySnapshot(Now.UtcDateTime, hosts));
        return state;
    }

    private static HostOccupancyObservation Host(
        string hostId,
        string inventory,
        string? reason = null,
        int idleSeats = 0,
        params SeatObservation[] seats) =>
        new(hostId, "runner", inventory, reason, 1, 0, 1, 0, 0, idleSeats, 0,
            seats.Count(s => s.Orphan && s.Occupies), 10, null, null, seats);

    private static SeatObservation Seat(
        Guid sessionId,
        Guid agentId,
        Guid taskId,
        Guid cardId,
        DateTime started,
        SeatClass seatClass,
        bool occupies,
        bool orphan = false,
        DateTime? idleSince = null) =>
        new("server2", sessionId, occupies ? "Running" : "Exited", 42, started, occupies, orphan,
            PooledWarm: true, "Running", taskId, AgentTaskStatus.Failed, 1, AgentTaskRole.Review, cardId, null,
            agentId, seatClass, idleSince ?? started, "unknown");

    /// <summary>Remote runner whose inventory has not answered, so its sessions are unproved.</summary>
    private sealed class PendingInventoryDirectory(ISessionRunnerClient client) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local { get; } = client;
        public IReadOnlyList<string> KnownRunnerIds => ["server2"];
        public ISessionRunnerClient Resolve(string? runnerId) => client;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("pending"));
        public Guid? GetLiveStoreId(string? runnerId) => null;
        public bool RemoteInventoryPending(string? runnerId) =>
            string.Equals(runnerId, "server2", StringComparison.Ordinal);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c1610-vis").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
