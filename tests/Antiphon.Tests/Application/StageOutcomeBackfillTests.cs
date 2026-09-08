using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class StageOutcomeBackfillTests
{
    [Test]
    [Arguments(21, "build OK, pushed, worktree removed")]
    [Arguments(21, "build skipped (base unchanged)")]
    [Arguments(24, "build OK, pushed, cleanup incomplete")]
    [Arguments(24, "build skipped, cleanup incomplete")]
    [Arguments(22, "push rejected; could not delete; build failed")]
    [Arguments(29, "already present, no push attempted")]
    public async Task C448_V33_BackfillCannotInventLandingEvidence(int eventValue, string contradictoryProse)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, CreatedAt = DateTime.UtcNow });
        db.AgentTaskEvents.AddRange(
            new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = id, Type = AgentTaskEventType.LandRequested,
                Detail = "land requested", At = DateTime.UtcNow.AddSeconds(-12) },
            new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = id, Type = (AgentTaskEventType)eventValue,
                Detail = contradictoryProse, At = DateTime.UtcNow });
        await db.SaveChangesAsync();
        (await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None)).ShouldBe(0);
        (await StageOutcomeBackfillService.RunAsync(db, CancellationToken.None)).ShouldBe(0);
        await using var observer = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        (await observer.StageOutcomes.CountAsync(s => s.SubjectTaskId == id)).ShouldBe(0);
        (await observer.AgentTaskEvents.CountAsync(e => e.AgentTaskId == id)).ShouldBe(2);
    }

    [Test]
    public async Task Retired_hosted_service_does_not_open_a_scope()
    {
        using var service = new StageOutcomeBackfillService(new RefusingScopeFactory(),
            NullLogger<StageOutcomeBackfillService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;
    }

    private sealed class RefusingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("Retired backfill must not query event prose");
    }
}
