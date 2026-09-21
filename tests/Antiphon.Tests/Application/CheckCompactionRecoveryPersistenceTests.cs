using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class CheckCompactionRecoveryPersistenceTests
{
    [Test]
    public async Task Unresolved_episode_survives_and_old_terminal_audit_prunes()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var agentId = Guid.NewGuid();
        var openId = Guid.NewGuid();
        var freshId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = NewDb(schema.ConnectionString))
        {
            db.Agents.Add(Agent(agentId, sessionId, now));
            db.AgentSessions.Add(Session(sessionId, agentId, now));
            db.CheckCompactionRecoveries.Add(Episode(openId, agentId, sessionId, "open", CheckCompactionRecoveryState.Confirmed, now));
            db.CheckCompactionRecoveries.Add(Episode(freshId, agentId, sessionId, "fresh", CheckCompactionRecoveryState.Recovered, now.AddDays(-10)));
            db.CheckCompactionRecoveries.Add(Episode(staleId, agentId, sessionId, "stale", CheckCompactionRecoveryState.Recovered, now.AddDays(-91)));
            await db.SaveChangesAsync();
            var removed = await new DataRetentionService(
                db,
                Options.Create(new RetentionSettings()),
                Options.Create(new AuditSettings()),
                TimeProvider.System,
                NullLogger<DataRetentionService>.Instance,
                new AuditService(db, Options.Create(new AuditSettings())))
                .PruneCheckCompactionRecoveriesAsync(CancellationToken.None);
            removed.ShouldBe(1);
        }

        await using var verify = NewDb(schema.ConnectionString);
        (await verify.CheckCompactionRecoveries.AnyAsync(r => r.Id == openId)).ShouldBeTrue();
        (await verify.CheckCompactionRecoveries.AnyAsync(r => r.Id == freshId)).ShouldBeTrue();
        (await verify.CheckCompactionRecoveries.AnyAsync(r => r.Id == staleId)).ShouldBeFalse();
    }

    [Test]
    public async Task Duplicate_episode_key_is_rejected()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var accepted = new DateTime(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc);
        await using var db = NewDb(schema.ConnectionString);
        db.Agents.Add(Agent(agentId, sessionId, accepted));
        db.AgentSessions.Add(Session(sessionId, agentId, accepted));
        db.CheckCompactionRecoveries.Add(Episode(Guid.NewGuid(), agentId, sessionId, "same", CheckCompactionRecoveryState.Confirmed, accepted));
        db.CheckCompactionRecoveries.Add(Episode(Guid.NewGuid(), agentId, sessionId, "same", CheckCompactionRecoveryState.Confirmed, accepted));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static AppDbContext NewDb(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static Agent Agent(Guid id, Guid sessionId, DateTime at) => new()
    {
        Id = id,
        Name = "check",
        Slug = "check-" + id.ToString("N")[..8],
        WorkingDirectory = Path.GetTempPath(),
        Kind = AgentKind.ClaudeCode,
        AlwaysOn = true,
        Status = AgentStatus.Running,
        StandingSpecialistRole = AgentTaskRole.Check,
        StandingSpecialistOwnerId = id,
        PersistentSessionId = sessionId.ToString("D"),
        CreatedAt = at,
        UpdatedAt = at,
    };

    private static AgentSession Session(Guid id, Guid agentId, DateTime at) => new()
    {
        Id = id,
        StandingAgentId = agentId,
        DefinitionName = "claude",
        AgentKind = AgentKind.ClaudeCode,
        Status = SessionStatus.Running,
        Cwd = Path.GetTempPath(),
        CreatedAt = at,
        StartedAt = at,
        LastSeenAt = at,
    };

    private static CheckCompactionRecovery Episode(
        Guid id, Guid agentId, Guid sessionId, string boundary, CheckCompactionRecoveryState state, DateTime detected) =>
        new()
        {
            Id = id,
            PhysicalAgentId = agentId,
            SessionId = sessionId,
            AcceptedStartedAt = detected,
            BoundaryIdentity = boundary,
            BoundaryCreatedAt = detected,
            ContinuationCreatedAt = detected,
            ConfiguredThresholdMinutes = 10,
            DetectedAt = detected,
            State = state,
        };
}
