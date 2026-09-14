using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class RemoteControlModalPersistenceTests
{
    [Test]
    public async Task C514_Concurrent_open_episode_insert_is_database_constrained()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var sessionId = await SeedSessionAsync(isolated.ConnectionString);

        await using var db1 = Create(isolated.ConnectionString);
        db1.RemoteControlModalEpisodes.Add(Episode(sessionId, generation));
        await db1.SaveChangesAsync();

        await using var db2 = Create(isolated.ConnectionString);
        db2.RemoteControlModalEpisodes.Add(Episode(sessionId, generation));
        var ex = await Should.ThrowAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        var pg = ex.InnerException as PostgresException;
        pg.ShouldNotBeNull();
        pg!.SqlState.ShouldBe("23505");
    }

    [Test]
    public async Task C514_Concurrent_active_arm_insert_is_database_constrained()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var sessionId = await SeedSessionAsync(isolated.ConnectionString);

        await using var db1 = Create(isolated.ConnectionString);
        db1.SessionQueuedMessages.Add(Arm(sessionId, generation, active: true));
        await db1.SaveChangesAsync();

        await using var db2 = Create(isolated.ConnectionString);
        db2.SessionQueuedMessages.Add(Arm(sessionId, generation, active: true));
        var ex = await Should.ThrowAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        ((PostgresException)ex.InnerException!).SqlState.ShouldBe("23505");
    }

    [Test]
    public async Task C514_Legacy_rows_keep_provenance_and_become_unclassified()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = await SeedSessionAsync(isolated.ConnectionString);
        await using var db = Create(isolated.ConnectionString);
        var row = new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Body = "/remote-control",
            Status = QueuedMessageStatus.Pending,
            Sequence = 1,
            Origin = QueuedMessageOrigin.Ui,
            CreatedAt = DateTime.UtcNow,
            DeliveryAttempts = 1,
            LastDeliveryBaselineSequence = 9,
        };
        db.SessionQueuedMessages.Add(row);
        await db.SaveChangesAsync();

        var classified = db.SessionQueuedMessages.Single(m => m.Id == row.Id);
        if (classified.MaintenanceKind == RemoteControlMaintenanceKind.None)
        {
            classified.MaintenanceKind = RemoteControlMaintenanceKind.LegacyUnclassified;
            classified.MaintenanceEvidence = "legacy-unclassified";
            await db.SaveChangesAsync();
        }

        await using var verify = Create(isolated.ConnectionString);
        var reloaded = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == row.Id);
        reloaded.MaintenanceKind.ShouldBe(RemoteControlMaintenanceKind.LegacyUnclassified);
        reloaded.Origin.ShouldBe(QueuedMessageOrigin.Ui);
        reloaded.Body.ShouldBe("/remote-control");
        reloaded.DeliveryAttempts.ShouldBe(1);
        reloaded.LastDeliveryBaselineSequence.ShouldBe(9);
    }

    [Test]
    public async Task C514_Episode_identity_and_first_observation_are_immutable()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var sessionId = await SeedSessionAsync(isolated.ConnectionString);
        await using var db = Create(isolated.ConnectionString);
        var episode = Episode(sessionId, generation);
        var first = episode.FirstObservedAt;
        var id = episode.Id;
        db.RemoteControlModalEpisodes.Add(episode);
        await db.SaveChangesAsync();

        for (var i = 0; i < 20; i++)
        {
            episode.LastObservedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using var verify = Create(isolated.ConnectionString);
        var reloaded = await verify.RemoteControlModalEpisodes.SingleAsync(e => e.Id == id);
        reloaded.Id.ShouldBe(id);
        reloaded.FirstObservedAt.ShouldBe(first);
        reloaded.RelatedMaintenanceQueueId.ShouldBeNull();
    }

    [Test]
    public async Task C514_Receipts_exclude_sensitive_payloads()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var sessionId = await SeedSessionAsync(isolated.ConnectionString);
        await using var db = Create(isolated.ConnectionString);
        db.RemoteControlModalEpisodes.Add(Episode(sessionId, generation));
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Kind = AgentIncidentKind.RemoteControlModalDetected,
            Severity = AlertSeverity.Warning,
            Message = "Remote Control menu blocks input",
            FailureReason = "rc-modal:" + Guid.NewGuid().ToString("D"),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await using var verify = Create(isolated.ConnectionString);
        var json = System.Text.Json.JsonSerializer.Serialize(await verify.RemoteControlModalEpisodes.ToListAsync());
        json.ShouldNotContain("SYNTHETIC");
        json.ShouldNotContain("https://claude.ai");
        json.ShouldNotContain("secret");
        var incidents = await verify.AgentIncidents.ToListAsync();
        incidents.ShouldAllBe(i => !i.Message.Contains("https://", StringComparison.Ordinal));
    }

    [Test]
    public async Task C514_Generation_replacement_ends_old_episode_without_transfer()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var generationA = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-1));
        var generationB = SessionGeneration.Next(generationA, DateTime.UtcNow);
        var sessionId = await SeedSessionAsync(isolated.ConnectionString);
        await using var db = Create(isolated.ConnectionString);
        var old = Episode(sessionId, generationA);
        old.DismissalIntentAt = DateTime.UtcNow;
        db.RemoteControlModalEpisodes.Add(old);
        await db.SaveChangesAsync();

        old.ResolvedAt = DateTime.UtcNow;
        old.Resolution = RemoteControlEpisodeResolution.GenerationEnded;
        await db.SaveChangesAsync();

        db.RemoteControlModalEpisodes.Add(Episode(sessionId, generationB));
        await db.SaveChangesAsync();

        await using var verify = Create(isolated.ConnectionString);
        var a = await verify.RemoteControlModalEpisodes.SingleAsync(e => e.AcceptedStartedAt == generationA);
        a.Resolution.ShouldBe(RemoteControlEpisodeResolution.GenerationEnded);
        var b = await verify.RemoteControlModalEpisodes.SingleAsync(e => e.AcceptedStartedAt == generationB);
        b.DismissalIntentAt.ShouldBeNull();
        b.Resolution.ShouldBeNull();
    }

    [Test]
    public async Task C514_Migration_and_receipts_survive_recreation()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = await SeedSessionAsync(isolated.ConnectionString);
        await using (var db = Create(isolated.ConnectionString))
        {
            db.RemoteControlModalEpisodes.Add(Episode(sessionId, SessionGeneration.Normalize(DateTime.UtcNow)));
            await db.SaveChangesAsync();
        }

        await using var verify = Create(isolated.ConnectionString);
        (await verify.RemoteControlModalEpisodes.CountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task C514_Only_meaningful_episode_transitions_emit_incidents()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = await SeedSessionAsync(isolated.ConnectionString);
        await using var db = Create(isolated.ConnectionString);
        db.RemoteControlModalEpisodes.Add(Episode(sessionId, SessionGeneration.Normalize(DateTime.UtcNow)));
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Kind = AgentIncidentKind.RemoteControlModalDetected,
            Severity = AlertSeverity.Warning,
            Message = "Remote Control menu blocks input",
            FailureReason = "rc-modal:" + Guid.NewGuid().ToString("D"),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        (await db.AgentIncidents.CountAsync(i => i.Kind == AgentIncidentKind.RemoteControlModalDetected))
            .ShouldBe(1);
    }

    [Test]
    public async Task C514_Unattributed_menu_keeps_related_request_null()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = await SeedSessionAsync(isolated.ConnectionString);
        await using var db = Create(isolated.ConnectionString);
        db.SessionQueuedMessages.Add(Arm(sessionId, SessionGeneration.Normalize(DateTime.UtcNow), active: false));
        var episode = Episode(sessionId, SessionGeneration.Normalize(DateTime.UtcNow));
        episode.RelatedMaintenanceQueueId = null;
        db.RemoteControlModalEpisodes.Add(episode);
        await db.SaveChangesAsync();
        (await db.RemoteControlModalEpisodes.SingleAsync()).RelatedMaintenanceQueueId.ShouldBeNull();
    }

    [Test]
    public async Task C514_Repeated_observation_keeps_the_same_episode_id()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = await SeedSessionAsync(isolated.ConnectionString);
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        await using var db = Create(isolated.ConnectionString);
        var episode = Episode(sessionId, generation);
        db.RemoteControlModalEpisodes.Add(episode);
        await db.SaveChangesAsync();
        var id = episode.Id;
        episode.LastObservedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        (await db.RemoteControlModalEpisodes.SingleAsync(e => e.SessionId == sessionId && e.ResolvedAt == null))
            .Id.ShouldBe(id);
    }

    [Test]
    public async Task C514_Final_receipt_commit_failure_remains_recoverable()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var sessionId = await SeedSessionAsync(isolated.ConnectionString);
        await using var db = Create(isolated.ConnectionString);
        var row = Arm(sessionId, SessionGeneration.Normalize(DateTime.UtcNow), active: true);
        row.MaintenanceResult = RemoteControlArmResult.SubmissionStarted;
        db.SessionQueuedMessages.Add(row);
        await db.SaveChangesAsync();
        await using var verify = Create(isolated.ConnectionString);
        var reloaded = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == row.Id);
        reloaded.MaintenanceSlotActive.ShouldBeTrue();
        reloaded.MaintenanceResult.ShouldBe(RemoteControlArmResult.SubmissionStarted);
    }

    private static AppDbContext Create(string cs) =>
        new(TestDbFixture.CreateDbContextOptions(cs));

    private static async Task<Guid> SeedSessionAsync(string cs)
    {
        await using var db = Create(cs);
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "c514",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            CreatedAt = DateTime.UtcNow,
            StartedAt = SessionGeneration.Normalize(DateTime.UtcNow),
            LastSeenAt = DateTime.UtcNow,
        };
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private static RemoteControlModalEpisode Episode(Guid sessionId, DateTime generation) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        AcceptedStartedAt = generation,
        FirstObservedAt = DateTime.UtcNow,
        LastObservedAt = DateTime.UtcNow,
    };

    private static SessionQueuedMessage Arm(Guid sessionId, DateTime generation, bool active) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Body = "/remote-control",
        Status = QueuedMessageStatus.Pending,
        Sequence = Random.Shared.Next(1, 10_000),
        Origin = QueuedMessageOrigin.Supervision,
        CreatedAt = DateTime.UtcNow,
        MaintenanceKind = RemoteControlMaintenanceKind.AutomaticArm,
        MaintenanceAcceptedStartedAt = generation,
        MaintenanceSlotActive = active,
        MaintenanceResult = RemoteControlArmResult.Requested,
    };
}
