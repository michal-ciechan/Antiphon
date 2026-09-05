using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0352 S4 — card diagnosis sweep selection, ordering, backoff, and the sweep switch.
/// Isolated schema: SelectAsync is a global query and must not see another test's cards.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class CardDiagnosisSweepTests
{
    [Test]
    public async Task open_statuses_are_selected_and_done_canceled_archived_are_not()
    {
        await using var w = await World.CreateAsync();
        var backlog = await w.SeedCardAsync(CardStatus.Backlog);
        var inProgress = await w.SeedCardAsync(CardStatus.InProgress);
        var review = await w.SeedCardAsync(CardStatus.Review);
        var decision = await w.SeedCardAsync(CardStatus.NeedsDecision);
        var done = await w.SeedCardAsync(CardStatus.Done);
        var canceled = await w.SeedCardAsync(CardStatus.Canceled);
        var archivedCard = await w.SeedCardAsync(CardStatus.Backlog, archived: true);
        var archivedBoardCard = await w.SeedCardOnArchivedBoardAsync();

        var selected = await w.SelectAsync();

        selected.ShouldContain(backlog);
        selected.ShouldContain(inProgress);
        selected.ShouldContain(review);
        selected.ShouldContain(decision);
        selected.ShouldNotContain(done);
        selected.ShouldNotContain(canceled);
        selected.ShouldNotContain(archivedCard);
        selected.ShouldNotContain(archivedBoardCard);
    }

    [Test]
    public async Task a_card_with_only_complexity_is_still_selected_and_both_families_are_not()
    {
        await using var w = await World.CreateAsync();
        var onlyComplexity = await w.SeedCardAsync(
            CardStatus.Backlog, labels: ["bug", "complexity:medium"]);
        var onlyUi = await w.SeedCardAsync(CardStatus.Backlog, labels: ["ui:yes"]);
        var both = await w.SeedCardAsync(
            CardStatus.Backlog, labels: ["bug", "complexity:easy", "ui:no"]);
        var neither = await w.SeedCardAsync(CardStatus.Backlog, labels: ["bug"]);

        var selected = await w.SelectAsync();

        selected.ShouldContain(onlyComplexity);
        selected.ShouldContain(onlyUi);
        selected.ShouldContain(neither);
        selected.ShouldNotContain(both);
    }

    [Test]
    public async Task ordering_is_status_then_importance_urgency_created_and_the_batch_caps()
    {
        await using var w = await World.CreateAsync(s => s.DiagnoseSweepBatch = 5);
        var now = w.Clock.GetUtcNow().UtcDateTime;

        var review = await w.SeedCardAsync(CardStatus.Review, createdAt: now.AddHours(-1));
        var inProgress = await w.SeedCardAsync(CardStatus.InProgress, createdAt: now.AddHours(-1));
        var decision = await w.SeedCardAsync(CardStatus.NeedsDecision, createdAt: now.AddHours(-1));
        var olderBacklog = await w.SeedCardAsync(
            CardStatus.Backlog, importance: CardImportance.Critical, createdAt: now.AddHours(-3));
        var newerCritical = await w.SeedCardAsync(
            CardStatus.Backlog, importance: CardImportance.Critical, createdAt: now.AddHours(-1));
        var highSoon = await w.SeedCardAsync(
            CardStatus.Backlog,
            importance: CardImportance.High,
            urgency: CardUrgency.Soon,
            createdAt: now.AddHours(-1));
        var extra = await w.SeedCardAsync(CardStatus.Review, createdAt: now.AddHours(-4));

        var selected = await w.SelectAsync();

        selected.Count.ShouldBe(5);
        selected[0].ShouldBe(newerCritical, "Backlog + Critical + newest");
        selected[1].ShouldBe(olderBacklog, "Backlog + Critical + older");
        selected[2].ShouldBe(highSoon, "Backlog + High + Soon");
        selected[3].ShouldBe(decision, "NeedsDecision after Backlog");
        selected[4].ShouldBe(inProgress, "InProgress after NeedsDecision");
        selected.ShouldNotContain(review);
        selected.ShouldNotContain(extra);
    }

    [Test]
    public async Task a_row_one_hour_old_excludes_and_a_row_25_hours_old_includes()
    {
        await using var w = await World.CreateAsync();
        var recent = await w.SeedCardAsync(CardStatus.Backlog);
        var stale = await w.SeedCardAsync(CardStatus.Backlog);
        var now = w.Clock.GetUtcNow().UtcDateTime;
        await w.SeedDiagnosisAsync(recent, now.AddHours(-1));
        await w.SeedDiagnosisAsync(stale, now.AddHours(-25));

        var selected = await w.SelectAsync();

        selected.ShouldNotContain(recent);
        selected.ShouldContain(stale);
    }

    [Test]
    public async Task three_non_applied_rows_exclude_until_UpdatedAt_moves()
    {
        await using var w = await World.CreateAsync();
        var card = await w.SeedCardAsync(CardStatus.Backlog);
        var now = w.Clock.GetUtcNow().UtcDateTime;
        // Older than DiagnoseRetryHours so the 24 h window is not the thing excluding it —
        // only the three non-Applied rows since UpdatedAt.
        await w.SetUpdatedAtAsync(card, now.AddHours(-40));
        await w.SeedDiagnosisAsync(card, now.AddHours(-30), DiagnosisOutcome.Unclear);
        await w.SeedDiagnosisAsync(card, now.AddHours(-28), DiagnosisOutcome.RejectedUnparseable);
        await w.SeedDiagnosisAsync(card, now.AddHours(-26), DiagnosisOutcome.DegradedTimeout);

        (await w.SelectAsync()).ShouldNotContain(card);

        await w.SetUpdatedAtAsync(card, now);
        (await w.SelectAsync()).ShouldContain(card);
    }

    [Test]
    public async Task DiagnoseSweepEnabled_false_enqueues_nothing()
    {
        await using var w = await World.CreateAsync(s => s.DiagnoseSweepEnabled = false);
        await w.SeedCardAsync(CardStatus.Backlog);

        (await w.TickAsync()).ShouldBe(0);
        w.Queue.TryDequeue(out _).ShouldBeFalse();
    }

    [Test]
    public async Task a_tick_enqueues_selected_cards()
    {
        await using var w = await World.CreateAsync(s => s.DiagnoseSweepBatch = 2);
        var first = await w.SeedCardAsync(CardStatus.Backlog, importance: CardImportance.Critical);
        var second = await w.SeedCardAsync(CardStatus.Backlog, importance: CardImportance.High);
        await w.SeedCardAsync(CardStatus.Review);

        (await w.TickAsync()).ShouldBe(2);

        var a = w.Dequeue();
        var b = w.Dequeue();
        w.Queue.TryDequeue(out _).ShouldBeFalse();
        a.Kind.ShouldBe(DiagnosisKind.Labels);
        a.Forced.ShouldBeFalse();
        new[] { a.CardId, b.CardId }.ShouldBe([first, second], ignoreOrder: false);
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly ServiceProvider _provider;
        private int _n;

        private readonly string _scratch;

        private World(
            IsolatedTestSchema schema,
            ServiceProvider provider,
            FakeTimeProvider clock,
            DiagnoseQueue queue,
            Guid boardId,
            Dictionary<CardStatus, Guid> columns,
            string scratch)
        {
            _schema = schema;
            _provider = provider;
            _scratch = scratch;
            Clock = clock;
            Queue = queue;
            BoardId = boardId;
            Columns = columns;
        }

        public FakeTimeProvider Clock { get; }
        public DiagnoseQueue Queue { get; }
        public Guid BoardId { get; }
        public Dictionary<CardStatus, Guid> Columns { get; }

        public static async Task<World> CreateAsync(Action<DelegationSettings>? configure = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var clock = new FakeTimeProvider(new DateTimeOffset(2100, 6, 1, 12, 0, 0, TimeSpan.Zero));
            var settings = new DelegationSettings
            {
                DiagnoseEnabled = true,
                DiagnoseSweepEnabled = true,
                DiagnoseSweepBatch = 20,
                DiagnoseRetryHours = 24,
                DiagnoseMaxAttemptsPerCard = 3,
                DiagnoseDailyBudgetUsd = 2.00m,
                DiagnoseAgentSlug = $"diag-sweep-{Guid.NewGuid():N}"[..24],
                DiagnoseWorkingDirectory = Path.Combine(Path.GetTempPath(), $"antiphon-diag-sweep-{Guid.NewGuid():N}"),
            };
            configure?.Invoke(settings);
            Directory.CreateDirectory(settings.DiagnoseWorkingDirectory!);

            var queue = new DiagnoseQueue();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(Options.Create(settings));
            services.AddSingleton(queue);
            services.AddScoped<DiagnoseProvisioner>();
            services.AddScoped<CardDiagnosisSweep>();

            var provider = services.BuildServiceProvider();
            var (boardId, columns) = await SeedBoardAsync(provider, clock.GetUtcNow().UtcDateTime);
            return new World(schema, provider, clock, queue, boardId, columns, settings.DiagnoseWorkingDirectory!);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _schema.DisposeAsync();
            try { Directory.Delete(_scratch, recursive: true); }
            catch (IOException) { }
        }

        public async Task<IReadOnlyList<Guid>> SelectAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CardDiagnosisSweep>()
                .SelectAsync(CancellationToken.None);
        }

        public async Task<int> TickAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CardDiagnosisSweep>()
                .TickAsync(CancellationToken.None);
        }

        public DiagnoseRequest Dequeue()
        {
            Queue.TryDequeue(out var request).ShouldBeTrue();
            return request;
        }

        public async Task<Guid> SeedCardAsync(
            CardStatus status,
            IReadOnlyList<string>? labels = null,
            CardImportance importance = CardImportance.Normal,
            CardUrgency urgency = CardUrgency.Normal,
            DateTime? createdAt = null,
            bool archived = false)
        {
            var now = createdAt ?? Clock.GetUtcNow().UtcDateTime;
            var id = Guid.NewGuid();
            var n = Interlocked.Increment(ref _n);
            await using var db = CreateDb();
            db.Cards.Add(new Card
            {
                Id = id,
                BoardId = BoardId,
                BoardColumnId = Columns[status],
                Identifier = $"CARD-{n:0000}",
                Title = $"Sweep {n}",
                Description = "A card for the diagnose sweep.",
                Importance = importance,
                Urgency = urgency,
                LabelsJson = BoardService.SerializeLabels(labels ?? []),
                Status = status,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
                ArchivedAt = archived ? now : null,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<Guid> SeedCardOnArchivedBoardAsync()
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            await using var db = CreateDb();
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"archived-{Guid.NewGuid():N}",
                GitRepositoryUrl = "https://example.test/archived.git",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = project.Name,
                ArchivedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var column = new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                StateKey = "backlog",
                Name = "Backlog",
                CardStatus = CardStatus.Backlog,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                BoardColumnId = column.Id,
                Identifier = "CARD-9999",
                Title = "On archived board",
                Description = "Should not be swept.",
                Status = CardStatus.Backlog,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AddRange(project, board, column, card);
            await db.SaveChangesAsync();
            return card.Id;
        }

        public async Task SeedDiagnosisAsync(
            Guid cardId, DateTime createdAt, DiagnosisOutcome outcome = DiagnosisOutcome.Unclear)
        {
            await using var db = CreateDb();
            db.Diagnoses.Add(new DiagnosisRecord
            {
                Id = Guid.NewGuid(),
                Kind = DiagnosisKind.Labels,
                CardId = cardId,
                Outcome = outcome,
                CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        }

        public async Task SetUpdatedAtAsync(Guid cardId, DateTime updatedAt)
        {
            await using var db = CreateDb();
            var card = await db.Cards.SingleAsync(c => c.Id == cardId);
            card.UpdatedAt = DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        private AppDbContext CreateDb() =>
            new(_provider.GetRequiredService<DbContextOptions<AppDbContext>>());

        private static async Task<(Guid BoardId, Dictionary<CardStatus, Guid> Columns)> SeedBoardAsync(
            ServiceProvider provider, DateTime now)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "Sweep project",
                GitRepositoryUrl = "https://example.test/sweep.git",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Sweep",
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AddRange(project, board);
            var columns = new Dictionary<CardStatus, Guid>();
            var specs = new (CardStatus Status, string Key, string Name, bool Terminal)[]
            {
                (CardStatus.Backlog, "backlog", "Backlog", false),
                (CardStatus.InProgress, "in-progress", "In Progress", false),
                (CardStatus.Review, "review", "Review", false),
                (CardStatus.NeedsDecision, "needs-decision", "Needs decision", false),
                (CardStatus.Done, "done", "Done", true),
                (CardStatus.Canceled, "canceled", "Canceled", true),
            };
            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                var id = Guid.NewGuid();
                columns[spec.Status] = id;
                db.BoardColumns.Add(new BoardColumn
                {
                    Id = id,
                    BoardId = board.Id,
                    StateKey = spec.Key,
                    Name = spec.Name,
                    ColumnOrder = i,
                    CardStatus = spec.Status,
                    IsTerminal = spec.Terminal,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            await db.SaveChangesAsync();
            return (board.Id, columns);
        }
    }
}
