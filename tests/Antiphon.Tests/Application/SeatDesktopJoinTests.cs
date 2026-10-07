using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1079 V-6. One desktop join: latest task, open task, blocked clock, warmth, receipt.</summary>
[Category("Integration")]
public sealed class SeatDesktopJoinTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task C1079_Join_reports_latest_bound_task_blocked_at_pooled_warm_and_receipt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var blockedSession = Guid.NewGuid();
        var warmSession = Guid.NewGuid();
        var coldSession = Guid.NewGuid();
        var workingSession = Guid.NewGuid();
        var stoppedSession = Guid.NewGuid();
        var staleSession = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var olderTask = Guid.NewGuid();
        var newerTask = Guid.NewGuid();
        var workingTask = Guid.NewGuid();
        var staleTask = Guid.NewGuid();
        var blockedAgent = Guid.NewGuid();
        var warmAgent = Guid.NewGuid();
        var coldAgent = Guid.NewGuid();
        var workingAgent = Guid.NewGuid();
        var earlyBlock = Guid.NewGuid();
        var lateBlock = Guid.NewGuid();
        var decoyBlock = Guid.NewGuid();
        var completed = Now.AddHours(3);
        var lateAt = Now.AddMinutes(50);
        var decoyAt = Now.AddMinutes(90);

        await using (var db = new AppDbContext(options))
        {
            db.Projects.Add(new Project
            {
                Id = projectId,
                Name = "c1079-join",
                GitRepositoryUrl = "https://example.invalid/c1079.git",
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            db.Boards.Add(new Board
            {
                Id = boardId,
                ProjectId = projectId,
                Name = "c1079",
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            db.BoardColumns.Add(new BoardColumn
            {
                Id = columnId,
                BoardId = boardId,
                Name = "Backlog",
                StateKey = "backlog",
                CardStatus = CardStatus.Backlog,
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            db.Cards.Add(new Card
            {
                Id = cardId,
                BoardId = boardId,
                BoardColumnId = columnId,
                Identifier = "CARD-1079",
                Title = "seat",
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            db.AgentSessions.AddRange(
                Session(blockedSession, SessionStatus.Running),
                Session(warmSession, SessionStatus.Running),
                Session(coldSession, SessionStatus.Running),
                Session(workingSession, SessionStatus.Running),
                Session(stoppedSession, SessionStatus.Stopped),
                Session(staleSession, SessionStatus.Running));
            db.Agents.AddRange(
                Agent(blockedAgent, "c1079-blocked", blockedSession, warm: false),
                Agent(warmAgent, "c1079-warm", warmSession, warm: true),
                Agent(coldAgent, "c1079-cold", coldSession, warm: false),
                Agent(workingAgent, "c1079-working", workingSession, warm: false));
            db.AgentTasks.AddRange(
                TaskRow(olderTask, blockedSession, AgentTaskStatus.Succeeded, Now, attempt: 1, completedAt: Now.AddMinutes(5)),
                TaskRow(newerTask, blockedSession, AgentTaskStatus.Blocked, Now.AddMinutes(10), attempt: 4,
                    cardId: cardId, agentId: blockedAgent, completedAt: completed, role: AgentTaskRole.Code),
                TaskRow(workingTask, workingSession, AgentTaskStatus.Working, Now.AddMinutes(10), attempt: 2,
                    cardId: cardId, agentId: workingAgent, role: AgentTaskRole.Review),
                TaskRow(staleTask, staleSession, AgentTaskStatus.Blocked, Now.AddMinutes(10), attempt: 2,
                    cardId: cardId, agentId: blockedAgent, role: AgentTaskRole.Debug));
            db.AgentTaskEvents.AddRange(
                Event(earlyBlock, newerTask, blockedSession, Now.AddMinutes(20)),
                Event(lateBlock, newerTask, blockedSession, lateAt),
                Event(decoyBlock, olderTask, blockedSession, decoyAt));
            db.AgentTaskParks.AddRange(
                Park(newerTask, attempt: 4, blockEventId: lateBlock, receipt: Guid.NewGuid(), at: lateAt),
                Park(newerTask, attempt: 1, blockEventId: earlyBlock, receipt: null, at: Now.AddMinutes(20)),
                Park(staleTask, attempt: 1, blockEventId: Guid.NewGuid(), receipt: Guid.NewGuid(), at: Now),
                Park(staleTask, attempt: 2, blockEventId: Guid.NewGuid(), receipt: null, at: Now));
            await db.SaveChangesAsync();
        }

        await using var read = new AppDbContext(options);
        (await SeatDesktopJoin.LoadAsync(read, [], CancellationToken.None)).Count.ShouldBe(0);
        var rows = await SeatDesktopJoin.LoadAsync(
            read,
            [blockedSession, warmSession, coldSession, workingSession, stoppedSession, staleSession, missing],
            CancellationToken.None);

        var blocked = rows[blockedSession];
        blocked.Live.ShouldBeTrue();
        blocked.Status.ShouldBe("Running");
        blocked.OpenTaskId.ShouldBe(newerTask);
        blocked.PooledWarm.ShouldBeFalse();
        blocked.PublicationReceipt.ShouldBeTrue();
        blocked.BlockedAt.ShouldBe(lateAt);
        blocked.LatestTask.ShouldNotBeNull();
        blocked.LatestTask.Id.ShouldBe(newerTask);
        blocked.LatestTask.Status.ShouldBe(AgentTaskStatus.Blocked);
        blocked.LatestTask.Attempt.ShouldBe(4);
        blocked.LatestTask.Role.ShouldBe(AgentTaskRole.Code);
        blocked.LatestTask.CardId.ShouldBe(cardId);
        blocked.LatestTask.BoardId.ShouldBe(boardId);
        blocked.LatestTask.AgentId.ShouldBe(blockedAgent);
        blocked.LatestTask.CompletedAt.ShouldBe(completed);

        var warm = rows[warmSession];
        warm.PooledWarm.ShouldBeTrue();
        warm.Live.ShouldBeTrue();
        warm.OpenTaskId.ShouldBeNull();
        warm.LatestTask.ShouldBeNull();
        warm.PublicationReceipt.ShouldBeFalse();

        rows[coldSession].PooledWarm.ShouldBeFalse();
        rows[coldSession].Live.ShouldBeTrue();

        var working = rows[workingSession];
        working.OpenTaskId.ShouldBe(workingTask);
        working.Live.ShouldBeTrue();
        working.PooledWarm.ShouldBeFalse();
        working.PublicationReceipt.ShouldBeFalse();
        working.BlockedAt.ShouldBeNull();
        working.LatestTask.ShouldNotBeNull();
        working.LatestTask.Id.ShouldBe(workingTask);
        working.LatestTask.Status.ShouldBe(AgentTaskStatus.Working);

        var stopped = rows[stoppedSession];
        stopped.Live.ShouldBeFalse();
        stopped.Status.ShouldBe("Stopped");

        var stale = rows[staleSession];
        stale.PublicationReceipt.ShouldBeFalse();
        stale.OpenTaskId.ShouldBe(staleTask);
        stale.LatestTask.ShouldNotBeNull();
        stale.LatestTask.Id.ShouldBe(staleTask);

        var absent = rows[missing];
        absent.Live.ShouldBeFalse();
        absent.Status.ShouldBeNull();
        absent.OpenTaskId.ShouldBeNull();
        absent.LatestTask.ShouldBeNull();
        absent.PooledWarm.ShouldBeFalse();
    }

    [Test]
    public async Task C1124_Join_owner_task_is_queued_dispatched_working_or_blocked()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        AgentTaskStatus[] owners =
        [
            AgentTaskStatus.Queued,
            AgentTaskStatus.Dispatched,
            AgentTaskStatus.Working,
            AgentTaskStatus.Blocked,
        ];
        AgentTaskStatus[] settled =
        [
            AgentTaskStatus.Succeeded,
            AgentTaskStatus.Failed,
            AgentTaskStatus.Canceled,
        ];
        var ids = new Dictionary<AgentTaskStatus, (Guid Session, Guid Task)>();
        var mixedWorkingSession = Guid.NewGuid();
        var mixedWorkingTask = Guid.NewGuid();
        var mixedWorkingSucceeded = Guid.NewGuid();
        var mixedBlockedSession = Guid.NewGuid();
        var mixedBlockedTask = Guid.NewGuid();
        var mixedBlockedSucceeded = Guid.NewGuid();
        var emptySession = Guid.NewGuid();
        var stoppedSession = Guid.NewGuid();
        var stoppedTask = Guid.NewGuid();

        await using (var db = new AppDbContext(options))
        {
            foreach (var status in owners.Concat(settled))
            {
                var session = Guid.NewGuid();
                var task = Guid.NewGuid();
                ids[status] = (session, task);
                db.AgentSessions.Add(Session(session, SessionStatus.Running));
                db.AgentTasks.Add(TaskRow(task, session, status, Now));
            }

            db.AgentSessions.AddRange(
                Session(mixedWorkingSession, SessionStatus.Running),
                Session(mixedBlockedSession, SessionStatus.Running),
                Session(emptySession, SessionStatus.Running),
                Session(stoppedSession, SessionStatus.Stopped));
            db.AgentTasks.AddRange(
                TaskRow(mixedWorkingTask, mixedWorkingSession, AgentTaskStatus.Working, Now),
                TaskRow(mixedWorkingSucceeded, mixedWorkingSession, AgentTaskStatus.Succeeded, Now.AddMinutes(5),
                    completedAt: Now.AddMinutes(6)),
                TaskRow(mixedBlockedTask, mixedBlockedSession, AgentTaskStatus.Blocked, Now),
                TaskRow(mixedBlockedSucceeded, mixedBlockedSession, AgentTaskStatus.Succeeded, Now.AddMinutes(5),
                    completedAt: Now.AddMinutes(6)),
                TaskRow(stoppedTask, stoppedSession, AgentTaskStatus.Blocked, Now));
            await db.SaveChangesAsync();
        }

        await using var read = new AppDbContext(options);
        var wanted = ids.Values.Select(pair => pair.Session)
            .Append(mixedWorkingSession)
            .Append(mixedBlockedSession)
            .Append(emptySession)
            .Append(stoppedSession)
            .ToArray();
        var rows = await SeatDesktopJoin.LoadAsync(read, wanted, CancellationToken.None);

        foreach (var status in owners)
        {
            var (session, task) = ids[status];
            rows[session].OpenTaskId.ShouldBe(task, status.ToString());
            rows[session].LatestTask.ShouldNotBeNull();
            rows[session].LatestTask.Id.ShouldBe(task);
        }

        foreach (var status in settled)
        {
            var (session, task) = ids[status];
            rows[session].OpenTaskId.ShouldBeNull(status.ToString());
            rows[session].LatestTask.ShouldNotBeNull();
            rows[session].LatestTask.Id.ShouldBe(task);
            rows[session].LatestTask.Status.ShouldBe(status);
        }

        rows[mixedWorkingSession].OpenTaskId.ShouldBe(mixedWorkingTask);
        rows[mixedWorkingSession].LatestTask.ShouldNotBeNull();
        rows[mixedWorkingSession].LatestTask.Id.ShouldBe(mixedWorkingSucceeded);
        rows[mixedBlockedSession].OpenTaskId.ShouldBe(mixedBlockedTask);
        rows[mixedBlockedSession].LatestTask.ShouldNotBeNull();
        rows[mixedBlockedSession].LatestTask.Id.ShouldBe(mixedBlockedSucceeded);
        rows[emptySession].OpenTaskId.ShouldBeNull();
        rows[emptySession].LatestTask.ShouldBeNull();
        rows[stoppedSession].Live.ShouldBeFalse();
        rows[stoppedSession].OpenTaskId.ShouldBe(stoppedTask);
    }

    [Test]
    public async Task C1124_Join_reports_the_current_attempt_park_in_nine_statements()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var plain = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var sessionC = Guid.NewGuid();
        var taskA = Guid.NewGuid();
        var taskB = Guid.NewGuid();
        var taskC = Guid.NewGuid();
        var blockA = Guid.NewGuid();
        var historicalPark = Guid.NewGuid();
        var olderPark = Guid.NewGuid();
        var newerPark = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var historicalReceipt = Guid.NewGuid();
        var olderAt = Now.AddMinutes(10);
        var newerAt = Now.AddMinutes(20);

        await using (var db = new AppDbContext(plain))
        {
            db.Projects.Add(new Project
            {
                Id = projectId,
                Name = "c1124-join",
                GitRepositoryUrl = "https://example.invalid/c1124.git",
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            db.Boards.Add(new Board
            {
                Id = boardId,
                ProjectId = projectId,
                Name = "c1124",
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            db.BoardColumns.Add(new BoardColumn
            {
                Id = columnId,
                BoardId = boardId,
                Name = "Backlog",
                StateKey = "backlog",
                CardStatus = CardStatus.Backlog,
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            db.Cards.Add(new Card
            {
                Id = cardId,
                BoardId = boardId,
                BoardColumnId = columnId,
                Identifier = "CARD-1124",
                Title = "park",
                CreatedAt = Now,
                UpdatedAt = Now,
            });
            db.AgentSessions.AddRange(
                Session(sessionA, SessionStatus.Running),
                Session(sessionB, SessionStatus.Running),
                Session(sessionC, SessionStatus.Running));
            db.AgentTasks.AddRange(
                TaskRow(taskA, sessionA, AgentTaskStatus.Blocked, Now, attempt: 2, cardId: cardId),
                TaskRow(taskB, sessionB, AgentTaskStatus.Working, Now, attempt: 1),
                TaskRow(taskC, sessionC, AgentTaskStatus.Blocked, Now, attempt: 3));
            db.AgentTaskEvents.Add(Event(blockA, taskA, sessionA, newerAt));
            db.AgentTaskParks.AddRange(
                Park(taskA, attempt: 1, blockEventId: Guid.NewGuid(), receipt: historicalReceipt, at: Now,
                    state: AgentTaskParkState.Parked, id: historicalPark),
                Park(taskA, attempt: 2, blockEventId: Guid.NewGuid(), receipt: null, at: olderAt,
                    state: AgentTaskParkState.Requested, reason: "park_requested", id: olderPark),
                Park(taskA, attempt: 2, blockEventId: blockA, receipt: null, at: newerAt,
                    state: AgentTaskParkState.Held, reason: "park_dirty", releaseId: releaseId,
                    sync: AgentTaskParkSyncState.Pending, id: newerPark),
                Park(taskC, attempt: 1, blockEventId: Guid.NewGuid(), receipt: Guid.NewGuid(), at: Now,
                    state: AgentTaskParkState.Parked));
            await db.SaveChangesAsync();
        }

        var capture = new CountingCommandInterceptor();
        await using var read = new AppDbContext(CountingOptions(schema.ConnectionString, capture));
        var rows = await SeatDesktopJoin.LoadAsync(read, [sessionA, sessionB, sessionC], CancellationToken.None);
        var commands = capture.Commands.ToArray();
        commands.Length.ShouldBe(9, string.Join("\n---\n", commands));
        foreach (var sql in commands)
        {
            sql.Contains("INSERT", StringComparison.Ordinal).ShouldBeFalse(sql);
            sql.Contains("UPDATE", StringComparison.Ordinal).ShouldBeFalse(sql);
            sql.Contains("DELETE", StringComparison.Ordinal).ShouldBeFalse(sql);
        }

        var owned = rows[sessionA];
        owned.OpenTaskId.ShouldBe(taskA);
        owned.PublicationReceipt.ShouldBeFalse();
        owned.Park.ShouldBe(new SeatParkRow(
            newerPark, AgentTaskParkState.Held, "park_dirty", releaseId, AgentTaskParkSyncState.Pending));

        var working = rows[sessionB];
        working.OpenTaskId.ShouldBe(taskB);
        working.Park.ShouldBeNull();

        var historical = rows[sessionC];
        historical.OpenTaskId.ShouldBe(taskC);
        historical.Park.ShouldBeNull();
        historical.PublicationReceipt.ShouldBeFalse();
    }

    [Test]
    public async Task C1079_Join_latest_task_query_is_grouped_per_session()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var plain = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var session = Guid.NewGuid();
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        await using (var db = new AppDbContext(plain))
        {
            db.AgentSessions.Add(Session(session, SessionStatus.Running));
            db.AgentTasks.AddRange(
                TaskRow(older, session, AgentTaskStatus.Succeeded, Now, completedAt: Now.AddMinutes(1)),
                TaskRow(newer, session, AgentTaskStatus.Blocked, Now.AddMinutes(10)));
            await db.SaveChangesAsync();
        }

        var capture = new CountingCommandInterceptor();
        var options = CountingOptions(schema.ConnectionString, capture);
        await using var read = new AppDbContext(options);
        var rows = await SeatDesktopJoin.LoadAsync(read, [session], CancellationToken.None);
        rows[session].LatestTask.ShouldNotBeNull();
        rows[session].LatestTask!.Id.ShouldBe(newer);
        capture.Commands.Any(sql =>
            sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("MAX(", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue(
            string.Join("\n---\n", capture.Commands));
    }

    private static DbContextOptions<AppDbContext> CountingOptions(
        string connectionString, CountingCommandInterceptor capture) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            })
            .AddInterceptors(capture)
            .Options;

    private static AgentSession Session(Guid id, SessionStatus status) => new()
    {
        Id = id,
        DefinitionName = "grok",
        AgentKind = AgentKind.Grok,
        Status = status,
        Cwd = "/work",
        Cols = 80,
        Rows = 24,
        CreatedAt = Now,
        StartedAt = Now,
        LastSeenAt = Now,
    };

    private static Agent Agent(Guid id, string slug, Guid sessionId, bool warm) => new()
    {
        Id = id,
        Name = slug,
        Slug = slug,
        Status = AgentStatus.Idle,
        PersistentSessionId = sessionId.ToString("D"),
        PoolIdleSince = warm ? Now.AddMinutes(-5) : null,
        IsPoolDelegate = warm,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static AgentTask TaskRow(
        Guid id, Guid sessionId, AgentTaskStatus status, DateTime created, int attempt = 1,
        Guid? cardId = null, Guid? agentId = null, DateTime? completedAt = null,
        AgentTaskRole role = AgentTaskRole.Custom) => new()
    {
        Id = id,
        RootTaskId = id,
        AgentSessionId = sessionId,
        Status = status,
        Attempt = attempt,
        Role = role,
        CardId = cardId,
        AgentId = agentId,
        Title = "c1079",
        Goal = "seat join",
        CreatedAt = created,
        DispatchedAt = created,
        CompletedAt = completedAt,
    };

    private static AgentTaskEvent Event(Guid id, Guid taskId, Guid sessionId, DateTime at) => new()
    {
        Id = id,
        AgentTaskId = taskId,
        AgentSessionId = sessionId,
        Type = AgentTaskEventType.Blocked,
        Detail = "blocked",
        At = at,
    };

    private static AgentTaskPark Park(
        Guid taskId, int attempt, Guid blockEventId, Guid? receipt, DateTime at,
        AgentTaskParkState state = AgentTaskParkState.Requested,
        string reason = "park_requested",
        Guid? releaseId = null,
        AgentTaskParkSyncState sync = AgentTaskParkSyncState.NotRequired,
        Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TaskId = taskId,
        Attempt = attempt,
        BlockEventId = blockEventId,
        TaskConcurrencyToken = Guid.NewGuid(),
        PublicationReceiptId = receipt,
        State = state,
        ReasonCode = reason,
        RunnerSeatReleaseId = releaseId,
        SyncState = sync,
        BlockedAt = at,
        CreatedAt = at,
        UpdatedAt = at,
    };
}
