using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-0824 provisional multi-step schema/identity budget.
public sealed class C0824RefOnlyIdentityTests
{
    [Test]
    public async Task Mode_round_trips_with_legacy_default()
    {
        // Assert representation before touching new columns: absent mapping must be assertion-red, never SQL setup red.
        await using (var model = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            var task = model.Model.FindEntityType(typeof(AgentTask))!;
            task.FindProperty(nameof(AgentTask.DesktopWorkspaceKind)).ShouldNotBeNull("persisted-mode=RefOnly");
            task.FindProperty(nameof(AgentTask.TaskRefWorkspaceBindingId)).ShouldNotBeNull("persisted-binding=expected");
            model.Model.FindEntityType(typeof(TaskRefWorkspaceBinding)).ShouldNotBeNull("durable-binding-schema");
        }
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var id = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AgentTasks" ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role", "ModelLevel",
                    "Attempt", "MaxAttempts", "Workspace", "WorkingDirectory", "Ephemeral", "Status", "ReplyTo",
                    "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut", "CostUsd")
                VALUES ({id}, {id}, {0}, {"legacy"}, {"preserve"}, {0}, {0}, {0}, {1}, {2}, {1},
                    {"/tmp/c0824-legacy"}, {false}, {0}, {0}, {Guid.NewGuid()}, {DateTime.UtcNow}, {0L}, {0L}, {0m})
                """);
        }
        await using var fresh = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var legacy = await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        legacy.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.Checkout, "legacy-default=Checkout");
        legacy.TaskRefWorkspaceBindingId.ShouldBeNull();
    }
}
