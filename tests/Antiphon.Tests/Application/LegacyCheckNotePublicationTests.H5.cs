using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class LegacyCheckNotePublicationTests
{
    [Test]
    public async Task Legacy_capture_retry_does_not_probe_again()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var probes = 0;
            var publisher = Publisher(world.Db);
            await publisher.CaptureOnceAsync(world.Subject, 1, world.Dispatched, 1, world.Episode,
                world.Harness.SessionId, world.Generation, world.Harness.SessionId, world.RunId,
                _ => { probes++; return Task.CompletedTask; }, CancellationToken.None);
            await publisher.CaptureOnceAsync(world.Subject, 1, world.Dispatched, 1, world.Episode,
                world.Harness.SessionId, world.Generation, world.Harness.SessionId, world.RunId,
                _ => { probes++; return Task.CompletedTask; }, CancellationToken.None);
            probes.ShouldBe(1);
        }
    }

    [Test]
    public async Task Legacy_capture_rejects_a_changed_execution()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            world.Subject.CheckCount = 2;
            await world.Db.SaveChangesAsync();
            var captured = await Publisher(world.Db).CaptureAsync(world.Subject, 1, world.Dispatched, 1, world.Episode,
                world.Harness.SessionId, world.Generation, world.Harness.SessionId, world.RunId, CancellationToken.None);
            captured.ShouldBeNull();
            (await world.Db.LegacyCheckNotePublications.CountAsync()).ShouldBe(0);
        }
    }

    [Test]
    public async Task Legacy_capture_and_interpretation_link_commit_together()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var publication = await CapturedWithoutRunAsync(world);
            var options = TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString);
            var builder = new DbContextOptionsBuilder<AppDbContext>(options);
            builder.AddInterceptors(new ThrowWhenRunAndPublicationSaveTogether());
            await using var fault = new AppDbContext(builder.Options);
            await Should.ThrowAsync<InvalidOperationException>(() =>
                Publisher(fault).BindInterpretationAsync(publication.Id, CancellationToken.None));
            await using var verify = new AppDbContext(options);
            (await verify.AgentTasks.CountAsync(t => t.Title == "captured check")).ShouldBe(0);
        }
    }

    [Test]
    public async Task Legacy_run_link_ack_loss_adopts_the_original_run()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var publication = await CapturedWithoutRunAsync(world);
            var publisher = Publisher(world.Db);
            await publisher.BindInterpretationAsync(publication.Id, CancellationToken.None);
            await publisher.BindInterpretationAsync(publication.Id, CancellationToken.None);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            var ids = await verify.LegacyCheckNotePublications.Select(p => p.InterpretationTaskId).ToListAsync();
            ids.Distinct().Count().ShouldBe(1);
            (await verify.AgentTasks.CountAsync(t => t.Title == "captured check")).ShouldBe(1);
        }
    }

    [Test]
    public async Task Legacy_missing_run_cannot_bind_a_replacement_generation()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var publication = await CapturedWithoutRunAsync(world);
            await world.Db.AgentSessions.Where(s => s.Id == world.Harness.SessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartedAt, world.Generation.AddMinutes(5)));
            await Publisher(world.Db).BindInterpretationAsync(publication.Id, CancellationToken.None);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            (await verify.AgentTasks.CountAsync(t => t.Title == "captured check")).ShouldBe(0);
        }
    }

    [Test]
    public async Task Legacy_linked_run_recovery_keeps_the_original_deadline()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var publication = await CapturedWithoutRunAsync(world);
            var deadline = publication.InterpretationDeadlineAt;
            await Publisher(world.Db).BindInterpretationAsync(publication.Id, CancellationToken.None);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            (await verify.LegacyCheckNotePublications.SingleAsync()).InterpretationDeadlineAt
                .ShouldBe(deadline, TimeSpan.FromMilliseconds(1));
        }
    }

    [Test]
    public async Task Legacy_expired_capture_never_creates_an_interpreter()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var now = DateTime.UtcNow;
            var atDeadline = await CapturedWithoutRunAsync(world, now);
            var past = await CapturedWithoutRunAsync(world, now.AddMicroseconds(-1), checkNumber: 2);
            var publisher = Publisher(world.Db);
            await publisher.BindInterpretationAsync(atDeadline.Id, CancellationToken.None);
            await publisher.BindInterpretationAsync(past.Id, CancellationToken.None);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            (await verify.AgentTasks.CountAsync(t => t.Title == "captured check")).ShouldBe(0);
        }
    }

    [Test]
    public async Task Legacy_late_result_cannot_upgrade_a_selected_timeout()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var publication = await CapturedWithoutRunAsync(world);
            var publisher = Publisher(world.Db);
            await publisher.SelectOutcomeAsync(publication.Id, "timeout", CancellationToken.None);
            await publisher.SelectOutcomeAsync(publication.Id, "late success", CancellationToken.None);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            (await verify.LegacyCheckNotePublications.SingleAsync()).InterpretationSnapshotJson.ShouldBe("timeout");
        }
    }

    [Test]
    public async Task Legacy_concurrent_finalizers_keep_the_first_selection()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var publication = await CapturedWithoutRunAsync(world);
            var options = TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString);
            await using var first = new AppDbContext(options);
            await using var second = new AppDbContext(options);
            await Publisher(first).SelectOutcomeAsync(publication.Id, "first", CancellationToken.None);
            await Publisher(second).SelectOutcomeAsync(publication.Id, "second", CancellationToken.None);
            await using var verify = new AppDbContext(options);
            (await verify.LegacyCheckNotePublications.SingleAsync()).InterpretationSnapshotJson.ShouldBe("first");
        }
    }

    [Test]
    public async Task Legacy_note_body_event_and_obligation_commit_together()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var publication = await CapturedWithoutRunAsync(world);
            publication.InterpretationTaskId = world.RunId;
            await world.Db.SaveChangesAsync();
            await Should.ThrowAsync<InvalidOperationException>(() =>
                Publisher(world.Db, new ThrowBeforeProduce()).ProduceAsync(
                    publication, "original body line", "event", false, null, CancellationToken.None));
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            (await verify.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.Check)).ShouldBe(0);
            (await verify.AgentTaskLandNotifications.CountAsync()).ShouldBe(0);
        }
    }

    [Test]
    public async Task Legacy_outbox_cannot_commit_without_production()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var publication = await CapturedWithoutRunAsync(world);
            publication.InterpretationTaskId = world.RunId;
            await world.Db.SaveChangesAsync();
            await Should.ThrowAsync<InvalidOperationException>(() =>
                Publisher(world.Db, new ThrowBeforeProduce()).ProduceAsync(
                    publication, "original body line", "event", false, null, CancellationToken.None));
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            (await verify.AgentTaskLandNotifications.CountAsync()).ShouldBe(0);
        }
    }

    [Test]
    public async Task Legacy_conflicting_replay_preserves_the_winner()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            await Publisher(world.Db).TryPublishAsync(
                world.Subject, 1, "winner body line", "event", world.RunId, false, null, CancellationToken.None, world.Episode.Id);
            var publication = await world.Db.LegacyCheckNotePublications.SingleAsync();
            var conflict = await Publisher(world.Db).ConflictingReplayAsync(
                publication.Id, null, checkNumber: 2, attempt: null, dispatchedAt: null, CancellationToken.None);
            conflict.ShouldNotBeNull();
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            (await verify.LegacyCheckNotePublications.SingleAsync()).Body.ShouldBe("winner body line");
        }
    }

    [Test]
    public async Task Legacy_body_digest_covers_the_canonical_body()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var body = "alpha\r\nβ\nomega-distinct";
            await Publisher(world.Db).TryPublishAsync(
                world.Subject, 1, body, "event", world.RunId, false, null, CancellationToken.None, world.Episode.Id);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            var stored = await verify.LegacyCheckNotePublications.SingleAsync();
            stored.ContentDigest.ShouldBe(DelegationNoteDigest.Compute("alpha\nβ\nomega-distinct"));
        }
    }

    [Test]
    public async Task Legacy_disabled_capture_finalizes_without_a_new_run()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var publication = await CapturedWithoutRunAsync(world);
            var settings = Options.Create(new DelegationSettings { Enabled = true, CheckEnabled = false, CheckInterpreterEnabled = true });
            await new LegacyCheckNotePublicationService(world.Db, TimeProvider.System, settings: settings)
                .RecoverCapturedAsync(publication.Id, CancellationToken.None);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            (await verify.AgentTasks.CountAsync(t => t.Title == "captured check")).ShouldBe(0);
            (await verify.LegacyCheckNotePublications.SingleAsync()).State.ShouldBe(LegacyCheckNoteState.Produced);
        }
    }

    [Test]
    public async Task Legacy_suppression_commits_an_event_without_an_obligation()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var result = await Publisher(world.Db).TryPublishAsync(
                world.Subject, 1, "suppressed body", "because settled", world.RunId, true, "settled",
                CancellationToken.None, world.Episode.Id);
            result.ShouldBe(LegacyCheckNotePublicationService.PublishResult.Suppressed);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            (await verify.AgentTaskLandNotifications.CountAsync()).ShouldBe(0);
            (await verify.AgentTaskEvents.CountAsync(e => e.Type == AgentTaskEventType.Check)).ShouldBe(1);
        }
    }

    [Test]
    public async Task Legacy_publication_scope_is_narrow()
    {
        var world = await SeedAsync();
        await using (world.Schema)
        await using (world.Harness)
        {
            var result = await Publisher(world.Db).TryPublishAsync(
                world.Subject, 1, "ordinary legacy body", "event", world.RunId, false, null, CancellationToken.None);
            result.ShouldBe(LegacyCheckNotePublicationService.PublishResult.NotAssociated);
            (await world.Db.LegacyCheckNotePublications.CountAsync()).ShouldBe(0);
        }
    }

    private static LegacyCheckNotePublicationService Publisher(AppDbContext db, CheckCompactionBoundary? boundary = null) =>
        new(db, TimeProvider.System, boundary: boundary);

    private static async Task<LegacyCheckNotePublication> CapturedWithoutRunAsync(World world, DateTime? deadline = null, int checkNumber = 1)
    {
        var publication = new LegacyCheckNotePublication
        {
            Id = Guid.NewGuid(),
            CheckedTaskId = world.Subject.Id,
            CheckedTaskAttempt = 1,
            CheckedTaskDispatchedAt = world.Dispatched,
            CheckNumber = checkNumber,
            RecoveryId = world.Episode.Id,
            PhysicalAgentId = world.Harness.AgentId,
            InterpreterSessionId = world.Harness.SessionId,
            InterpreterAcceptedStartedAt = world.Generation,
            ParentSessionId = world.Harness.SessionId,
            CapturedAt = DateTime.UtcNow,
            FactsSnapshotJson = "{}",
            RenderContextJson = "{}",
            InterpretationDeadlineAt = deadline ?? DateTime.UtcNow.AddMinutes(5),
            State = LegacyCheckNoteState.Captured,
            SourceEventId = Guid.NewGuid(),
            NotificationId = Guid.NewGuid(),
            NextAttemptAt = DateTime.UtcNow,
        };
        world.Db.LegacyCheckNotePublications.Add(publication);
        await world.Db.SaveChangesAsync();
        return publication;
    }

    private static async Task<World> SeedAsync()
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var harness = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString, AlwaysOn = false });
        var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == harness.SessionId);
        var generation = SessionGeneration.Normalize(session.StartedAt);
        var dispatched = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-1));
        var episode = new CheckCompactionRecovery
        {
            Id = Guid.NewGuid(), PhysicalAgentId = harness.AgentId, SessionId = harness.SessionId,
            AcceptedStartedAt = generation, BoundaryIdentity = "boundary-1", BoundaryCreatedAt = dispatched,
            ContinuationCreatedAt = dispatched, ConfiguredThresholdMinutes = 10, DetectedAt = dispatched,
            State = CheckCompactionRecoveryState.AwaitingCheck, ResumeSessionId = harness.SessionId,
            ResumeAcceptedStartedAt = generation,
        };
        var subject = new AgentTask
        {
            Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "subject", Goal = "watch",
            Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Working,
            AgentId = harness.AgentId, AgentSessionId = harness.SessionId, ParentSessionId = harness.SessionId,
            ReplyTo = AgentTaskReplyTo.Session, WorkingDirectory = harness.TempRoot, CreatedAt = dispatched,
            DispatchedAt = dispatched, Attempt = 1, CheckCount = 1,
        };
        subject.RootTaskId = subject.Id;
        var run = new AgentTask
        {
            Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "read", Goal = "interpret",
            Role = AgentTaskRole.Check, Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Succeeded,
            AgentId = harness.AgentId, AgentSessionId = harness.SessionId, WorkingDirectory = harness.TempRoot,
            CreatedAt = dispatched, Result = "useful reading",
        };
        run.RootTaskId = run.Id;
        db.CheckCompactionRecoveries.Add(episode);
        db.AgentTasks.AddRange(subject, run);
        await db.SaveChangesAsync();
        return new World(schema, harness, db, episode, subject, run.Id, generation, dispatched);
    }

    private sealed record World(
        IsolatedTestSchema Schema,
        BridgeQueueHarness Harness,
        AppDbContext Db,
        CheckCompactionRecovery Episode,
        AgentTask Subject,
        Guid RunId,
        DateTime Generation,
        DateTime Dispatched);

    private sealed class ThrowBeforeProduce : CheckCompactionBoundary
    {
        public override Task ReachedAsync(string boundary, Guid operationId, CancellationToken ct)
        {
            if (boundary == "legacy-before-produce-commit")
                throw new InvalidOperationException("produce commit fault");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowWhenRunAndPublicationSaveTogether : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var tracker = eventData.Context?.ChangeTracker;
            if (tracker?.Entries<AgentTask>().Any(entry => entry.State == EntityState.Added) == true
                && tracker.Entries<LegacyCheckNotePublication>().Any(entry => entry.State == EntityState.Modified))
                throw new InvalidOperationException("run link fault");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
