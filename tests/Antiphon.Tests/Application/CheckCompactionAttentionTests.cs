using Antiphon.Server.Application.Dtos;
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
public class CheckCompactionAttentionTests
{
    [Test]
    public async Task Open_episode_is_one_attention_row_until_it_is_recovered()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = DateTime.UtcNow;
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        await using (var db = NewDb(schema.ConnectionString))
        {
            db.Agents.Add(new Agent
            {
                Id = agentId, Name = "check", Slug = "check-" + agentId.ToString("N")[..8],
                WorkingDirectory = Path.GetTempPath(), Kind = AgentKind.ClaudeCode, AlwaysOn = true,
                Status = AgentStatus.Running, StandingSpecialistRole = AgentTaskRole.Check,
                StandingSpecialistOwnerId = agentId, PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = now, UpdatedAt = now,
            });
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, StandingAgentId = agentId, DefinitionName = "claude",
                AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running, Cwd = Path.GetTempPath(),
                CreatedAt = now, StartedAt = now, LastSeenAt = now,
            });
            db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
            {
                Id = episodeId, PhysicalAgentId = agentId, SessionId = sessionId,
                AcceptedStartedAt = now, BoundaryIdentity = "boundary-1", BoundarySequence = 12,
                BoundaryCreatedAt = now, ContinuationCreatedAt = now, ConfiguredThresholdMinutes = 10,
                DetectedAt = now, State = CheckCompactionRecoveryState.AwaitingCheck,
            });
            await db.SaveChangesAsync();
        }

        await using var read = NewDb(schema.ConnectionString);
        var attention = await new AttentionService(
            read, new FakeSessionRunnerClient(), Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()), TimeProvider.System,
            NullLogger<AttentionService>.Instance).GetAsync(CancellationToken.None);
        var item = attention.Items.Single(i => i.Kind == AttentionKind.CompactionContinuationStalled);
        item.AgentId.ShouldBe(agentId);
        item.SessionId.ShouldBe(sessionId);
        item.ConditionKey.ShouldBe($"compaction-continuation:{episodeId:N}");
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.Headline.ShouldContain("validation pending");
        item.Evidence.ShouldContain(CheckCompactionRecovery.Actor);

        await using var done = NewDb(schema.ConnectionString);
        var episode = await done.CheckCompactionRecoveries.SingleAsync();
        episode.State = CheckCompactionRecoveryState.Recovered;
        await done.SaveChangesAsync();
        await using var after = NewDb(schema.ConnectionString);
        var cleared = await new AttentionService(
            after, new FakeSessionRunnerClient(), Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()), TimeProvider.System,
            NullLogger<AttentionService>.Instance).GetAsync(CancellationToken.None);
        cleared.Items.ShouldNotContain(i => i.Kind == AttentionKind.CompactionContinuationStalled);
    }

    private static AppDbContext NewDb(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));
}
