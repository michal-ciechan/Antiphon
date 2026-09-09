using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class StandingSpecialistRoutingMigrationTests
{
    [Test]
    public async Task Card0415_V25_real_upgrade_preserves_primary_tasks_and_holds_without_fabricating_qualification()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow;
        var owner = new Agent { Id = Guid.NewGuid(), Name = "legacy primary", Slug = "legacy-primary",
            Kind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.High, ModelId = "sonnet",
            WorkingDirectory = Path.GetTempPath(), AlwaysOn = true, CreatedAt = now, UpdatedAt = now };
        var taskId = Guid.NewGuid();
        db.Agents.Add(owner);
        db.AgentTasks.Add(new AgentTask { Id = taskId, RootTaskId = taskId, Title = "legacy Check",
            Goal = "legacy facts", Role = AgentTaskRole.Check, AgentId = owner.Id,
            Status = AgentTaskStatus.Failed, FailureReason = "original identity refusal", CreatedAt = now });
        var hold = new ModelAvailabilityHold { Id = Guid.NewGuid(), Kind = AgentKind.ClaudeCode,
            ModelAlias = "sonnet", Source = ModelAvailabilitySource.Manual, HitAt = now,
            DisabledUntil = now.AddHours(1), Reason = "fixture capacity hold" };
        db.ModelAvailabilityHolds.Add(hold);
        await db.SaveChangesAsync();
        // Compare the persisted value: PostgreSQL timestamps have microsecond precision.
        var persistedUntil = await db.ModelAvailabilityHolds.Where(h => h.Id == hold.Id).Select(h => h.DisabledUntil).SingleAsync();
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var first = Array.FindIndex(migrations, m => m.EndsWith("_AddSpecialistExecutionIdentity", StringComparison.Ordinal));
        first.ShouldBeGreaterThan(0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[first - 1]);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AgentTasks\" SET \"FailureReason\" = 'legacy write while downgraded' WHERE \"Id\" = {taskId}");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var primary = await db.Agents.SingleAsync(a => a.Id == owner.Id);
        primary.Kind.ShouldBe(AgentKind.ClaudeCode);
        primary.ModelLevel.ShouldBe(AgentModelLevel.High);
        primary.ModelId.ShouldBe("sonnet");
        primary.WorkingDirectory.ShouldBe(owner.WorkingDirectory);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.FailureReason.ShouldBe("legacy write while downgraded");
        task.SpecialistModelAlias.ShouldBeNull();
        task.SpecialistSessionId.ShouldBeNull();
        var retained = await db.ModelAvailabilityHolds.SingleAsync(h => h.Id == hold.Id);
        retained.DisabledUntil.ShouldBe(persistedUntil);
        retained.ClearedAt.ShouldBeNull();
        (await db.StandingSpecialistRoutings.CountAsync()).ShouldBe(0);
        (await db.StandingSpecialistCandidateStates.CountAsync()).ShouldBe(0);
        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Test]
    [Arguments("routing")]
    [Arguments("pair")]
    [Arguments("physical")]
    public async Task Card0415_V25_real_unique_indexes_refuse_duplicate_owner_pair_or_active_seat(string duplicate)
    {
        await using var h = await StandingSpecialistRoutingHttpTests.Harness.CreateAsync();
        await h.PutAsync(null, h.Pairs);
        await using var db = h.Context();
        var now = DateTime.UtcNow;
        if (duplicate == "routing")
            db.StandingSpecialistRoutings.Add(new StandingSpecialistRouting { Id = Guid.NewGuid(), AgentId = h.AgentId,
                CandidatesJson = "[]", CreatedAt = now, UpdatedAt = now });
        else
            db.StandingSpecialistCandidateStates.Add(new StandingSpecialistCandidateState { Id = Guid.NewGuid(),
                AgentId = h.AgentId, AgentKind = AgentKind.ClaudeCode,
                ModelLevel = duplicate == "pair" ? AgentModelLevel.High : AgentModelLevel.Low,
                PhysicalAgentId = duplicate == "physical" ? h.AgentId : null, Enabled = true,
                DeclaredAt = now, UpdatedAt = now });
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldStartWith(duplicate == "routing" ? "IX_StandingSpecialistRoutings_AgentId"
            : duplicate == "physical" ? "IX_StandingSpecialistCandidateStates_PhysicalAgentId"
            : "IX_StandingSpecialistCandidateStates_AgentId_AgentKind_ModelLe");
    }
}
