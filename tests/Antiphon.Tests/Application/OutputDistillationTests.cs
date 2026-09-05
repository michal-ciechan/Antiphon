using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0330 S3 — Shadow/Apply pipeline, gates, skip reasons, no recursion.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class OutputDistillationTests
{
    [Test]
    public async Task shadow_mode_records_without_replacing_the_note()
    {
        using var h = new Harness(s => s.OutputDistillerMode = OutputDistillerMode.Shadow);
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync();

        var run = Task.Run(() => h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None));
        var distill = await h.WaitForDistillAsync(specialist.Id);
        await h.SettleDistillAsync(distill.Id, h.PassingDistillation());
        await h.PumpClockAsync(run);
        await run;

        var queued = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        queued.Body.ShouldBe(seed.RawBody);
        queued.HoldUntil.ShouldBeNull();
        queued.ContentDigest.ShouldBe(seed.Digest);
        queued.NoteHeader.ShouldBe(seed.Header);

        var source = await h.ReloadTaskAsync(seed.Task.Id);
        source.DistilledResult.ShouldNotBeNullOrWhiteSpace();
        source.Result.ShouldBe(seed.Report);

        var ledger = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DistillationOutcome.Shadowed);
        ledger.Mode.ShouldBe(OutputDistillerMode.Shadow);
    }

    [Test]
    public async Task apply_mode_replaces_a_held_pending_body_and_keeps_header_and_digest()
    {
        using var h = new Harness(s => s.OutputDistillerMode = OutputDistillerMode.Apply);
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync(holdUntil: DateTime.UtcNow.AddMinutes(5));

        var run = Task.Run(() => h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None));
        var distill = await h.WaitForDistillAsync(specialist.Id);
        var distilled = h.PassingDistillation();
        await h.SettleDistillAsync(distill.Id, distilled);
        await h.PumpClockAsync(run);
        await run;

        var queued = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        queued.HoldUntil.ShouldBeNull();
        queued.ContentDigest.ShouldBe(seed.Digest);
        queued.NoteHeader.ShouldBe(seed.Header);
        queued.Body.ShouldStartWith(seed.Header);
        queued.Body.ShouldContain(distilled.Trim());
        queued.Body.ShouldContain("Full report:");
        queued.Body.ShouldNotBe(seed.RawBody);

        var ledger = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DistillationOutcome.Applied);
    }

    [Test]
    public async Task a_row_already_delivered_records_applied_late()
    {
        using var h = new Harness(s => s.OutputDistillerMode = OutputDistillerMode.Apply);
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync();
        await h.MarkQueuedSentAsync(seed.QueuedMessageId);

        var run = Task.Run(() => h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None));
        var distill = await h.WaitForDistillAsync(specialist.Id);
        await h.SettleDistillAsync(distill.Id, h.PassingDistillation());
        await h.PumpClockAsync(run);
        await run;

        var queued = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        queued.Body.ShouldBe(seed.RawBody);
        (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem()
            .Outcome.ShouldBe(DistillationOutcome.AppliedLate);
    }

    [Test]
    public async Task a_rejected_distillation_delivers_the_raw_body_and_records_missing_anchors()
    {
        using var h = new Harness(s => s.OutputDistillerMode = OutputDistillerMode.Apply);
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync(holdUntil: DateTime.UtcNow.AddMinutes(5));

        var run = Task.Run(() => h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None));
        var distill = await h.WaitForDistillAsync(specialist.Id);
        await h.SettleDistillAsync(distill.Id, Pad("omitted every identifier on purpose"));
        await h.PumpClockAsync(run);
        await run;

        var queued = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        queued.Body.ShouldBe(seed.RawBody);
        queued.HoldUntil.ShouldBeNull();
        var ledger = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DistillationOutcome.RejectedOverCompressed);
        ledger.MissingAnchors.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task timeout_cancels_a_still_queued_distill_task_and_clears_the_hold()
    {
        using var h = new Harness(s =>
        {
            s.OutputDistillerMode = OutputDistillerMode.Apply;
            s.OutputDistillerWaitSeconds = 1;
        });
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync(holdUntil: DateTime.UtcNow.AddMinutes(5));

        var run = Task.Run(() => h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None));
        var distill = await h.WaitForDistillAsync(specialist.Id);
        await h.PumpClockAsync(run);
        await run;

        var distillRow = await h.ReloadTaskAsync(distill.Id);
        distillRow.Status.ShouldBe(AgentTaskStatus.Canceled);
        (await h.ReloadQueuedAsync(seed.QueuedMessageId)).HoldUntil.ShouldBeNull();
        (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem()
            .Outcome.ShouldBe(DistillationOutcome.DegradedTimeout);
        var incidents = await h.IncidentsAsync(specialist.Id);
        incidents.ShouldNotBeEmpty();
    }

    [Test]
    public async Task backlog_at_the_cap_degrades_without_creating()
    {
        using var h = new Harness(s => s.OutputDistillerMaxBacklog = 1);
        var specialist = await h.EnsureSpecialistAsync();
        await h.SeedQueuedDistillAsync(specialist.Id);
        var seed = await h.SeedSourceAsync();

        await h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None);

        (await h.DistillCountAsync(specialist.Id)).ShouldBe(1, "the cap forbids a second Distill row");
        (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem()
            .Outcome.ShouldBe(DistillationOutcome.DegradedBusy);
    }

    [Test]
    public async Task disabled_writes_no_ledger_row()
    {
        using var h = new Harness(s => s.OutputDistillerEnabled = false);
        var seed = await h.SeedSourceAsync();

        await h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None);

        (await h.LedgerAsync(seed.Task.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task short_and_long_reports_are_skipped_with_reasons()
    {
        using var h = new Harness();
        var shortSeed = await h.SeedSourceAsync(report: new string('a', 200));
        await h.Distiller.RequestAsync(shortSeed.Task.Id, shortSeed.QueuedMessageId, CancellationToken.None);
        (await h.LedgerAsync(shortSeed.Task.Id)).ShouldHaveSingleItem()
            .Outcome.ShouldBe(DistillationOutcome.SkippedShort);

        var longSeed = await h.SeedSourceAsync(report: new string('b', 21_000));
        await h.Distiller.RequestAsync(longSeed.Task.Id, longSeed.QueuedMessageId, CancellationToken.None);
        (await h.LedgerAsync(longSeed.Task.Id)).ShouldHaveSingleItem()
            .Outcome.ShouldBe(DistillationOutcome.SkippedLong);
    }

    [Test]
    public async Task a_blocked_task_is_never_requested()
    {
        var task = new AgentTask
        {
            Status = AgentTaskStatus.Blocked,
            ReplyTo = AgentTaskReplyTo.Session,
            Role = AgentTaskRole.Code,
        };
        OutputDistillationService.ShouldRequest(task, new DelegationSettings()).ShouldBeFalse();
    }

    [Test]
    public async Task a_specialist_row_is_never_requested()
    {
        var task = new AgentTask
        {
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.Session,
            Role = AgentTaskRole.Distill,
        };
        OutputDistillationService.ShouldRequest(task, new DelegationSettings()).ShouldBeFalse();
    }

    [Test]
    public async Task the_unavailable_incident_dedups_per_minute()
    {
        using var h = new Harness(s => s.OutputDistillerWaitSeconds = 1);
        var specialist = await h.EnsureSpecialistAsync();
        var first = await h.SeedSourceAsync();
        var second = await h.SeedSourceAsync();

        var run1 = Task.Run(() => h.Distiller.RequestAsync(first.Task.Id, first.QueuedMessageId, CancellationToken.None));
        await h.WaitForDistillAsync(specialist.Id);
        await h.PumpClockAsync(run1);
        await run1;

        var run2 = Task.Run(() => h.Distiller.RequestAsync(second.Task.Id, second.QueuedMessageId, CancellationToken.None));
        await h.WaitForDistillAsync(specialist.Id);
        await h.PumpClockAsync(run2);
        await run2;

        (await h.IncidentsAsync(specialist.Id)).Count.ShouldBe(1);
    }

    private static string Pad(string text)
    {
        if (text.Length >= 120)
            return text;
        return text + "\n- " + new string('x', 120 - text.Length);
    }

    private sealed record Seeded(
        AgentTask Task,
        Guid QueuedMessageId,
        string Report,
        string RawBody,
        string Header,
        string Digest);

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly string _scratch;
        private readonly DelegationSettings _settings;

        public Harness(Action<DelegationSettings>? configure = null)
        {
            _scratch = Directory.CreateTempSubdirectory("antiphon-distiller-wire").FullName;
            SpecialistSlug = $"distiller-{Guid.NewGuid():N}"[..24];
            _settings = new DelegationSettings
            {
                MaxConcurrentTasks = 512,
                OutputDistillerAgentSlug = SpecialistSlug,
                OutputDistillerWorkingDirectory = _scratch,
                OutputDistillerEnabled = true,
                OutputDistillerMode = OutputDistillerMode.Shadow,
                OutputDistillerWaitSeconds = 45,
                OutputDistillerMaxBacklog = 3,
                DistillMinChars = 1_200,
                DistillMaxRawChars = 20_000,
                DistilledMaxChars = 1_500,
                DistilledMaxRatio = 0.6,
                RolePolicy = new(StringComparer.OrdinalIgnoreCase),
            };
            configure?.Invoke(_settings);

            Clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            StartedAt = Clock.GetUtcNow().UtcDateTime;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(Options.Create(_settings));
            services.AddScoped<OutputDistillerProvisioner>();
            services.AddScoped<IAlertService, AlertService>();
            services.AddScoped<IAlertRouter, NullAlertRouter>();
            services.AddScoped<OutputDistillationService>();

            _provider = services.BuildServiceProvider();
            Distiller = _provider.CreateScope().ServiceProvider.GetRequiredService<OutputDistillationService>();
        }

        public FakeTimeProvider Clock { get; }
        public DateTime StartedAt { get; }
        public string SpecialistSlug { get; }
        public OutputDistillationService Distiller { get; }

        public void Dispose()
        {
            _provider.Dispose();
            try { Directory.Delete(_scratch, recursive: true); }
            catch (IOException) { }
        }

        public async Task<Agent> EnsureSpecialistAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            var agent = await scope.ServiceProvider.GetRequiredService<OutputDistillerProvisioner>()
                .EnsureAsync(CancellationToken.None);
            agent.ShouldNotBeNull();
            return agent;
        }

        public string PassingDistillation() => Pad(
            "- Landed CARD-0330 at a1b2c3d4e5f6789. See https://example.com/x. Cost $12.50. 3 failed. "
            + "Path server/Application/Services/Foo.cs:40 [[attach: docs/tmp/f.md]] docs/tmp/f.md");

        public async Task<Seeded> SeedSourceAsync(
            string? report = null, DateTime? holdUntil = null)
        {
            var body = report ?? LongReport();
            var id = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var queuedId = Guid.NewGuid();
            var header = $"[task {DelegationReportFormatter.Short(id)} done] seeded";
            var rawBody = header + "\n\n" + body;
            var digest = DelegationNoteDigest.Compute(body);
            var now = DateTime.UtcNow;

            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                ParentSessionId = sessionId,
                ReplyTo = AgentTaskReplyTo.Session,
                Title = "distill source",
                Goal = "do the thing",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentTaskStatus.Succeeded,
                Result = body,
                CreatedAt = now,
                CompletedAt = now,
            };
            db.AgentTasks.Add(task);
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = queuedId,
                AgentSessionId = sessionId,
                Body = rawBody,
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Delegation,
                SourceTaskId = id,
                ContentDigest = digest,
                NoteHeader = header,
                HoldUntil = holdUntil,
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
            return new Seeded(task, queuedId, body, rawBody, header, digest);
        }

        public async Task SeedQueuedDistillAsync(Guid specialistId)
        {
            await using var db = CreateContext();
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "queued distill",
                Goal = "g",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Distill,
                ModelLevel = AgentModelLevel.Low,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = _scratch,
                AgentId = specialistId,
                Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task MarkQueuedSentAsync(Guid queuedId)
        {
            await using var db = CreateContext();
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == queuedId);
            row.Status = QueuedMessageStatus.Sent;
            row.SentAt = DateTime.UtcNow;
            row.DeliveryAttempts = 1;
            await db.SaveChangesAsync();
        }

        public async Task<AgentTask> WaitForDistillAsync(Guid specialistId)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                await using var db = CreateContext();
                var row = await db.AgentTasks.AsNoTracking()
                    .Where(t => t.AgentId == specialistId
                        && t.Role == AgentTaskRole.Distill
                        && t.CreatedAt >= StartedAt
                        && t.Status == AgentTaskStatus.Queued)
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefaultAsync();
                if (row is not null)
                    return row;
                await Task.Delay(25);
            }

            throw new TimeoutException("The distiller never created a Distill task.");
        }

        public async Task PumpClockAsync(Task run)
        {
            for (var spins = 0; !run.IsCompleted && spins < 400; spins++)
            {
                Clock.Advance(TimeSpan.FromSeconds(2));
                await Task.Delay(15);
            }

            if (!run.IsCompleted)
                throw new TimeoutException("RequestAsync never finished waiting for its distillation.");
        }

        public async Task SettleDistillAsync(Guid id, string result)
        {
            await using var db = CreateContext();
            var row = await db.AgentTasks.SingleAsync(t => t.Id == id);
            row.Status = AgentTaskStatus.Succeeded;
            row.Result = result;
            row.CostUsd = 0.0031m;
            row.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task<AgentTask> ReloadTaskAsync(Guid id)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        }

        public async Task<SessionQueuedMessage> ReloadQueuedAsync(Guid id)
        {
            await using var db = CreateContext();
            return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        }

        public async Task<List<OutputDistillationRecord>> LedgerAsync(Guid taskId)
        {
            await using var db = CreateContext();
            return await db.OutputDistillations.AsNoTracking()
                .Where(d => d.TaskId == taskId)
                .OrderBy(d => d.CreatedAt)
                .ToListAsync();
        }

        public async Task<int> DistillCountAsync(Guid specialistId)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.CountAsync(
                t => t.AgentId == specialistId && t.Role == AgentTaskRole.Distill);
        }

        public async Task<List<AgentIncident>> IncidentsAsync(Guid specialistId)
        {
            await using var db = CreateContext();
            return await db.AgentIncidents.AsNoTracking()
                .Where(i => i.AgentId == specialistId
                    && i.Kind == AgentIncidentKind.OutputDistillerUnavailable
                    && i.CreatedAt >= StartedAt)
                .OrderBy(i => i.CreatedAt)
                .ToListAsync();
        }

        private static string LongReport()
        {
            var core = """
                Landed CARD-0330 at a1b2c3d4e5f6789. See https://example.com/x. Cost $12.50. 3 failed.
                Path server/Application/Services/Foo.cs:40. [[attach: docs/tmp/f.md]] docs/tmp/f.md
                """;
            return core + "\n" + new string('y', 1_400);
        }

        private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
    }
}
