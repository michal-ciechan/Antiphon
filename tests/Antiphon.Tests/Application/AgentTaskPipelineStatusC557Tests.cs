using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskPipelineStatusTests
{
    private static readonly DateTime C557Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly AgentTaskRole[] C557Stages =
    [
        AgentTaskRole.Investigate, AgentTaskRole.Plan, AgentTaskRole.TestDesign,
        AgentTaskRole.Code, AgentTaskRole.Review, AgentTaskRole.Mutation,
    ];

    private static Guid C557Id(int number) => Guid.Parse($"00000000-0000-0000-0000-{number:000000000000}");

    private sealed class C557Seed(AppDbContext db)
    {
        private int _nextTask = 30000;
        private readonly Project _project = new()
        {
            Id = C557Id(8000), Name = "pipeline-557", GitRepositoryUrl = "https://example.test/c557.git",
            CreatedAt = C557Now, UpdatedAt = C557Now,
        };

        public Guid Board(int number, bool archived = false, bool terminal = false)
        {
            if (db.Projects.Local.All(p => p.Id != _project.Id)) db.Projects.Add(_project);
            var boardId = C557Id(1000 + number);
            db.Boards.Add(new Board
            {
                Id = boardId, ProjectId = _project.Id, Name = $"Board {number}",
                ArchivedAt = archived ? C557Now : null, CreatedAt = C557Now, UpdatedAt = C557Now,
            });
            db.BoardColumns.Add(new BoardColumn
            {
                Id = C557Id(2000 + number), BoardId = boardId, StateKey = "backlog",
                Name = "Backlog", ColumnOrder = 0, CardStatus = CardStatus.Backlog,
                IsTerminal = terminal, CreatedAt = C557Now, UpdatedAt = C557Now,
            });
            return boardId;
        }

        public Card Card(int number, Guid boardId, CardStatus status = CardStatus.Backlog,
            CardImportance importance = CardImportance.Normal, CardUrgency urgency = CardUrgency.Normal,
            int? position = null, DateTime? dueAt = null, DateTime? createdAt = null,
            bool archived = false, Guid? ownerSession = null, string[]? labels = null,
            string? identifier = null)
        {
            var card = new Card
            {
                Id = C557Id(10000 + number), BoardId = boardId,
                BoardColumnId = C557Id(2000 + int.Parse(boardId.ToString()[^12..]) - 1000),
                Identifier = identifier ?? $"CARD-{number:0000}", Title = $"Card {number}",
                Description = "pipeline candidate", Status = status, Importance = importance,
                Urgency = urgency, Position = position, DueAt = dueAt,
                LabelsJson = JsonSerializer.Serialize(labels ?? []), OwnerSessionId = ownerSession,
                ArchivedAt = archived ? C557Now : null,
                TerminalReason = status is CardStatus.Done or CardStatus.Canceled ? "covered by landed CARD-0557" : null,
                CreatedAt = createdAt ?? C557Now.AddDays(-30), UpdatedAt = C557Now,
            };
            db.Cards.Add(card);
            return card;
        }

        public AgentTask Task(AgentTaskRole role, AgentTaskStatus status, Card? card = null,
            DateTime? completedAt = null, PipelineHandoffKind? next = null,
            string? handoff = null, string? deliverable = null)
        {
            var id = C557Id(_nextTask++);
            var task = new AgentTask
            {
                Id = id, RootTaskId = id, Role = role, Status = status,
                Title = $"task {id}", Goal = "pipeline test", CardId = card?.Id,
                Workspace = WorkspaceMode.Worktree, WorkingDirectory = "/tmp/c557",
                CreatedAt = C557Now.AddDays(-2),
                DispatchedAt = status is AgentTaskStatus.Queued ? null : C557Now.AddDays(-2),
                CompletedAt = completedAt ?? (status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed
                    or AgentTaskStatus.Canceled ? C557Now.AddDays(-1) : null),
                NextStage = next, NextHandoff = handoff, DeliverablePath = deliverable,
            };
            db.AgentTasks.Add(task);
            return task;
        }

        public async Task SaveAsync()
        {
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
    }

    private static async Task<Antiphon.Server.Application.Dtos.AgentTaskPipelineDto> C557Read(
        AppDbContext db, FakeTimeProvider? clock = null)
    {
        db.ChangeTracker.Clear();
        return await CreateService(db, time: clock ?? new FakeTimeProvider(new DateTimeOffset(C557Now)))
            .GetAsync(CancellationToken.None);
    }

    [Test]
    public async Task C557_backlog_status_guard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db);
        var board = seed.Board(1);
        var keep = seed.Card(1, board);
        foreach (var (number, status) in new[]
        {
            (2, CardStatus.InProgress), (3, CardStatus.Review), (4, CardStatus.NeedsDecision),
            (5, CardStatus.Done), (6, CardStatus.Canceled),
        }) seed.Card(number, board, status);
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Total.ShouldBe(1);
        backlog.Items.Select(i => i.CardId).ShouldBe([keep.Id]);
    }

    [Test]
    public async Task C557_archived_card_guard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        var keep = seed.Card(1, board); seed.Card(2, board, archived: true);
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Total.ShouldBe(1); backlog.Items.Select(i => i.CardId).ShouldBe([keep.Id]);
    }

    [Test]
    public async Task C557_archived_board_guard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db);
        var keep = seed.Card(1, seed.Board(1)); seed.Card(2, seed.Board(2, archived: true));
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Total.ShouldBe(1); backlog.Items.Select(i => i.CardId).ShouldBe([keep.Id]);
    }

    [Test]
    public async Task C557_terminal_column_guard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db);
        var keep = seed.Card(1, seed.Board(1)); seed.Card(2, seed.Board(2, terminal: true));
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Total.ShouldBe(1); backlog.Items.Select(i => i.CardId).ShouldBe([keep.Id]);
    }

    [Test]
    public async Task C557_owner_session_guard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var owner = C557Id(50000);
        await SeedSessionAsync(db, owner, "/tmp/c557");
        var seed = new C557Seed(db); var board = seed.Board(1);
        var keep = seed.Card(1, board); seed.Card(2, board, ownerSession: owner);
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Total.ShouldBe(1); backlog.Items.Select(i => i.CardId).ShouldBe([keep.Id]);
    }

    [Test]
    public async Task C557_open_stage_guard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        var excluded = new List<Guid>(); var n = 1;
        foreach (var role in C557Stages)
        foreach (var status in new[] { AgentTaskStatus.Queued, AgentTaskStatus.Dispatched,
            AgentTaskStatus.Working, AgentTaskStatus.Blocked })
        {
            var card = seed.Card(n++, board); excluded.Add(card.Id); seed.Task(role, status, card);
        }
        var fresh = seed.Card(25, seed.Board(2), identifier: "CARD-0001");
        seed.Task(AgentTaskRole.Investigate, AgentTaskStatus.Queued);
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Total.ShouldBe(1); backlog.Items.Select(i => i.CardId).ShouldBe([fresh.Id]);
        excluded.Count.ShouldBe(24);
        excluded.ShouldNotContain(fresh.Id);
    }

    [Test]
    public async Task C557_succeeded_stage_guard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        var n = 1;
        foreach (var role in C557Stages)
        {
            var card = seed.Card(n++, board);
            var task = seed.Task(role, AgentTaskStatus.Succeeded, card,
                next: role == AgentTaskRole.Code ? PipelineHandoffKind.Land : PipelineHandoffKind.None);
            if (role == AgentTaskRole.Code) task.DeliverableRef = "landed-sha";
            if (role == AgentTaskRole.Mutation) seed.Task(role, AgentTaskStatus.Failed, card);
        }
        var fresh = seed.Card(7, board);
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Total.ShouldBe(1); backlog.Items.Select(i => i.CardId).ShouldBe([fresh.Id]);
    }

    [Test]
    public async Task C557_retry_and_helper_controls()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        var cards = new List<Card>(); var n = 1;
        foreach (var role in C557Stages)
        {
            var card = seed.Card(n++, board); cards.Add(card);
            seed.Task(role, n % 2 == 0 ? AgentTaskStatus.Failed : AgentTaskStatus.Canceled, card);
        }
        var helperCard = seed.Card(7, board); cards.Add(helperCard);
        seed.Task(AgentTaskRole.Docs, AgentTaskStatus.Working, helperCard);
        seed.Task(AgentTaskRole.Docs, AgentTaskStatus.Succeeded, helperCard);
        var fresh = seed.Card(8, board, identifier: "CARD-0009"); cards.Add(fresh);
        seed.Card(9, seed.Board(2), CardStatus.Done, identifier: "CARD-0009");
        seed.Task(AgentTaskRole.Code, AgentTaskStatus.Succeeded);
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Total.ShouldBe(8);
        backlog.Items.Select(i => i.CardId).ShouldBe(cards.Take(5).Select(c => c.Id));
    }

    [Test]
    public async Task C557_post_land_companion_guard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        seed.Card(1, board, importance: CardImportance.Critical, labels: ["post-land-verification"]);
        var title = seed.Card(2, board); title.Title = "post-land-verification in prose";
        var label = seed.Card(3, board, labels: ["post-land-verification-notes"]);
        var failed = seed.Card(4, board, labels: ["post-land-verification"]);
        seed.Task(AgentTaskRole.Mutation, AgentTaskStatus.Failed, failed);
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Total.ShouldBe(2);
        backlog.Items.Select(i => i.CardId).ShouldBe([title.Id, label.Id]);
    }

    [Test]
    public async Task C557_rank_and_importance_order()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        var critical = seed.Card(5, board, importance: CardImportance.Critical, createdAt: C557Now.AddDays(-1));
        var normalNow = seed.Card(4, board, urgency: CardUrgency.Now, createdAt: C557Now.AddDays(-2));
        var lowNow = seed.Card(3, board, importance: CardImportance.Low, urgency: CardUrgency.Now,
            createdAt: C557Now.AddDays(-3));
        var normal = seed.Card(2, board, createdAt: C557Now.AddDays(-4));
        var low = seed.Card(1, board, importance: CardImportance.Low, createdAt: C557Now.AddDays(-5));
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Items.Select(i => i.CardId).ShouldBe([critical.Id, normalNow.Id, lowNow.Id, normal.Id, low.Id]);
        backlog.Items.Select(i => i.Rank).ShouldBe([4, 6, 9, 10, 13]);
    }

    [Test]
    public async Task C557_position_due_created_order()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        var first = seed.Card(5, board, position: 1, dueAt: C557Now.AddDays(50), createdAt: C557Now.AddDays(-1));
        var older = seed.Card(4, board, position: 2, dueAt: C557Now.AddDays(20), createdAt: C557Now.AddDays(-9));
        var newer = seed.Card(3, board, position: 2, dueAt: C557Now.AddDays(20), createdAt: C557Now.AddDays(-2));
        var laterDue = seed.Card(2, board, position: 2, dueAt: C557Now.AddDays(25), createdAt: C557Now.AddDays(-99));
        var unplaced = seed.Card(1, board, dueAt: C557Now.AddDays(15), createdAt: C557Now.AddDays(-100));
        seed.Card(6, board, dueAt: null, createdAt: C557Now.AddDays(-200));
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Total.ShouldBe(6);
        backlog.Items.Select(i => i.CardId).ShouldBe([first.Id, older.Id, newer.Id, laterDue.Id, unplaced.Id]);
    }

    [Test]
    public async Task C557_due_clock_boundaries()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        var nowEdge = seed.Card(1, board, dueAt: C557Now.AddDays(3));
        var soonEdge = seed.Card(2, board, dueAt: C557Now.AddDays(3).AddTicks(10));
        var fourteen = seed.Card(3, board, dueAt: C557Now.AddDays(14));
        var normal = seed.Card(4, board, dueAt: C557Now.AddDays(14).AddTicks(10));
        var overdue = seed.Card(5, board, dueAt: C557Now.AddDays(-1));
        var explicitNow = seed.Card(6, board, urgency: CardUrgency.Now);
        await seed.SaveAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(C557Now));
        var first = await C557Read(db, clock);
        first.AsOf.ShouldBe(C557Now);
        first.InvestigateBacklog.Total.ShouldBe(6);
        first.InvestigateBacklog.Items.Select(i => i.CardId)
            .ShouldBe([overdue.Id, nowEdge.Id, explicitNow.Id, soonEdge.Id, fourteen.Id]);
        first.InvestigateBacklog.Items.Select(i => i.Rank).ShouldBe([6, 6, 6, 8, 8]);
        clock.Advance(TimeSpan.FromTicks(10));
        var second = await C557Read(db, clock);
        second.AsOf.ShouldBe(C557Now.AddTicks(10));
        second.InvestigateBacklog.Items.Select(i => i.CardId)
            .ShouldBe([overdue.Id, nowEdge.Id, soonEdge.Id, explicitNow.Id, fourteen.Id]);
        second.InvestigateBacklog.Items.Select(i => i.Rank).ShouldBe([6, 6, 6, 6, 8]);
        // PostgreSQL stores microseconds; the 10-tick difference is one representable microsecond.
        normal.Id.ShouldNotBe(second.InvestigateBacklog.Items.First().CardId);
    }

    [Test]
    public async Task C557_board_and_card_ties()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var laterBoard = seed.Board(2); var earlierBoard = seed.Board(1);
        var last = seed.Card(3, laterBoard, identifier: "CARD-0001");
        var middle = seed.Card(2, earlierBoard, identifier: "CARD-9999");
        var first = seed.Card(1, earlierBoard, identifier: "CARD-9998");
        await seed.SaveAsync();
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var backlog = (await C557Read(db)).InvestigateBacklog;
            backlog.Total.ShouldBe(3);
            backlog.Items.Select(i => i.CardId).ShouldBe([first.Id, middle.Id, last.Id]);
        }
    }

    [Test]
    public async Task C557_uncapped_candidates()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        for (var i = 1; i <= 501; i++) seed.Card(i, board, importance: CardImportance.Low);
        var high = new List<Card>();
        for (var i = 600; i < 606; i++)
        {
            var card = seed.Card(i, board, importance: CardImportance.Critical);
            card.UpdatedAt = C557Now.AddDays(-20); high.Add(card);
        }
        await seed.SaveAsync();
        var backlog = (await C557Read(db)).InvestigateBacklog;
        backlog.Total.ShouldBe(507);
        backlog.Items.Select(i => i.CardId).ShouldBe(high.Take(5).Select(c => c.Id));
        backlog.Items.Select(i => i.Rank).ShouldBe([4, 4, 4, 4, 4]);
    }

    [Test]
    public async Task C557_count_and_limit()
    {
        foreach (var count in new[] { 0, 1, 5, 7 })
        {
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var db = CreateContext(schema);
            var seed = new C557Seed(db); var board = seed.Board(1);
            var expected = new List<Guid>();
            for (var i = 1; i <= count; i++) expected.Add(seed.Card(i, board).Id);
            seed.Card(100, board, importance: CardImportance.Critical, archived: true);
            seed.Card(101, seed.Board(2, archived: true), importance: CardImportance.Critical);
            await seed.SaveAsync();
            var backlog = (await C557Read(db)).InvestigateBacklog;
            backlog.Total.ShouldBe(count);
            backlog.Items.Count.ShouldBe(Math.Min(count, 5));
            backlog.Items.Select(i => i.CardId).ShouldBe(expected.Take(5));
        }
    }

    [Test]
    public async Task C557_ready_priority_order()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        var expected = new Dictionary<AgentTaskRole, Guid[]>(); var n = 1;
        foreach (var role in C557Stages)
        {
            var low = seed.Card(n++, board, CardStatus.Review, CardImportance.Low);
            var high = seed.Card(n++, board, CardStatus.Review, CardImportance.Critical);
            var target = Enum.Parse<PipelineHandoffKind>(role.ToString());
            seed.Task(AgentTaskRole.Plan, AgentTaskStatus.Succeeded, low,
                completedAt: C557Now.AddDays(-3), next: target, deliverable: "docs/low.md");
            seed.Task(AgentTaskRole.Plan, AgentTaskStatus.Succeeded, high,
                completedAt: C557Now.AddDays(-1), next: target, deliverable: "docs/high.md");
            expected[role] = [high.Id, low.Id];
        }
        await seed.SaveAsync();
        var dto = await C557Read(db);
        dto.AsOf.ShouldBe(C557Now);
        foreach (var role in C557Stages)
        {
            var rows = dto.Stages.Single(s => s.Role == role).Ready;
            rows.Select(r => r.Card.Id).ShouldBe(expected[role]);
            rows.Select(r => r.Rank).ShouldBe([4, 13]);
        }
    }

    [Test]
    public async Task C557_ready_tie_order()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var lateBoard = seed.Board(2); var earlyBoard = seed.Board(1);
        var last = seed.Card(3, lateBoard, CardStatus.Review, identifier: "CARD-0001");
        var middle = seed.Card(2, earlyBoard, CardStatus.Review, identifier: "CARD-9999");
        var first = seed.Card(1, earlyBoard, CardStatus.Review, identifier: "CARD-9998");
        var oldest = seed.Card(4, lateBoard, CardStatus.Review, identifier: "CARD-9997");
        var tLast = seed.Task(AgentTaskRole.Plan, AgentTaskStatus.Succeeded, last,
            completedAt: C557Now.AddDays(-1), next: PipelineHandoffKind.Code,
            handoff: "last", deliverable: "docs/last.md");
        var tMiddle = seed.Task(AgentTaskRole.Plan, AgentTaskStatus.Succeeded, middle,
            completedAt: C557Now.AddDays(-1), next: PipelineHandoffKind.Code,
            handoff: "middle", deliverable: "docs/middle.md");
        var tFirst = seed.Task(AgentTaskRole.Plan, AgentTaskStatus.Succeeded, first,
            completedAt: C557Now.AddDays(-1), next: PipelineHandoffKind.Code,
            handoff: "first", deliverable: "docs/first.md");
        var tOldest = seed.Task(AgentTaskRole.Plan, AgentTaskStatus.Succeeded, oldest,
            completedAt: C557Now.AddDays(-2), next: PipelineHandoffKind.Code,
            handoff: "oldest", deliverable: "docs/oldest.md");
        await seed.SaveAsync();
        var rows = (await C557Read(db)).Stages.Single(s => s.Role == AgentTaskRole.Code).Ready;
        rows.Select(r => r.Card.Id).ShouldBe([oldest.Id, first.Id, middle.Id, last.Id]);
        rows.Select(r => r.SourcePlanTaskId).ShouldBe([tOldest.Id, tFirst.Id, tMiddle.Id, tLast.Id]);
        rows.Select(r => r.DeliverablePath).ShouldBe(["docs/oldest.md", "docs/first.md", "docs/middle.md", "docs/last.md"]);
        rows.Select(r => r.Handoff).ShouldBe(["oldest", "first", "middle", "last"]);
    }

    [Test]
    public async Task C557_read_is_advisory_and_nonmutating()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        var card = seed.Card(1, board, urgency: CardUrgency.Normal);
        seed.Task(AgentTaskRole.Code, AgentTaskStatus.Working);
        db.RoutingPins.Add(new RoutingPin
        {
            Id = C557Id(60000), CardId = card.Id, Role = AgentTaskRole.Investigate,
            Provenance = RoutingPinProvenance.Human, Strength = RoutingPinStrength.Required,
            NotBefore = C557Now.AddDays(1), Reason = "later", CreatedAt = C557Now,
            UpdatedAt = C557Now,
        });
        await seed.SaveAsync();
        var before = await db.Cards.AsNoTracking().Where(c => c.Id == card.Id)
            .Select(c => new { c.Status, c.Urgency, c.ConcurrencyToken, c.RevisionCount }).SingleAsync();
        var taskCount = await db.AgentTasks.CountAsync();
        var pinCount = await db.RoutingPins.CountAsync();
        for (var i = 0; i < 2; i++)
        {
            var dto = await C557Read(db);
            dto.InvestigateBacklog.Items.Select(x => x.CardId).ShouldBe([card.Id]);
            db.ChangeTracker.Entries().ShouldBeEmpty();
        }
        var after = await db.Cards.AsNoTracking().Where(c => c.Id == card.Id)
            .Select(c => new { c.Status, c.Urgency, c.ConcurrencyToken, c.RevisionCount }).SingleAsync();
        after.ShouldBe(before);
        (await db.AgentTasks.CountAsync()).ShouldBe(taskCount);
        (await db.RoutingPins.CountAsync()).ShouldBe(pinCount);
        (await db.CardRevisions.CountAsync()).ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task C557_formal_investigate_is_not_fresh()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var seed = new C557Seed(db); var board = seed.Board(1);
        var formal = seed.Card(1, board, CardStatus.Review);
        var fresh = seed.Card(2, board);
        seed.Task(AgentTaskRole.Plan, AgentTaskStatus.Succeeded, formal,
            next: PipelineHandoffKind.Investigate, handoff: "re-investigate");
        seed.Task(AgentTaskRole.Code, AgentTaskStatus.Working);
        seed.Task(AgentTaskRole.Review, AgentTaskStatus.Queued);
        seed.Task(AgentTaskRole.Mutation, AgentTaskStatus.Blocked);
        await seed.SaveAsync();
        var dto = await C557Read(db);
        dto.InvestigateBacklog.Total.ShouldBe(1);
        dto.InvestigateBacklog.Items.Select(x => x.CardId).ShouldBe([fresh.Id]);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Investigate).Ready.Select(r => r.Card.Id)
            .ShouldBe([formal.Id]);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Code).InFlightCount.ShouldBe(1);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Review).Queued.Count.ShouldBe(1);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Mutation).Blocked.Count.ShouldBe(1);
        dto.InFlightAgainstCap.ShouldBe(1);
    }
}
