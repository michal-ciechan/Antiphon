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
/// CARD-0352 S4 — applying card labels: append, human-wins, forced, shadow, parse rejects,
/// conflict, and the same hold/budget/busy/timeout gates as job 1.
/// </summary>
[Category("Integration")]
[NotInParallel("DiagnoseLabels")]
public class CardDiagnosisApplyTests
{
    [Test]
    public void shipped_DiagnoseLabelMode_is_Shadow()
    {
        new DelegationSettings().DiagnoseLabelMode.ShouldBe(DiagnoseLabelMode.Shadow);
    }

    [Test]
    public async Task a_good_answer_appends_both_labels_and_writes_one_ContentEdit()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var card = await h.SeedCardAsync(labels: ["bug", "reliability"]);
        var tokenBefore = card.ConcurrencyToken;

        var run = Task.Run(() => h.Diagnose.RunCardAsync(card.Id, forced: false, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, "complexity=medium ui=no", 0.0091m);
        await h.PumpClockAsync(run);

        var reloaded = await h.ReloadCardAsync(card.Id);
        BoardService.ParseLabels(reloaded.LabelsJson)
            .ShouldBe(["bug", "reliability", "complexity:medium", "ui:no"]);
        reloaded.ConcurrencyToken.ShouldNotBe(tokenBefore);

        var revision = (await h.RevisionsAsync(card.Id)).ShouldHaveSingleItem();
        revision.Kind.ShouldBe(CardRevisionKind.ContentEdit);
        revision.EditedBy.ShouldBe("antiphon-diagnose");
        revision.Reason.ShouldContain("complexity=medium ui=no");
        revision.Reason.ShouldContain($"diagnose task {DelegationReportFormatter.Short(diagnoseTask.Id)}");
        revision.Reason.ShouldContain("$0.0091");

        h.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "CardChanged");
        var ledger = (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DiagnosisOutcome.Applied);
        ledger.Applied.ShouldBe("complexity=medium ui=no");
        ledger.Forced.ShouldBeFalse();
    }

    [Test]
    public async Task a_human_added_ui_label_is_kept_and_only_complexity_is_added()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var card = await h.SeedCardAsync(labels: ["bug"]);

        var run = Task.Run(() => h.Diagnose.RunCardAsync(card.Id, forced: false, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SetLabelsAsync(card.Id, ["bug", "ui:yes"]);
        await h.SettleAsync(diagnoseTask.Id, "complexity=hard ui=no", 0.001m);
        await h.PumpClockAsync(run);

        BoardService.ParseLabels((await h.ReloadCardAsync(card.Id)).LabelsJson)
            .ShouldBe(["bug", "ui:yes", "complexity:hard"]);
        (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem().Applied
            .ShouldBe("complexity=hard ui=yes");
    }

    [Test]
    public async Task Forced_replaces_diagnosis_labels_and_keeps_topic_labels()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var card = await h.SeedCardAsync(labels: ["pty", "complexity:easy", "ui:yes"]);

        var run = Task.Run(() => h.Diagnose.RunCardAsync(card.Id, forced: true, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, "complexity=hard ui=no", 0.001m);
        await h.PumpClockAsync(run);

        BoardService.ParseLabels((await h.ReloadCardAsync(card.Id)).LabelsJson)
            .ShouldBe(["pty", "complexity:hard", "ui:no"]);
        var ledger = (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DiagnosisOutcome.Applied);
        ledger.Forced.ShouldBeTrue();
    }

    [Test]
    public async Task Shadow_writes_the_ledger_and_does_not_touch_the_card()
    {
        using var h = new Harness(s => s.DiagnoseLabelMode = DiagnoseLabelMode.Shadow);
        var specialist = await h.EnsureSpecialistAsync();
        var card = await h.SeedCardAsync(labels: ["bug"]);
        var labelsBefore = card.LabelsJson;
        var tokenBefore = card.ConcurrencyToken;

        var run = Task.Run(() => h.Diagnose.RunCardAsync(card.Id, forced: false, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, "complexity=medium ui=yes", 0.001m);
        await h.PumpClockAsync(run);

        var reloaded = await h.ReloadCardAsync(card.Id);
        reloaded.LabelsJson.ShouldBe(labelsBefore);
        reloaded.ConcurrencyToken.ShouldBe(tokenBefore);
        (await h.RevisionsAsync(card.Id)).ShouldBeEmpty();
        var ledger = (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DiagnosisOutcome.Shadowed);
        ledger.Applied.ShouldBe("complexity=medium ui=yes");
    }

    [Test]
    public async Task unclear_writes_Unclear_and_does_not_touch_the_card()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var card = await h.SeedCardAsync();
        var labelsBefore = card.LabelsJson;

        var run = Task.Run(() => h.Diagnose.RunCardAsync(card.Id, forced: false, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, "unclear", 0.001m);
        await h.PumpClockAsync(run);

        (await h.ReloadCardAsync(card.Id)).LabelsJson.ShouldBe(labelsBefore);
        (await h.RevisionsAsync(card.Id)).ShouldBeEmpty();
        (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.Unclear);
    }

    [Test]
    public async Task prose_is_RejectedUnparseable()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var card = await h.SeedCardAsync();
        var labelsBefore = card.LabelsJson;

        var run = Task.Run(() => h.Diagnose.RunCardAsync(card.Id, forced: false, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, "this card looks medium and not UI", 0.001m);
        await h.PumpClockAsync(run);

        (await h.ReloadCardAsync(card.Id)).LabelsJson.ShouldBe(labelsBefore);
        (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.RejectedUnparseable);
    }

    [Test]
    public async Task a_duplicate_revision_number_is_RejectedConflict_and_the_card_is_untouched()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var card = await h.SeedCardAsync(labels: ["bug"]);
        await h.SeedConflictingRevisionAsync(card.Id);
        var labelsBefore = (await h.ReloadCardAsync(card.Id)).LabelsJson;

        var run = Task.Run(() => h.Diagnose.RunCardAsync(card.Id, forced: false, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, "complexity=easy ui=no", 0.001m);
        await h.PumpClockAsync(run);

        (await h.ReloadCardAsync(card.Id)).LabelsJson.ShouldBe(labelsBefore);
        (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.RejectedConflict);
    }

    [Test]
    public async Task a_held_alias_creates_no_seat_row_and_ledgers_DegradedHeld()
    {
        using var h = new Harness(held: true);
        var specialist = await h.EnsureSpecialistAsync();
        var card = await h.SeedCardAsync();

        await h.Diagnose.RunCardAsync(card.Id, forced: false, CancellationToken.None);

        (await h.DiagnoseRunCountAsync(specialist.Id)).ShouldBe(0);
        (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.DegradedHeld);
    }

    [Test]
    public async Task a_spent_budget_creates_no_seat_row_and_ledgers_DegradedBudget()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        await h.SeedDiagnoseCostAsync(2.00m);
        var card = await h.SeedCardAsync();

        await h.Diagnose.RunCardAsync(card.Id, forced: false, CancellationToken.None);

        (await h.DiagnoseRunCountAsync(specialist.Id)).ShouldBe(0);
        (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.DegradedBudget);
    }

    [Test]
    public async Task a_full_backlog_ledgers_DegradedBusy()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        await h.SeedPendingDiagnoseAsync(specialist.Id, AgentTaskStatus.Queued);
        await h.SeedPendingDiagnoseAsync(specialist.Id, AgentTaskStatus.Working);
        var card = await h.SeedCardAsync();

        await h.Diagnose.RunCardAsync(card.Id, forced: false, CancellationToken.None);

        (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.DegradedBusy);
        (await h.DiagnoseRunCountAsync(specialist.Id)).ShouldBe(2);
    }

    [Test]
    public async Task a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout()
    {
        using var h = new Harness(s => s.DiagnoseWaitSeconds = 4);
        var specialist = await h.EnsureSpecialistAsync();
        var card = await h.SeedCardAsync();

        var run = Task.Run(() => h.Diagnose.RunCardAsync(card.Id, forced: false, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.PumpClockAsync(run);

        (await h.ReloadAsync(diagnoseTask.Id)).Status.ShouldBe(AgentTaskStatus.Canceled);
        (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.DegradedTimeout);
        (await h.RevisionsAsync(card.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task both_families_already_present_skips_without_a_seat_row()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var card = await h.SeedCardAsync(labels: ["complexity:medium", "ui:no"]);

        await h.Diagnose.RunCardAsync(card.Id, forced: false, CancellationToken.None);

        (await h.DiagnoseRunCountAsync(specialist.Id)).ShouldBe(0);
        (await h.LedgerForCardAsync(card.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.SkippedAlreadyLabelled);
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly string _scratch;

        public Harness(Action<DelegationSettings>? configure = null, bool held = false, DateTimeOffset? now = null)
        {
            _scratch = Directory.CreateTempSubdirectory("antiphon-diagnose-labels").FullName;
            SpecialistSlug = $"diagnose-{Guid.NewGuid():N}"[..24];
            var settings = new DelegationSettings
            {
                MaxConcurrentTasks = 512,
                DiagnoseAgentSlug = SpecialistSlug,
                DiagnoseWorkingDirectory = _scratch,
                DiagnoseEnabled = true,
                DiagnoseTitleEnabled = true,
                DiagnoseSweepEnabled = true,
                DiagnoseLabelMode = DiagnoseLabelMode.Apply,
                DiagnoseWaitSeconds = 30,
                DiagnoseMaxBacklog = 2,
                DiagnoseDailyBudgetUsd = 2.00m,
                RolePolicy = new(StringComparer.OrdinalIgnoreCase),
            };
            configure?.Invoke(settings);

            Clock = new FakeTimeProvider(
                now ?? new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.Zero)
                    .AddDays(Random.Shared.Next(0, 20_000)));
            EventBus = new MockEventBus();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<IEventBus>(EventBus);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(Options.Create(settings));
            services.AddSingleton<DiagnoseQueue>();
            services.AddScoped<DiagnoseProvisioner>();
            services.AddScoped<IAlertService, AlertService>();
            services.AddScoped<IAlertRouter, NullAlertRouter>();
            if (held)
                services.AddSingleton<IModelAvailability>(new HeldAvailability());
            services.AddScoped<DiagnoseService>();

            _provider = services.BuildServiceProvider();
            Diagnose = _provider.CreateScope().ServiceProvider.GetRequiredService<DiagnoseService>();
        }

        public FakeTimeProvider Clock { get; }
        public MockEventBus EventBus { get; }
        public string SpecialistSlug { get; }
        public DiagnoseService Diagnose { get; }

        public void Dispose()
        {
            _provider.Dispose();
            try { Directory.Delete(_scratch, recursive: true); }
            catch (IOException) { }
        }

        public async Task<Agent> EnsureSpecialistAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            var agent = await scope.ServiceProvider.GetRequiredService<DiagnoseProvisioner>()
                .EnsureAsync(CancellationToken.None);
            agent.ShouldNotBeNull();
            return agent;
        }

        public async Task<Card> SeedCardAsync(IReadOnlyList<string>? labels = null)
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            await using var db = CreateContext();
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"diagnose-labels-{Guid.NewGuid():N}",
                GitRepositoryUrl = "https://example.test/diagnose.git",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = project.Name,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var column = new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                StateKey = "backlog",
                Name = "Backlog",
                ColumnOrder = 0,
                CardStatus = CardStatus.Backlog,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                BoardColumnId = column.Id,
                Identifier = $"CARD-{Random.Shared.Next(1000, 9999)}",
                Title = "Label this card",
                Description = "A few files in one area; the design is settled.",
                LabelsJson = BoardService.SerializeLabels(labels ?? []),
                Status = CardStatus.Backlog,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AddRange(project, board, column, card);
            await db.SaveChangesAsync();
            return card;
        }

        public async Task SeedDiagnoseCostAsync(decimal costUsd)
        {
            var id = Guid.NewGuid();
            var createdAt = Clock.GetUtcNow().UtcDateTime;
            await using var db = CreateContext();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "budget seed",
                Goal = "budget seed",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Diagnose,
                ModelLevel = AgentModelLevel.Low,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = _scratch,
                Status = AgentTaskStatus.Succeeded,
                CostUsd = costUsd,
                CreatedAt = createdAt,
                CompletedAt = createdAt,
            });
            await db.SaveChangesAsync();
        }

        public async Task<AgentTask> SeedPendingDiagnoseAsync(Guid specialistId, AgentTaskStatus status)
        {
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "labels for CARD-0001",
                Goal = "diagnose this",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Diagnose,
                ReplyTo = AgentTaskReplyTo.None,
                ModelLevel = AgentModelLevel.Low,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = _scratch,
                AgentId = specialistId,
                Ephemeral = false,
                Status = status,
                CreatedAt = Clock.GetUtcNow().UtcDateTime.AddSeconds(-90),
            };
            await using var db = CreateContext();
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        public async Task SeedConflictingRevisionAsync(Guid cardId)
        {
            await using var db = CreateContext();
            var card = await db.Cards.SingleAsync(c => c.Id == cardId);
            db.CardRevisions.Add(new CardRevision
            {
                CardId = card.Id,
                RevisionNumber = card.RevisionCount + 1,
                Kind = CardRevisionKind.ContentEdit,
                CreatedAt = Clock.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync();
        }

        public async Task SetLabelsAsync(Guid cardId, IReadOnlyList<string> labels)
        {
            await using var db = CreateContext();
            var card = await db.Cards.SingleAsync(c => c.Id == cardId);
            card.LabelsJson = BoardService.SerializeLabels(labels);
            await db.SaveChangesAsync();
        }

        public async Task<AgentTask> WaitForDiagnoseRunAsync(Guid specialistId)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                await using var db = CreateContext();
                var row = await db.AgentTasks.AsNoTracking()
                    .Where(t => t.AgentId == specialistId
                        && t.Role == AgentTaskRole.Diagnose
                        && t.Status == AgentTaskStatus.Queued)
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefaultAsync();
                if (row is not null)
                    return row;
                await Task.Delay(25);
            }

            throw new TimeoutException("The diagnose worker never created a run.");
        }

        public async Task PumpClockAsync(Task run)
        {
            for (var spins = 0; !run.IsCompleted && spins < 400; spins++)
            {
                Clock.Advance(TimeSpan.FromSeconds(2));
                await Task.Delay(15);
            }

            if (!run.IsCompleted)
                throw new TimeoutException("RunCardAsync never finished waiting for the diagnose seat.");
            await run;
        }

        public async Task SettleAsync(
            Guid id, string result, decimal costUsd,
            AgentTaskStatus status = AgentTaskStatus.Succeeded)
        {
            await using var db = CreateContext();
            var row = await db.AgentTasks.SingleAsync(t => t.Id == id);
            row.Status = status;
            row.Result = result;
            row.CostUsd = costUsd;
            row.CompletedAt = Clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }

        public async Task<AgentTask> ReloadAsync(Guid taskId)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        }

        public async Task<Card> ReloadCardAsync(Guid cardId)
        {
            await using var db = CreateContext();
            return await db.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId);
        }

        public async Task<List<DiagnosisRecord>> LedgerForCardAsync(Guid cardId)
        {
            await using var db = CreateContext();
            return await db.Diagnoses.AsNoTracking()
                .Where(d => d.CardId == cardId)
                .OrderBy(d => d.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<CardRevision>> RevisionsAsync(Guid cardId)
        {
            await using var db = CreateContext();
            return await db.CardRevisions.AsNoTracking()
                .Where(r => r.CardId == cardId)
                .OrderBy(r => r.RevisionNumber)
                .ToListAsync();
        }

        public async Task<int> DiagnoseRunCountAsync(Guid specialistId)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.CountAsync(
                t => t.AgentId == specialistId && t.Role == AgentTaskRole.Diagnose);
        }

        private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
    }

    private sealed class HeldAvailability : IModelAvailability
    {
        public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct) =>
            Task.FromResult(true);
    }
}
