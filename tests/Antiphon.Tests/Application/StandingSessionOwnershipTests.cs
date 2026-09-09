using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class StandingSessionOwnershipTests
{
    [Test]
    public async Task Deleting_and_recreating_the_same_name_does_not_adopt_historical_ownership()
    {
        await using var f = new StandingRecoveryFixture(new FakeAgentProtocolAdapter());
        await f.SeedAsync();
        await f.Harness.AgentService.DeleteAsync(f.Agent.Id, default);
        var replacement = await f.Harness.AgentService.CreateAsync(new CreateAgentRequest(f.Agent.Name, f.Root), default);
        replacement.Id.ShouldNotBe(f.Agent.Id);
        (await f.Harness.Control.GetSessionsAsync(replacement.Id, 25, null, default)).Items.ShouldBeEmpty();
        (await Should.ThrowAsync<ConflictException>(() => f.Harness.Control.StartAsync(replacement.Id,
            new StartAgentRequest(ResumeSessionId: f.A.Id), default))).Code.ShouldBe("standing_resume_not_owned");
        await using var verify = f.Db();
        (await verify.AgentSessions.FindAsync(f.A.Id))!.StandingAgentId.ShouldBe(f.Agent.Id);
        (await verify.AgentSessions.FindAsync(f.B.Id))!.StandingAgentId.ShouldBe(f.Agent.Id);
        (await verify.Agents.FindAsync(replacement.Id))!.PersistentSessionId.ShouldBeNull();
    }

    [Test]
    [Arguments(0)] [Arguments(2)] [Arguments(500)]
    public async Task Upgrade_backfills_only_unambiguous_owners_and_preserves_recovery_state(int oldCount)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var currentDb = new AppDbContext(options);
        var migrations = currentDb.Database.GetMigrations().ToArray();
        var target = Array.FindIndex(migrations, m => m.EndsWith("_StandingSessionContinuity"));
        target.ShouldBeGreaterThan(0);
        var migrator = currentDb.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[target - 1]);
        (await currentDb.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'AgentSessions' AND column_name = 'StandingAgentId'
            """).SingleAsync()).ShouldBe(0);
        await using var db = new PreContinuityDbContext(options);
        var now = DateTime.UtcNow.AddHours(-1);
        var agent = new Agent { Id = Guid.NewGuid(), Name = "Legacy owner", Slug = $"legacy-{Guid.NewGuid():N}",
            WorkingDirectory = Path.GetTempPath(), CreatedAt = now, UpdatedAt = now };
        var foreign = new Agent { Id = Guid.NewGuid(), Name = "Foreign", Slug = $"foreign-{Guid.NewGuid():N}",
            WorkingDirectory = agent.WorkingDirectory, CreatedAt = now, UpdatedAt = now };
        AgentSession Session() => new() { Id = Guid.NewGuid(), AgentKind = AgentKind.ClaudeCode, DefinitionName = "fake",
            Cwd = agent.WorkingDirectory, Status = SessionStatus.Stopped, CreatedAt = now, StartedAt = now, LastSeenAt = now };
        var old = Session(); var current = Session(); var conflict = Session(); var taskOnly = Session(); var unproven = Session();
        var poolOnly = Session(); var unrelated = Session(); var noEvidence = Session(); var cardSession = Session(); var treeSession = Session();
        agent.PersistentSessionId = current.Id.ToString("D");
        db.Agents.AddRange(agent, foreign); db.AgentSessions.AddRange(old, current, conflict, taskOnly, unproven);
        db.AgentSessions.AddRange(poolOnly, unrelated, noEvidence, cardSession, treeSession);
        var pool = new Agent { Id = Guid.NewGuid(), IsPoolDelegate = true, Name = "Pool", Slug = $"pool-{Guid.NewGuid():N}",
            PersistentSessionId = poolOnly.Id.ToString("D"), WorkingDirectory = agent.WorkingDirectory, CreatedAt = now, UpdatedAt = now };
        db.Agents.Add(pool);
        var card = StandingSessionSelectionTests.SeedCard(db, agent.WorkingDirectory); cardSession.CardId = card.Id;
        var tree = new Worktree { Id = Guid.NewGuid(), CardId = card.Id, Path = agent.WorkingDirectory,
            RepoPath = agent.WorkingDirectory, Branch = "synthetic", CreatedAt = now, LastTouchedAt = now };
        db.Worktrees.Add(tree); treeSession.WorktreeId = tree.Id;
        void Evidence(Guid owner, Guid target) => db.AgentIncidents.Add(new AgentIncident { Id = Guid.NewGuid(), AgentId = owner,
            SessionId = target, Kind = AgentIncidentKind.Crash, Message = "Legacy evidence", CreatedAt = now });
        Evidence(agent.Id, old.Id); Evidence(agent.Id, conflict.Id); Evidence(foreign.Id, conflict.Id);
        Evidence(agent.Id, current.Id); Evidence(agent.Id, cardSession.Id); Evidence(agent.Id, treeSession.Id);
        db.AgentIncidents.Add(new AgentIncident { Id = Guid.NewGuid(), AgentId = agent.Id, SessionId = unrelated.Id,
            Kind = AgentIncidentKind.StartFailure, Message = "Not ownership evidence", CreatedAt = now });
        var taskId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, AgentId = agent.Id,
            AgentSessionId = taskOnly.Id, ParentSessionId = unproven.Id, Title = "Historical execution",
            Goal = "Historical execution", Status = AgentTaskStatus.Succeeded, CreatedAt = now, WorkingDirectory = agent.WorkingDirectory });
        db.AgentSupervisionStates.Add(new AgentSupervisionState { AgentId = agent.Id, ConsecutiveFailures = oldCount,
            NextRestartAt = now.AddDays(1), Suspended = true, LivenessLatchedAt = now, HerdrFailureHeldAt = now,
            CapacityRecoveryActionKey = "synthetic-wait", CapacityNextDueAt = now.AddDays(2),
            HerdrConsecutiveFailures = 3, LastEscalationTier = 2, UpdatedAt = now });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await migrator.MigrateAsync(migrations[target]);
        var state = (await currentDb.AgentSupervisionStates.FindAsync(agent.Id))!;
        state.ConsecutiveFailures.ShouldBe(0); state.RestartBackoffFailures.ShouldBe(oldCount);
        state.Suspended.ShouldBeTrue(); state.LastEscalationTier.ShouldBe(2);
        state.HerdrConsecutiveFailures.ShouldBe(3); state.ContinuityHeldAt.ShouldBeNull();
        state.NextRestartAt!.Value.ShouldBe(now.AddDays(1), TimeSpan.FromMilliseconds(1));
        state.LivenessLatchedAt!.Value.ShouldBe(now, TimeSpan.FromMilliseconds(1));
        state.HerdrFailureHeldAt!.Value.ShouldBe(now, TimeSpan.FromMilliseconds(1));
        state.CapacityRecoveryActionKey.ShouldBe("synthetic-wait");
        state.CapacityNextDueAt!.Value.ShouldBe(now.AddDays(2), TimeSpan.FromMilliseconds(1));
        foreach (var owned in new[] { old.Id, current.Id, taskOnly.Id })
            (await currentDb.AgentSessions.FindAsync(owned))!.StandingAgentId.ShouldBe(agent.Id);
        foreach (var unknown in new[] { conflict.Id, unproven.Id, poolOnly.Id, unrelated.Id, noEvidence.Id, cardSession.Id, treeSession.Id })
            (await currentDb.AgentSessions.FindAsync(unknown))!.StandingAgentId.ShouldBeNull();
        currentDb.Database.HasPendingModelChanges().ShouldBeFalse();
        currentDb.Model.FindEntityType(typeof(AgentSession))!.GetForeignKeys()
            .Any(f => f.Properties.Any(p => p.Name == nameof(AgentSession.StandingAgentId))).ShouldBeFalse();
        currentDb.Model.FindEntityType(typeof(AgentSession))!.GetIndexes().Any(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "StandingAgentId", "CreatedAt" })).ShouldBeTrue();
        var index = await currentDb.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value" FROM pg_indexes WHERE schemaname = current_schema()
            AND indexname = 'IX_AgentSessions_StandingAgentId_CreatedAt'
            """).SingleAsync();
        index.ShouldContain("(\"StandingAgentId\", \"CreatedAt\")");
        (await currentDb.AgentSessions.AnyAsync(s => s.InteractiveLaunchCompletedAt != null || s.RestartFailureKind != null)).ShouldBeFalse();
    }

    // Seed through the predecessor model while the added columns demonstrably do not exist.
    private sealed class PreContinuityDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            var sessions = builder.Entity<AgentSession>();
            foreach (var index in sessions.Metadata.GetIndexes().Where(i => i.Properties.Any(p => p.Name == "StandingAgentId")).ToArray())
                sessions.Metadata.RemoveIndex(index);
            foreach (var name in new[] { "StandingAgentId", "RestartFailureKind", "InteractiveLaunchCompletedAt" }) sessions.Ignore(name);
            foreach (var name in new[] { "RestartBackoffFailures", "LastObservedRestartSessionId", "LastObservedRestartStartedAt",
                "ContinuityHeldAt", "ContinuitySessionId", "ContinuityReason", "ContinuityEvidence" })
                builder.Entity<AgentSupervisionState>().Ignore(name);
        }
    }
}
