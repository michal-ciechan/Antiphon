using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class AgentTaskLandNotificationPersistenceTests
{
    [Test]
    public async Task Legacy_capture_key_is_unique_in_postgres()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var episodeId = await SeedEpisodeAsync(schema.ConnectionString);
        var keyTime = DateTime.UtcNow;
        var checkedTaskId = Guid.NewGuid();
        await InsertPublicationAsync(schema.ConnectionString, episodeId, keyTime, checkNumber: 1, checkedTaskId: checkedTaskId);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        db.LegacyCheckNotePublications.Add(Publication(episodeId, keyTime, checkNumber: 1, checkedTaskId: checkedTaskId));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task Legacy_interpretation_link_is_unique_in_postgres()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var episodeId = await SeedEpisodeAsync(schema.ConnectionString);
        var runId = Guid.NewGuid();
        await InsertPublicationAsync(schema.ConnectionString, episodeId, DateTime.UtcNow, checkNumber: 1, runId);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        db.LegacyCheckNotePublications.Add(Publication(episodeId, DateTime.UtcNow.AddMinutes(1), checkNumber: 2, runId));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task Legacy_publication_state_constraints_preserve_capture_lifecycle()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var episodeId = await SeedEpisodeAsync(schema.ConnectionString);
        await InsertPublicationAsync(schema.ConnectionString, episodeId, DateTime.UtcNow, checkNumber: 1);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var incomplete = Publication(episodeId, DateTime.UtcNow.AddMinutes(1), checkNumber: 2);
        incomplete.State = LegacyCheckNoteState.Produced;
        incomplete.Body = "";
        incomplete.ContentDigest = DelegationDigest();
        incomplete.ProducedAt = DateTime.UtcNow;
        db.LegacyCheckNotePublications.Add(incomplete);
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static string DelegationDigest() => "abc";

    private static async Task<Guid> SeedEpisodeAsync(string connectionString)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        var id = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        db.Agents.Add(new Agent
        {
            Id = agentId, Name = "check", Slug = "p" + agentId.ToString("N")[..8], WorkingDirectory = Path.GetTempPath(),
            Kind = AgentKind.ClaudeCode, Status = AgentStatus.Running, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
        {
            Id = id, PhysicalAgentId = agentId, SessionId = Guid.NewGuid(),
            AcceptedStartedAt = DateTime.UtcNow, BoundaryIdentity = "b", BoundaryCreatedAt = DateTime.UtcNow,
            ContinuationCreatedAt = DateTime.UtcNow, ConfiguredThresholdMinutes = 10, DetectedAt = DateTime.UtcNow,
            State = CheckCompactionRecoveryState.AwaitingCheck,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task InsertPublicationAsync(
        string connectionString, Guid episodeId, DateTime dispatched, int checkNumber, Guid? runId = null, Guid? checkedTaskId = null)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
        db.LegacyCheckNotePublications.Add(Publication(episodeId, dispatched, checkNumber, runId, checkedTaskId));
        await db.SaveChangesAsync();
    }

    private static LegacyCheckNotePublication Publication(
        Guid episodeId, DateTime dispatched, int checkNumber, Guid? runId = null, Guid? checkedTaskId = null) =>
        new()
        {
            Id = Guid.NewGuid(), CheckedTaskId = checkedTaskId ?? Guid.NewGuid(), CheckedTaskAttempt = 1,
            CheckedTaskDispatchedAt = dispatched, CheckNumber = checkNumber, RecoveryId = episodeId,
            PhysicalAgentId = Guid.NewGuid(), InterpreterSessionId = Guid.NewGuid(),
            InterpreterAcceptedStartedAt = DateTime.UtcNow, ParentSessionId = Guid.NewGuid(),
            CapturedAt = DateTime.UtcNow, FactsSnapshotJson = "{}", RenderContextJson = "{}",
            InterpretationTaskId = runId, InterpretationDeadlineAt = DateTime.UtcNow.AddMinutes(1),
            State = LegacyCheckNoteState.Captured, SourceEventId = Guid.NewGuid(), NotificationId = Guid.NewGuid(),
            NextAttemptAt = DateTime.UtcNow,
        };
}
