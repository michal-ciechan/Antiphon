using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class ExpectationSnapshotTests
{
    [Test]
    public async Task C650_Scope_excludes_other_boards_and_ambiguous_callers()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await World.CreateAsync(schema.ConnectionString);
        var now = world.Now;
        var ownAt = now.AddMinutes(-5);
        var foreignAt = now.AddMinutes(-1);
        var otherProject = Guid.NewGuid();
        var matched = Guid.NewGuid();
        var nullProject = Guid.NewGuid();
        var wrongProject = Guid.NewGuid();
        var unboundOwned = Guid.NewGuid();
        var unboundNull = Guid.NewGuid();
        var unboundOther = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var nullCard = Guid.NewGuid();
        var wrongCard = Guid.NewGuid();
        var foreignCard = Guid.NewGuid();
        var ownedSession = Guid.NewGuid();
        var nullSession = Guid.NewGuid();
        var otherSession = Guid.NewGuid();

        await using (var db = new AppDbContext(world.Options))
        {
            db.Projects.Add(new Project
            {
                Id = otherProject,
                Name = "other",
                GitRepositoryUrl = "",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Cards.Add(Card(nullCard, world, "C650N"));
            db.Cards.Add(Card(wrongCard, world, "C650W"));
            db.Cards.Add(Card(foreignCard, world, "C650F", world.OtherBoardId, world.OtherColumnId));
            db.AgentSessions.Add(Session(ownedSession, world.AgentId, now));
            db.AgentSessions.Add(Session(nullSession, null, now));
            db.AgentSessions.Add(Session(otherSession, Guid.NewGuid(), now));
            db.AgentTasks.Add(NewTask(matched, world.CardId, world.ProjectId, AgentTaskStatus.Dispatched, AgentTaskRole.Code, null, now));
            db.AgentTasks.Add(NewTask(nullProject, nullCard, null, AgentTaskStatus.Working, AgentTaskRole.Code, null, now));
            db.AgentTasks.Add(NewTask(wrongProject, wrongCard, otherProject, AgentTaskStatus.Dispatched, AgentTaskRole.Code, null, now));
            db.AgentTasks.Add(NewTask(unboundOwned, null, world.ProjectId, AgentTaskStatus.Dispatched, AgentTaskRole.Code, null, now, ownedSession));
            db.AgentTasks.Add(NewTask(unboundNull, null, world.ProjectId, AgentTaskStatus.Dispatched, AgentTaskRole.Code, null, now, nullSession));
            db.AgentTasks.Add(NewTask(unboundOther, null, world.ProjectId, AgentTaskStatus.Dispatched, AgentTaskRole.Code, null, now, otherSession));
            db.AgentTasks.Add(NewTask(foreign, foreignCard, world.ProjectId, AgentTaskStatus.Dispatched, AgentTaskRole.Code, null, now));
            db.AgentTaskEvents.Add(Event(matched, AgentTaskEventType.Dispatched, ownAt, "own"));
            db.AgentTaskEvents.Add(Event(foreign, AgentTaskEventType.Dispatched, foreignAt, "foreign"));
            await db.SaveChangesAsync();
        }

        await using var read = new AppDbContext(world.Options);
        var snapshot = await new ExpectationSnapshotReader(read).ReadAsync(
            world.Directive, world.Digest, now, ExpectationProbeInput.None, CancellationToken.None);

        snapshot.IncludedTaskIds.ShouldContain(matched);
        snapshot.IncludedTaskIds.ShouldContain(nullProject);
        snapshot.IncludedTaskIds.ShouldContain(unboundOwned);
        snapshot.IncludedTaskIds.ShouldNotContain(wrongProject);
        snapshot.IncludedTaskIds.ShouldNotContain(unboundNull);
        snapshot.IncludedTaskIds.ShouldNotContain(unboundOther);
        snapshot.IncludedTaskIds.ShouldNotContain(foreign);
        snapshot.AmbiguousUnboundExcluded.ShouldBe(2);
        snapshot.LastScopedDispatchAt.ShouldNotBeNull();
        snapshot.LastScopedDispatchAt!.Value.ShouldBe(ownAt, TimeSpan.FromMilliseconds(1));
        snapshot.LastScopedDispatchAt.Value.ShouldNotBe(foreignAt);
    }

    [Test]
    public async Task C650_Counts_runner_lanes_and_excludes_specialists()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await World.CreateAsync(schema.ConnectionString);
        var now = world.Now;
        var localRunning = Guid.NewGuid();
        var server2Running = Guid.NewGuid();
        var localQueued = Guid.NewGuid();
        var localBlocked = Guid.NewGuid();
        var specialist = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var foreignCard = Guid.NewGuid();
        var hold = DispatchHoldDetails.LeaseFenced("dead journal");

        await using (var db = new AppDbContext(world.Options))
        {
            db.Cards.Add(Card(foreignCard, world, "C650F", world.OtherBoardId, world.OtherColumnId));
            db.AgentTasks.Add(NewTask(localRunning, world.CardId, world.ProjectId, AgentTaskStatus.Dispatched, AgentTaskRole.Code, null, now));
            db.AgentTasks.Add(NewTask(server2Running, world.CardId, world.ProjectId, AgentTaskStatus.Working, AgentTaskRole.Review, "server2", now));
            db.AgentTasks.Add(NewTask(localQueued, world.CardId, world.ProjectId, AgentTaskStatus.Queued, AgentTaskRole.Code, null, now, repo: @"C:\src\Antiphon"));
            db.AgentTasks.Add(NewTask(localBlocked, world.CardId, world.ProjectId, AgentTaskStatus.Blocked, AgentTaskRole.Code, null, now));
            db.AgentTasks.Add(NewTask(specialist, world.CardId, world.ProjectId, AgentTaskStatus.Dispatched, AgentTaskRole.Check, null, now));
            db.AgentTasks.Add(NewTask(foreign, foreignCard, world.ProjectId, AgentTaskStatus.Dispatched, AgentTaskRole.Code, null, now));
            db.AgentTaskEvents.Add(Event(localQueued, AgentTaskEventType.Held, now, hold));
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(world.Options))
        {
            var snapshot = await new ExpectationSnapshotReader(db).ReadAsync(
                world.Directive, world.Digest, now, ExpectationProbeInput.None, CancellationToken.None);
            var local = snapshot.Lanes.Where(lane => lane.RunnerId is null).ShouldHaveSingleItem();
            var remote = snapshot.Lanes.Where(lane => lane.RunnerId == "server2").ShouldHaveSingleItem();
            local.Running.ShouldBe(1);
            local.RunningTaskIds.ShouldBe([localRunning]);
            local.Queued.ShouldBe(1);
            local.QueuedTaskIds.ShouldBe([localQueued]);
            local.Blocked.ShouldBe(1);
            local.RunningTaskIds.ShouldNotContain(localQueued);
            local.RunningTaskIds.ShouldNotContain(localBlocked);
            local.RunningTaskIds.ShouldNotContain(specialist);
            remote.Running.ShouldBe(1);
            remote.RunningTaskIds.ShouldBe([server2Running]);
            remote.Queued.ShouldBe(0);
            snapshot.IncludedTaskIds.ShouldNotContain(foreign);
            snapshot.IncludedTaskIds.ShouldNotContain(specialist);
            var queued = snapshot.Queued.ShouldHaveSingleItem();
            queued.TaskId.ShouldBe(localQueued);
            queued.HoldClass.ShouldBe(ExpectationHoldClass.RepositoryFenced);
            queued.HoldDetail.ShouldContain("dead journal");
        }

        var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        await using (var db = new AppDbContext(world.Options))
        {
            var service = new ExpectationWatchdogService(db, new ExpectationLedger(db, clock, new QuietBus()), clock);
            var scan = await service.ScanAsync(world.Directive, ExpectationProbeInput.None, CancellationToken.None);
            scan.NudgesCommitted.ShouldBe(0);
        }

        await using var fresh = new AppDbContext(world.Options);
        (await fresh.ExpectationNudges.CountAsync()).ShouldBe(0);
        (await fresh.SessionQueuedMessages.CountAsync()).ShouldBe(0);
        var episode = await fresh.ExpectationEpisodes.SingleAsync();
        episode.Kind.ShouldBe(ExpectationEpisodeKind.DispatchFence);
        episode.ResolvedAt.ShouldBeNull();
        episode.Evidence.ShouldContain("dead journal");
        var state = await fresh.ExpectationWatchStates.SingleAsync();
        state.LastSuccessfulScanAt.ShouldNotBeNull();
        state.LastSuccessfulScanAt!.Value.ShouldBe(now, TimeSpan.FromMilliseconds(1));
    }

    [Test]
    public async Task C650_Backlog_candidates_exclude_owned_held_unrated_and_open_work()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var schema = isolated;
        var world = await World.CreateAsync(schema.ConnectionString);
        var now = world.Now;
        var eligible = Guid.NewGuid();
        var rated = Guid.NewGuid();
        var closedWork = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var owned = Guid.NewGuid();
        var live = Guid.NewGuid();
        var held = Guid.NewGuid();
        var unrated = Guid.NewGuid();
        var archived = Guid.NewGuid();
        var active = Guid.NewGuid();
        var openWork = Guid.NewGuid();
        var otherBoard = Guid.NewGuid();
        var ownerSession = Guid.NewGuid();
        var liveSession = Guid.NewGuid();

        await using (var db = new AppDbContext(world.Options))
        {
            db.Cards.Add(Card(eligible, world, "C650E", status: CardStatus.Backlog));
            db.Cards.Add(Card(rated, world, "C650R", status: CardStatus.Backlog));
            db.Cards.Add(Card(closedWork, world, "C650S", status: CardStatus.Backlog));
            db.Cards.Add(Card(assigned, world, "C650A", status: CardStatus.Backlog, agentId: world.AgentId));
            db.Cards.Add(Card(owned, world, "C650O", status: CardStatus.Backlog));
            db.Cards.Add(Card(live, world, "C650L", status: CardStatus.Backlog));
            db.Cards.Add(Card(held, world, "C650H", status: CardStatus.Backlog, heldAt: now));
            db.Cards.Add(Card(unrated, world, "C650U", status: CardStatus.Backlog));
            db.Cards.Add(Card(archived, world, "C650Z", status: CardStatus.Backlog, archivedAt: now));
            db.Cards.Add(Card(active, world, "C650P", status: CardStatus.InProgress));
            db.Cards.Add(Card(openWork, world, "C650Q", status: CardStatus.Backlog));
            db.Cards.Add(Card(otherBoard, world, "C650X", world.OtherBoardId, world.OtherColumnId, CardStatus.Backlog));
            db.ExternalIssueRefs.Add(Issue(rated, now));
            db.ExternalIssueRefs.Add(Issue(unrated, now));
            db.AgentTasks.Add(NewTask(Guid.NewGuid(), closedWork, world.ProjectId, AgentTaskStatus.Succeeded, AgentTaskRole.Code, null, now));
            db.AgentTasks.Add(NewTask(Guid.NewGuid(), openWork, world.ProjectId, AgentTaskStatus.Queued, AgentTaskRole.Code, null, now));
            db.AgentSessions.Add(Session(liveSession, world.AgentId, now, live, SessionStatus.Running));
            await db.SaveChangesAsync();
            var ownedCard = await db.Cards.SingleAsync(card => card.Id == owned);
            db.AgentSessions.Add(Session(ownerSession, world.AgentId, now, owned));
            await db.SaveChangesAsync();
            ownedCard.OwnerSessionId = ownerSession;
            var ratedCard = await db.Cards.SingleAsync(card => card.Id == rated);
            ratedCard.ImportanceProvenance = CardImportanceProvenance.Human;
            await db.SaveChangesAsync();
        }

        await using var read = new AppDbContext(world.Options);
        var snapshot = await new ExpectationSnapshotReader(read).ReadAsync(
            world.Directive, world.Digest, now, ExpectationProbeInput.None, CancellationToken.None);
        snapshot.BacklogCandidateIds.OrderBy(id => id).ToArray()
            .ShouldBe(new[] { eligible, rated, closedWork }.OrderBy(id => id).ToArray());
        snapshot.EligibleBacklog.ShouldBe(3);
    }

    [Test]
    public async Task C650_Hold_classifier_matches_dispatcher_messages()
    {
        var now = new DateTime(2026, 9, 24, 2, 6, 23, DateTimeKind.Utc);
        var fenced = DispatchHoldDetails.LeaseFenced("dead journal 78ff");
        Classify(fenced, ExpectationHoldClass.RepositoryFenced);
        Classify(DispatchHoldDetails.LeaseOccupiedUnknown, ExpectationHoldClass.RepositoryOwnerUnknown);
        Classify(
            DispatchHoldDetails.RunnerUnavailable("server2", "lease expired"),
            ExpectationHoldClass.RunnerUnavailable);
        Classify("fable is held; dispatch paused for that model.", ExpectationHoldClass.ModelHeld);

        Classify(DispatchHoldDetails.ConcurrencyCap(4), ExpectationHoldClass.OrdinaryWait);
        Classify(
            DispatchHoldDetails.LeaseHeldByLand("abcd1234", "land the card", "req1", now),
            ExpectationHoldClass.OrdinaryWait);
        Classify(DispatchHoldDetails.RemoteMirrorRequested("server2", now), ExpectationHoldClass.OrdinaryWait);
        Classify(
            DispatchHoldDetails.RemotePrepBackoff("server2", 3, now.AddMinutes(2)),
            ExpectationHoldClass.OrdinaryWait);
        Classify(
            "routing pin not before 2099-01-01T00:00:00Z; dispatch paused (wait).",
            ExpectationHoldClass.OrdinaryWait);
        Classify(
            DispatchHoldDetails.PinnedAgentParkedOn("claude", "abcd1234", AgentTaskStatus.Working),
            ExpectationHoldClass.OrdinaryWait);
        Classify(
            DispatchHoldDetails.StandingAgentNoSession("orch"),
            ExpectationHoldClass.OrdinaryWait);
        Classify(
            "Held: running task abcd1234 \"title\" is already writing in this shared checkout (Shared/Shared; no intersecting scope - two shared writers share one working tree).",
            ExpectationHoldClass.OrdinaryWait);
        Classify(
            "held: CARD-0647's kept branch feat/x (task abcd1234) is landing and is not yet in origin/master",
            ExpectationHoldClass.OrdinaryWait);

        var wrapped = DispatchHoldDetails.Escalation(
            "Error:", 904, now.AddMinutes(-15), now.AddHours(-1), fenced, 0, 4, ["abcd1234"]);
        var classified = DispatchHoldDetails.Classify(wrapped);
        classified.Class.ShouldBe(ExpectationHoldClass.RepositoryFenced);
        classified.Evidence.ShouldBe(wrapped);
        classified.Evidence.ShouldContain(fenced);

        var unknown = DispatchHoldDetails.Classify("Held: the dispatcher grew a new sentence");
        unknown.Class.ShouldBe(ExpectationHoldClass.Unknown);
        unknown.Evidence.ShouldBe("Held: the dispatcher grew a new sentence");
        DispatchHoldDetails.Classify("  ").Class.ShouldBe(ExpectationHoldClass.Unknown);
        await Task.CompletedTask;
    }

    private static void Classify(string detail, ExpectationHoldClass expected)
    {
        var classified = DispatchHoldDetails.Classify(detail);
        classified.Class.ShouldBe(expected);
        classified.Evidence.ShouldBe(detail);
    }

    private static Card Card(
        Guid id,
        World world,
        string identifier,
        Guid? boardId = null,
        Guid? columnId = null,
        CardStatus status = CardStatus.Backlog,
        Guid? agentId = null,
        DateTime? heldAt = null,
        DateTime? archivedAt = null) => new()
    {
        Id = id,
        BoardId = boardId ?? world.BoardId,
        BoardColumnId = columnId ?? world.ColumnId,
        Identifier = identifier + id.ToString("N")[..6],
        Title = identifier,
        Status = status,
        AssignedAgentId = agentId,
        AutoDispatchHeldAt = heldAt,
        ArchivedAt = archivedAt,
        CreatedAt = world.Now,
        UpdatedAt = world.Now,
    };

    private static ExternalIssueRef Issue(Guid cardId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        CardId = cardId,
        TrackerKind = TrackerKind.GitHubIssues,
        ExternalId = cardId.ToString("N"),
        ExternalKey = "GH-" + cardId.ToString("N")[..8],
        Url = "https://example.test/" + cardId.ToString("N"),
        RawPayloadJson = "{}",
        LastSyncedAt = now,
        Origin = ExternalIssueOrigin.ExternalImport,
        Author = "importer",
        AuthorIsOperator = false,
    };

    private static AgentSession Session(
        Guid id, Guid? agentId, DateTime now, Guid? cardId = null, SessionStatus status = SessionStatus.Stopped) => new()
    {
        Id = id,
        StandingAgentId = agentId,
        CardId = cardId,
        DefinitionName = "claude",
        AgentKind = AgentKind.ClaudeCode,
        Status = status,
        Cwd = @"C:\src\Antiphon",
        CreatedAt = now,
        StartedAt = now,
        LastSeenAt = now,
    };

    private static AgentTask NewTask(
        Guid id,
        Guid? cardId,
        Guid? projectId,
        AgentTaskStatus status,
        AgentTaskRole role,
        string? runnerId,
        DateTime now,
        Guid? parentSessionId = null,
        string? repo = null) => new()
    {
        Id = id,
        RootTaskId = id,
        CardId = cardId,
        ProjectId = projectId,
        ParentSessionId = parentSessionId,
        Title = "task",
        Goal = "observe",
        Status = status,
        Role = role,
        RunnerId = runnerId,
        RepoPath = repo,
        WorkingDirectory = @"C:\src\Antiphon",
        CreatedAt = now,
        DispatchedAt = status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working ? now : null,
        LaunchEnvOverrideJson = "{}",
        InheritedLaunchEnvJson = "{}",
    };

    private static AgentTaskEvent Event(Guid taskId, AgentTaskEventType type, DateTime at, string detail) => new()
    {
        Id = Guid.NewGuid(),
        AgentTaskId = taskId,
        Type = type,
        At = at,
        Detail = detail,
    };

    private sealed class QuietBus : IEventBus
    {
        public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed record World(
        DbContextOptions<AppDbContext> Options,
        Guid ProjectId,
        Guid BoardId,
        Guid OtherBoardId,
        Guid ColumnId,
        Guid OtherColumnId,
        Guid AgentId,
        Guid CardId,
        ExpectationDirectiveSettings Directive,
        string Digest,
        DateTime Now)
    {
        public static async Task<World> CreateAsync(string connectionString)
        {
            var options = TestDbFixture.CreateDbContextOptions(connectionString);
            var now = new DateTime(2026, 9, 24, 2, 6, 23, DateTimeKind.Utc);
            var projectId = Guid.NewGuid();
            var boardId = Guid.NewGuid();
            var otherBoardId = Guid.NewGuid();
            var columnId = Guid.NewGuid();
            var otherColumnId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var cardId = Guid.NewGuid();
            var channelId = Guid.NewGuid();
            var directive = new ExpectationDirectiveSettings
            {
                Id = "tonight",
                AgentId = agentId,
                BoardId = boardId,
                AuditCardId = cardId,
                OperatorChannelId = channelId,
                Enabled = true,
                Targets =
                [
                    new ExpectationTargetSettings
                    {
                        InFlightTarget = 3,
                        Candidates =
                        [
                            new ExpectationCandidateSettings
                            {
                                AgentKind = AgentKind.Grok,
                                ModelLevel = AgentModelLevel.High,
                                SubscriptionKey = "local-key",
                            },
                        ],
                    },
                    new ExpectationTargetSettings
                    {
                        RunnerId = "server2",
                        InFlightTarget = 3,
                        Candidates =
                        [
                            new ExpectationCandidateSettings
                            {
                                AgentKind = AgentKind.ClaudeCode,
                                ModelLevel = AgentModelLevel.High,
                                SubscriptionKey = "remote-key",
                            },
                        ],
                    },
                ],
            };
            await using var db = new AppDbContext(options);
            db.Projects.Add(new Project
            {
                Id = projectId,
                Name = "c650",
                GitRepositoryUrl = "",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Boards.Add(new Board
            {
                Id = boardId,
                ProjectId = projectId,
                Name = "c650",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Boards.Add(new Board
            {
                Id = otherBoardId,
                ProjectId = projectId,
                Name = "other",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.BoardColumns.Add(new BoardColumn
            {
                Id = columnId,
                BoardId = boardId,
                StateKey = "backlog",
                Name = "Backlog",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.BoardColumns.Add(new BoardColumn
            {
                Id = otherColumnId,
                BoardId = otherBoardId,
                StateKey = "backlog",
                Name = "Backlog",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Cards.Add(new Card
            {
                Id = cardId,
                BoardId = boardId,
                BoardColumnId = columnId,
                Identifier = "C650" + cardId.ToString("N")[..8],
                Title = "scaffold",
                Status = CardStatus.InProgress,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = "Standing",
                Slug = "c650-" + agentId.ToString("N"),
                WorkingDirectory = @"C:\src\Antiphon",
                BoardId = boardId,
                IsPoolDelegate = false,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
            return new World(
                options,
                projectId,
                boardId,
                otherBoardId,
                columnId,
                otherColumnId,
                agentId,
                cardId,
                directive,
                ExpectationDirectiveDigest.Compute(directive),
                now);
        }
    }
}
