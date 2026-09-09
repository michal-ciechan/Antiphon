using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class StandingSessionOwnershipTests
{
    [Test]
    public async Task Upgrade_backfills_only_unambiguous_owners_and_preserves_recovery_state()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow.AddHours(-1);
        var agent = new Agent { Id = Guid.NewGuid(), Name = "Legacy owner", Slug = $"legacy-{Guid.NewGuid():N}",
            WorkingDirectory = Path.GetTempPath(), CreatedAt = now, UpdatedAt = now };
        var foreign = new Agent { Id = Guid.NewGuid(), Name = "Foreign", Slug = $"foreign-{Guid.NewGuid():N}",
            WorkingDirectory = agent.WorkingDirectory, CreatedAt = now, UpdatedAt = now };
        AgentSession Session() => new() { Id = Guid.NewGuid(), AgentKind = AgentKind.ClaudeCode, DefinitionName = "fake",
            Cwd = agent.WorkingDirectory, Status = SessionStatus.Stopped, CreatedAt = now, StartedAt = now, LastSeenAt = now };
        var old = Session(); var current = Session(); var conflict = Session(); var taskOnly = Session(); var unproven = Session();
        agent.PersistentSessionId = current.Id.ToString("D");
        db.Agents.AddRange(agent, foreign); db.AgentSessions.AddRange(old, current, conflict, taskOnly, unproven);
        void Evidence(Guid owner, Guid target) => db.AgentIncidents.Add(new AgentIncident { Id = Guid.NewGuid(), AgentId = owner,
            SessionId = target, Kind = AgentIncidentKind.Crash, Message = "Legacy evidence", CreatedAt = now });
        Evidence(agent.Id, old.Id); Evidence(agent.Id, conflict.Id); Evidence(foreign.Id, conflict.Id);
        var taskId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, AgentId = agent.Id,
            AgentSessionId = taskOnly.Id, ParentSessionId = unproven.Id, Title = "Historical execution",
            Goal = "Historical execution", Status = AgentTaskStatus.Succeeded, CreatedAt = now, WorkingDirectory = agent.WorkingDirectory });
        db.AgentSupervisionStates.Add(new AgentSupervisionState { AgentId = agent.Id, ConsecutiveFailures = 500,
            NextRestartAt = now.AddDays(1), Suspended = true, LivenessLatchedAt = now, HerdrFailureHeldAt = now,
            HerdrConsecutiveFailures = 3, LastEscalationTier = 2, UpdatedAt = now });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var migrations = db.Database.GetMigrations().ToArray();
        var target = Array.FindIndex(migrations, m => m.EndsWith("_StandingSessionContinuity"));
        target.ShouldBeGreaterThan(0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[target - 1]);
        await migrator.MigrateAsync(migrations[target]);
        var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
        state.ConsecutiveFailures.ShouldBe(0); state.RestartBackoffFailures.ShouldBe(500);
        state.Suspended.ShouldBeTrue(); state.LastEscalationTier.ShouldBe(2);
        state.HerdrConsecutiveFailures.ShouldBe(3); state.ContinuityHeldAt.ShouldBeNull();
        state.NextRestartAt!.Value.ShouldBe(now.AddDays(1), TimeSpan.FromMilliseconds(1));
        foreach (var owned in new[] { old.Id, current.Id, taskOnly.Id })
            (await db.AgentSessions.FindAsync(owned))!.StandingAgentId.ShouldBe(agent.Id);
        foreach (var unknown in new[] { conflict.Id, unproven.Id })
            (await db.AgentSessions.FindAsync(unknown))!.StandingAgentId.ShouldBeNull();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        db.Model.FindEntityType(typeof(AgentSession))!.GetForeignKeys()
            .Any(f => f.Properties.Any(p => p.Name == nameof(AgentSession.StandingAgentId))).ShouldBeFalse();
    }
}
