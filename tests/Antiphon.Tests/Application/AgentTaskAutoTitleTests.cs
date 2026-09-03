using Antiphon.Server.Application.Dtos;
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
/// CARD-0352 S3 — auto-title: create queues a request when no Title was sent and the Goal
/// fallback is longer than 80 chars; a good diagnose answer replaces the title in place.
/// </summary>
[Category("Integration")]
[NotInParallel("DiagnoseTitle")]
public class AgentTaskAutoTitleTests
{
    [Test]
    public async Task a_long_untitled_goal_is_queued_and_keeps_the_fallback_title()
    {
        using var h = new Harness();
        var goal = new string('x', 90);

        var created = await h.CreateAsync(goal);

        created.TitleDiagnosisQueued.ShouldBeTrue();
        var row = await h.ReloadAsync(created.Id);
        row.Title.ShouldBe(AgentTaskService.FallbackTitle(goal));
        row.Title.Length.ShouldBe(90);
        h.Dequeue().TaskId.ShouldBe(created.Id);
    }

    [Test]
    public async Task a_short_untitled_goal_is_not_queued()
    {
        using var h = new Harness();
        var created = await h.CreateAsync(new string('x', 40));

        created.TitleDiagnosisQueued.ShouldBeFalse();
        h.Queue.TryDequeue(out _).ShouldBeFalse();
    }

    [Test]
    public async Task an_explicit_Title_is_not_queued()
    {
        using var h = new Harness();
        var created = await h.CreateAsync(
            new string('x', 90),
            title: "short title");

        created.TitleDiagnosisQueued.ShouldBeFalse();
        h.Queue.TryDequeue(out _).ShouldBeFalse();
        (await h.ReloadAsync(created.Id)).Title.ShouldBe("short title");
    }

    [Test]
    [Arguments(AgentTaskRole.Check)]
    [Arguments(AgentTaskRole.Diagnose)]
    public async Task a_specialist_row_is_never_queued(AgentTaskRole role)
    {
        using var h = new Harness();
        var created = await h.CreateAsync(new string('x', 90), role: role);

        created.TitleDiagnosisQueued.ShouldBeFalse();
        h.Queue.TryDequeue(out _).ShouldBeFalse();
    }

    [Test]
    public async Task DiagnoseEnabled_false_is_byte_identical_to_today()
    {
        using var h = new Harness(s => s.DiagnoseEnabled = false);
        var created = await h.CreateAsync(new string('x', 90));

        created.TitleDiagnosisQueued.ShouldBeFalse();
        h.Queue.TryDequeue(out _).ShouldBeFalse();
        (await h.LedgerForAsync(created.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task a_good_answer_replaces_the_title_and_writes_Diagnosed()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var task = await h.SeedUntitledAsync();
        const string answer = "Plan haiku diagnose seat";

        var run = Task.Run(() => h.Diagnose.RunTitleAsync(task.Id, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, answer, 0.0087m);
        await h.PumpClockAsync(run);

        (await h.ReloadAsync(task.Id)).Title.ShouldBe(answer);
        var diagnosed = (await h.EventsAsync(task.Id, AgentTaskEventType.Diagnosed)).ShouldHaveSingleItem();
        diagnosed.Detail.ShouldContain($"diagnose task {DelegationReportFormatter.Short(diagnoseTask.Id)}");
        diagnosed.Detail.ShouldContain("$0.0087");
        h.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "AgentTaskChanged");
        var ledger = (await h.LedgerForAsync(task.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DiagnosisOutcome.Applied);
        ledger.Applied.ShouldBe(answer);
        ledger.DiagnoseTaskId.ShouldBe(diagnoseTask.Id);
        ledger.BundleStamp.ShouldBe(DiagnoseService.BundleStamp());
    }

    [Test]
    public async Task a_card_bound_task_is_prefixed_when_the_answer_omits_the_identifier()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var card = await h.SeedCardAsync("CARD-0352");
        var task = await h.SeedUntitledAsync(cardId: card.Id);

        var run = Task.Run(() => h.Diagnose.RunTitleAsync(task.Id, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, "Plan the diagnose seat", 0.001m);
        await h.PumpClockAsync(run);

        (await h.ReloadAsync(task.Id)).Title.ShouldBe("CARD-0352 Plan the diagnose seat");
        (await h.LedgerForAsync(task.Id)).ShouldHaveSingleItem().Outcome.ShouldBe(DiagnosisOutcome.Applied);
    }

    [Test]
    [Arguments("one\ntwo\nthree", "3 lines")]
    [Arguments("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "91 chars")]
    [Arguments("Hello", "1 words")]
    [Arguments("fix [antiphon-task: abc] now", "contains marker")]
    public async Task a_rejected_answer_leaves_the_title_and_ledgers_RejectedGate(
        string answer, string reason)
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var task = await h.SeedUntitledAsync();

        var run = Task.Run(() => h.Diagnose.RunTitleAsync(task.Id, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, answer, 0.001m);
        await h.PumpClockAsync(run);

        (await h.ReloadAsync(task.Id)).Title.ShouldBe(task.Title);
        var ledger = (await h.LedgerForAsync(task.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DiagnosisOutcome.RejectedGate);
        ledger.Reason.ShouldBe(reason);
        (await h.EventsAsync(task.Id, AgentTaskEventType.Diagnosed)).ShouldBeEmpty();
    }

    [Test]
    public async Task an_answer_equal_to_the_fallback_is_RejectedGate()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var task = await h.SeedUntitledAsync();

        var run = Task.Run(() => h.Diagnose.RunTitleAsync(task.Id, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, task.Title, 0.001m);
        await h.PumpClockAsync(run);

        (await h.ReloadAsync(task.Id)).Title.ShouldBe(task.Title);
        (await h.LedgerForAsync(task.Id)).ShouldHaveSingleItem().Reason.ShouldBe("equals fallback");
    }

    [Test]
    public async Task a_timeout_cancels_a_queued_run_and_ledgers_DegradedTimeout()
    {
        using var h = new Harness(s => s.DiagnoseWaitSeconds = 4);
        var specialist = await h.EnsureSpecialistAsync();
        var task = await h.SeedUntitledAsync();

        var run = Task.Run(() => h.Diagnose.RunTitleAsync(task.Id, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.PumpClockAsync(run);

        (await h.ReloadAsync(task.Id)).Title.ShouldBe(task.Title);
        (await h.ReloadAsync(diagnoseTask.Id)).Status.ShouldBe(AgentTaskStatus.Canceled);
        (await h.LedgerForAsync(task.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.DegradedTimeout);
    }

    [Test]
    public async Task a_failed_run_ledgers_DegradedFailed()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var task = await h.SeedUntitledAsync();

        var run = Task.Run(() => h.Diagnose.RunTitleAsync(task.Id, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, "Plan haiku diagnose seat", 0.001m, AgentTaskStatus.Failed);
        await h.PumpClockAsync(run);

        (await h.ReloadAsync(task.Id)).Title.ShouldBe(task.Title);
        (await h.LedgerForAsync(task.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.DegradedFailed);
    }

    [Test]
    public async Task an_empty_result_ledgers_DegradedEmpty()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var task = await h.SeedUntitledAsync();

        var run = Task.Run(() => h.Diagnose.RunTitleAsync(task.Id, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.SettleAsync(diagnoseTask.Id, "   ", 0.001m);
        await h.PumpClockAsync(run);

        (await h.ReloadAsync(task.Id)).Title.ShouldBe(task.Title);
        (await h.LedgerForAsync(task.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.DegradedEmpty);
    }

    [Test]
    public async Task a_held_alias_creates_no_seat_row_and_ledgers_DegradedHeld()
    {
        using var h = new Harness(held: true);
        var specialist = await h.EnsureSpecialistAsync();
        var task = await h.SeedUntitledAsync();

        await h.Diagnose.RunTitleAsync(task.Id, CancellationToken.None);

        (await h.ReloadAsync(task.Id)).Title.ShouldBe(task.Title);
        (await h.DiagnoseRunCountAsync(specialist.Id)).ShouldBe(0);
        (await h.LedgerForAsync(task.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.DegradedHeld);
    }

    [Test]
    public async Task a_spent_budget_creates_no_seat_row_and_ledgers_DegradedBudget()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        await h.SeedDiagnoseCostAsync(2.00m);
        var task = await h.SeedUntitledAsync();

        await h.Diagnose.RunTitleAsync(task.Id, CancellationToken.None);

        (await h.ReloadAsync(task.Id)).Title.ShouldBe(task.Title);
        (await h.DiagnoseRunCountAsync(specialist.Id)).ShouldBe(0);
        (await h.LedgerForAsync(task.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.DegradedBudget);
    }

    [Test]
    public async Task a_full_backlog_ledgers_DegradedBusy()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        await h.SeedPendingDiagnoseAsync(specialist.Id, AgentTaskStatus.Queued);
        await h.SeedPendingDiagnoseAsync(specialist.Id, AgentTaskStatus.Working);
        var task = await h.SeedUntitledAsync();

        await h.Diagnose.RunTitleAsync(task.Id, CancellationToken.None);

        (await h.ReloadAsync(task.Id)).Title.ShouldBe(task.Title);
        (await h.LedgerForAsync(task.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.DegradedBusy);
        (await h.DiagnoseRunCountAsync(specialist.Id)).ShouldBe(2);
    }

    [Test]
    public async Task a_title_changed_before_apply_is_SkippedAlreadyTitled()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var task = await h.SeedUntitledAsync();

        var run = Task.Run(() => h.Diagnose.RunTitleAsync(task.Id, CancellationToken.None));
        var diagnoseTask = await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.RenameAsync(task.Id, "human set this");
        await h.SettleAsync(diagnoseTask.Id, "Plan haiku diagnose seat", 0.001m);
        await h.PumpClockAsync(run);

        (await h.ReloadAsync(task.Id)).Title.ShouldBe("human set this");
        (await h.LedgerForAsync(task.Id)).ShouldHaveSingleItem().Outcome
            .ShouldBe(DiagnosisOutcome.SkippedAlreadyTitled);
        (await h.EventsAsync(task.Id, AgentTaskEventType.Diagnosed)).ShouldBeEmpty();
    }

    [Test]
    public async Task the_unavailable_incident_dedups_per_minute()
    {
        using var h = new Harness(s => s.DiagnoseWaitSeconds = 4);
        var specialist = await h.EnsureSpecialistAsync();

        var first = await h.SeedUntitledAsync();
        var run1 = Task.Run(() => h.Diagnose.RunTitleAsync(first.Id, CancellationToken.None));
        await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.PumpClockAsync(run1);
        (await h.IncidentsAsync(specialist.Id)).Count.ShouldBe(1);

        var second = await h.SeedUntitledAsync();
        var run2 = Task.Run(() => h.Diagnose.RunTitleAsync(second.Id, CancellationToken.None));
        await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.PumpClockAsync(run2);
        (await h.IncidentsAsync(specialist.Id)).Count.ShouldBe(1,
            "two timeouts in the same minute are one outage");

        h.Clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        var third = await h.SeedUntitledAsync();
        var run3 = Task.Run(() => h.Diagnose.RunTitleAsync(third.Id, CancellationToken.None));
        await h.WaitForDiagnoseRunAsync(specialist.Id);
        await h.PumpClockAsync(run3);
        (await h.IncidentsAsync(specialist.Id)).Count.ShouldBe(2);
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly string _scratch;
        private readonly DelegationSettings _settings;

        public Harness(
            Action<DelegationSettings>? configure = null,
            bool held = false,
            DateTimeOffset? now = null)
        {
            _scratch = Directory.CreateTempSubdirectory("antiphon-diagnose-title").FullName;
            SpecialistSlug = $"diagnose-{Guid.NewGuid():N}"[..24];
            _settings = new DelegationSettings
            {
                MaxConcurrentTasks = 512,
                DiagnoseAgentSlug = SpecialistSlug,
                DiagnoseWorkingDirectory = _scratch,
                DiagnoseEnabled = true,
                DiagnoseTitleEnabled = true,
                DiagnoseWaitSeconds = 30,
                DiagnoseMaxBacklog = 2,
                DiagnoseDailyBudgetUsd = 2.00m,
                DiagnoseTitleMinFallbackChars = 80,
                RolePolicy = new(StringComparer.OrdinalIgnoreCase),
            };
            configure?.Invoke(_settings);

            // One UTC day per harness so SUM(Diagnose CostUsd) today cannot leak into a sibling test.
            Clock = new FakeTimeProvider(
                now ?? new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.Zero)
                    .AddDays(Random.Shared.Next(0, 20_000)));
            EventBus = new MockEventBus();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<IEventBus>(EventBus);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(Options.Create(_settings));
            services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddSingleton<DiagnoseQueue>();
            services.AddScoped<AgentTaskService>();
            services.AddScoped<DiagnoseProvisioner>();
            services.AddScoped<IAlertService, AlertService>();
            services.AddScoped<IAlertRouter, NullAlertRouter>();
            if (held)
                services.AddSingleton<IModelAvailability>(new HeldAvailability());
            services.AddScoped<DiagnoseService>();

            _provider = services.BuildServiceProvider();
            Tasks = _provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskService>();
            Diagnose = _provider.CreateScope().ServiceProvider.GetRequiredService<DiagnoseService>();
            Queue = _provider.GetRequiredService<DiagnoseQueue>();
        }

        public FakeTimeProvider Clock { get; }
        public MockEventBus EventBus { get; }
        public string SpecialistSlug { get; }
        public AgentTaskService Tasks { get; }
        public DiagnoseService Diagnose { get; }
        public DiagnoseQueue Queue { get; }

        public void Dispose()
        {
            _provider.Dispose();
            try { Directory.Delete(_scratch, recursive: true); }
            catch (IOException) { }
        }

        public Task<AgentTaskCreatedDto> CreateAsync(
            string goal, string? title = null, AgentTaskRole role = AgentTaskRole.Code) =>
            Tasks.CreateAsync(
                new CreateAgentTaskRequest(Goal: goal, Title: title, Role: role),
                new AgentTaskService.Caller(null, null, _scratch),
                CancellationToken.None);

        public DiagnoseRequest Dequeue()
        {
            Queue.TryDequeue(out var request).ShouldBeTrue();
            return request;
        }

        public async Task<Agent> EnsureSpecialistAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            var agent = await scope.ServiceProvider.GetRequiredService<DiagnoseProvisioner>()
                .EnsureAsync(CancellationToken.None);
            agent.ShouldNotBeNull();
            return agent;
        }

        public async Task<AgentTask> SeedUntitledAsync(Guid? cardId = null)
        {
            var goal = new string('x', 90);
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = AgentTaskService.FallbackTitle(goal),
                Goal = goal,
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                CardId = cardId,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = _scratch,
                Status = AgentTaskStatus.Queued,
                CreatedAt = Clock.GetUtcNow().UtcDateTime,
            };
            await using var db = CreateContext();
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        public async Task<Card> SeedCardAsync(string identifier)
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            await using var db = CreateContext();
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"diagnose-title-{Guid.NewGuid():N}",
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
                Identifier = identifier,
                Title = identifier,
                Description = "Diagnose title test.",
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
                Title = "title for task deadbeef",
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
                throw new TimeoutException("RunTitleAsync never finished waiting for the diagnose seat.");
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

        public async Task RenameAsync(Guid id, string title)
        {
            await using var db = CreateContext();
            var row = await db.AgentTasks.SingleAsync(t => t.Id == id);
            row.Title = title;
            await db.SaveChangesAsync();
        }

        public async Task<AgentTask> ReloadAsync(Guid taskId)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        }

        public async Task<List<DiagnosisRecord>> LedgerForAsync(Guid taskId)
        {
            await using var db = CreateContext();
            return await db.Diagnoses.AsNoTracking()
                .Where(d => d.TaskId == taskId)
                .OrderBy(d => d.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<AgentTaskEvent>> EventsAsync(Guid taskId, AgentTaskEventType type)
        {
            await using var db = CreateContext();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == taskId && e.Type == type)
                .ToListAsync();
        }

        public async Task<int> DiagnoseRunCountAsync(Guid specialistId)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.CountAsync(
                t => t.AgentId == specialistId && t.Role == AgentTaskRole.Diagnose);
        }

        public async Task<List<AgentIncident>> IncidentsAsync(Guid specialistId)
        {
            await using var db = CreateContext();
            return await db.AgentIncidents.AsNoTracking()
                .Where(i => i.AgentId == specialistId
                    && i.Kind == AgentIncidentKind.DiagnoseUnavailable)
                .OrderBy(i => i.CreatedAt)
                .ToListAsync();
        }

        private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
    }

    private sealed class HeldAvailability : IModelAvailability
    {
        public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct) =>
            Task.FromResult(true);
    }
}
