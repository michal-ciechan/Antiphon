using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0547 D-1: the loaders over the shared test database. Rows are id-scoped and deleted by id.</summary>
[Category("Integration")]
public sealed class CommitRecoveryObligationsLoaderTests
{
    private const string Digest = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    private static AppDbContext CreateContext(CountingCommandInterceptor? counter = null) =>
        counter is null
            ? new(TestDbFixture.CreateDbContextOptions())
            : new(new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions()).AddInterceptors(counter).Options);

    private static async Task<Guid> SeedTaskAsync(List<Guid> owned)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "C547 loader",
            Goal = "Hold an obligation.",
            Role = AgentTaskRole.Code,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        owned.Add(id);
        return id;
    }

    private static async Task<AgentTaskEvent> AddAsync(Guid task, AgentTaskEventType type, string detail, int minutesAgo)
    {
        var row = new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task,
            Type = type,
            Detail = detail,
            At = DateTime.UtcNow.AddMinutes(-minutesAgo),
        };
        await using var db = CreateContext();
        db.AgentTaskEvents.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private static async Task CleanupAsync(List<Guid> owned)
    {
        await using var db = CreateContext();
        await db.AgentTaskEvents.Where(e => owned.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
        await db.AgentTasks.Where(t => owned.Contains(t.Id)).ExecuteDeleteAsync();
    }

    [Test]
    public async Task LoadUnresolvedAsync_is_task_scoped_and_applies_the_rule()
    {
        var owned = new List<Guid>();
        try
        {
            var a = await SeedTaskAsync(owned);
            var b = await SeedTaskAsync(owned);
            var resolved = await AddAsync(a, AgentTaskEventType.CommitRecoveryStarted, Digest, 30);
            await AddAsync(a, AgentTaskEventType.CommitRecoveryNotNeeded, $"{resolved.Id:D} NothingToCommit", 29);
            var open = await AddAsync(a, AgentTaskEventType.CommitRecoveryStarted, Digest, 10);
            await AddAsync(b, AgentTaskEventType.CommitRecoveryStarted, Digest, 10);

            await using var db = CreateContext();
            var pending = await CommitRecoveryObligations.LoadUnresolvedAsync(db, a, CancellationToken.None);

            var only = pending.ShouldHaveSingleItem();
            only.EventId.ShouldBe(open.Id);
            only.TaskId.ShouldBe(a);
        }
        finally
        {
            await CleanupAsync(owned);
        }
    }

    [Test]
    public async Task LoadUnresolvedAsync_for_a_set_runs_one_query_and_omits_tasks_with_nothing_pending()
    {
        var owned = new List<Guid>();
        try
        {
            var a = await SeedTaskAsync(owned);
            var b = await SeedTaskAsync(owned);
            var c = await SeedTaskAsync(owned);
            var resolved = await AddAsync(a, AgentTaskEventType.CommitRecoveryStarted, Digest, 30);
            await AddAsync(a, AgentTaskEventType.CommitRecoveryNotNeeded, $"{resolved.Id:D} NothingToCommit", 29);
            var openA = await AddAsync(a, AgentTaskEventType.CommitRecoveryStarted, Digest, 10);
            var openB = await AddAsync(b, AgentTaskEventType.CommitRecoveryStarted, Digest, 10);

            var counter = new CountingCommandInterceptor();
            await using var db = CreateContext(counter);
            var pending = await CommitRecoveryObligations.LoadUnresolvedAsync(db, [a, b, c], CancellationToken.None);

            pending.Keys.ShouldBe([a, b], ignoreOrder: true);
            pending[a].ShouldHaveSingleItem().EventId.ShouldBe(openA.Id);
            pending[b].ShouldHaveSingleItem().EventId.ShouldBe(openB.Id);
            counter.Count("AgentTaskEvents").ShouldBe(1);
        }
        finally
        {
            await CleanupAsync(owned);
        }
    }
}
