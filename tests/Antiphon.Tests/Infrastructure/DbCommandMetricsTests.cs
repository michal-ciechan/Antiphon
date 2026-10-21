using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1403 S0: the call-site counter behind <c>/api/diagnostics/session-state</c>'s
/// <c>dbCommands</c>, and the <see cref="QueryBudget"/> guard each hot-path slice asserts with.
/// </summary>
[Category("Integration")]
public sealed class DbCommandMetricsTests
{
    [Test]
    public async Task C1403_counts_tagged_untagged_and_non_query_commands()
    {
        var metrics = new DbCommandMetrics();
        var id = Guid.NewGuid();
        await using (var db = Context(metrics))
        {
            (await db.AgentSessions.TagWith("rt.c1403.read").AnyAsync(s => s.Id == id)).ShouldBeFalse();
            (await db.AgentSessions.CountAsync(s => s.Id == id)).ShouldBe(0);
            db.AgentSessions.Add(Session(id));
            await db.SaveChangesAsync();
            (await db.Database.ExecuteSqlRawAsync(
                "-- rt.c1403.non-query\nUPDATE \"AgentSessions\" SET \"Cols\" = 121 WHERE \"Id\" = {0}", id)).ShouldBe(1);
            // A tag outside the allowlist is never a key: it counts as untagged.
            (await db.AgentSessions.TagWith("Secret Tag").CountAsync(s => s.Id == id)).ShouldBe(1);
        }

        try
        {
            var snapshot = metrics.Read();
            snapshot.Tags.GetValueOrDefault("rt.c1403.read").ShouldBe(1);
            snapshot.Tags.GetValueOrDefault("rt.c1403.non-query").ShouldBe(1, "a tagged ExecuteSql is a non-query command");
            snapshot.Untagged.ShouldBe(3, "the untagged read, the SaveChanges insert and the non-allowlisted tag");
            snapshot.Total.ShouldBe(5);
            snapshot.Tags.Keys.ShouldBe(["rt.c1403.non-query", "rt.c1403.read"], ignoreOrder: true);
        }
        finally
        {
            await using var cleanup = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await cleanup.AgentSessions.Where(s => s.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task C1403_query_budget_fails_when_exceeded()
    {
        var budget = new QueryBudget();
        await using var db = Context(budget);
        using var scope = budget.Begin();
        await db.AgentSessions.TagWith("rt.c1403.first").AnyAsync(s => s.Id == Guid.Empty);
        await db.AgentSessions.TagWith("rt.c1403.second").AnyAsync(s => s.Id == Guid.Empty);

        scope.Count.ShouldBe(2);
        var failure = Should.Throw<QueryBudgetExceededException>(() => scope.ShouldBeWithin(1));
        failure.Message.ShouldContain("2 commands, budget 1");
        failure.Message.ShouldContain("rt.c1403.first");
        failure.Message.ShouldContain("rt.c1403.second");
        Should.NotThrow(() => scope.ShouldBeWithin(2));
    }

    private static AppDbContext Context(Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor interceptor) =>
        new(new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions())
            .AddInterceptors(interceptor).Options);

    private static AgentSession Session(Guid id) => new()
    {
        Id = id,
        DefinitionName = "c1403-metrics",
        AgentKind = AgentKind.ClaudeCode,
        Status = SessionStatus.Stopped,
        Cwd = Path.GetTempPath(),
        Cols = 120,
        Rows = 30,
        CreatedAt = DateTime.UtcNow,
        StartedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow,
    };
}
