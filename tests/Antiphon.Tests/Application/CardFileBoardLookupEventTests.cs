using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Realtime;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class CardFileBoardLookupTests
{
    [Test]
    [Arguments("board-create")]
    [Arguments("board-rename")]
    [Arguments("board-delete")]
    [Arguments("board-archive")]
    [Arguments("board-unarchive")]
    [Arguments("project-create")]
    [Arguments("project-rename")]
    [Arguments("project-delete")]
    [Arguments("project-archive")]
    [Arguments("project-unarchive")]
    [Arguments("project-visibility")]
    [Arguments("board-opt-out")]
    [Arguments("board-opt-in")]
    public async Task Committed_change_invalidates_the_shared_lookup(string change)
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        await world.InitializeAsync(enabledBoard: change != "board-opt-in");
        await using var db = world.Db();
        using var provider = Events();
        var events = provider.GetRequiredService<IEventBus>();
        var boards = new BoardService(db, events, TimeProvider.System);
        var projects = new ProjectService(db, null!, Options.Create(new GithubSettings()),
            NullLogger<ProjectService>.Instance, eventBus: events);
        if (change == "board-unarchive") await boards.ArchiveAsync(world.BoardId, new("test"), default);
        if (change == "project-unarchive") await projects.ArchiveAsync(world.ProjectId, new("test"), default);
        var counter = new QueryCounter();
        await using var read = Context(isolated.ConnectionString, counter);
        await _lookup.GetAsync(read, default);
        counter.Lookups.ShouldBe(1);
        counter.Reset();
        await _lookup.GetAsync(read, default);
        counter.Reads.ShouldBe(0);

        switch (change)
        {
            case "board-create": await boards.CreateAsync(new(world.ProjectId, "Sibling"), default); break;
            // There is no board rename endpoint. Exercise the existing BoardChanged contract
            // after the committed write, as an importer/rename publisher must do.
            case "board-rename":
                await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.Name, "Renamed"));
                await events.PublishToAllAsync("BoardChanged", new { boardId = world.BoardId }); break;
            case "board-delete": await boards.DeleteAsync(world.BoardId, default); break;
            case "board-archive": await boards.ArchiveAsync(world.BoardId, new("test"), default); break;
            case "board-unarchive": await boards.UnarchiveAsync(world.BoardId, new("test"), default); break;
            case "project-create": await projects.CreateAsync(new("New", "https://example.invalid/new", null, false, false), default); break;
            case "project-rename":
            case "project-visibility":
                await projects.UpdateAsync(world.ProjectId, new("Renamed", "https://example.invalid/c408.git", null, false, false, world.Repo.Path, "master")
                    { RepositoryVisibility = change == "project-visibility" ? RepositoryVisibility.Private : RepositoryVisibility.Public }, default); break;
            case "project-delete": await projects.DeleteAsync(world.ProjectId, true, default); break;
            case "project-archive": await projects.ArchiveAsync(world.ProjectId, new("test"), default); break;
            case "project-unarchive": await projects.UnarchiveAsync(world.ProjectId, new("test"), default); break;
            case "board-opt-out": await Service(world, db).UpdateSettingsAsync(world.BoardId, false, true, events, default); break;
            case "board-opt-in": await Service(world, db).UpdateSettingsAsync(world.BoardId, true, false, events, default); break;
        }
        // Settings returns status and can refill the shared cache before returning.
        if (change is "board-opt-out" or "board-opt-in")
            counter.Lookups.ShouldBe(0);
        var actual = await _lookup.GetAsync(read, default);
        if (change is not ("board-opt-out" or "board-opt-in")) counter.Lookups.ShouldBe(1);
        var fresh = await new CardFileBoardLookup().GetAsync(db, default);
        actual.Boards.SequenceEqual(fresh.Boards).ShouldBeTrue(change);
        counter.Reset();
        await _lookup.GetAsync(read, default);
        counter.Reads.ShouldBe(0);
    }

    [Test]
    public async Task Renames_recompute_collisions_including_opted_out_and_archived_siblings_but_retain_pins()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        await world.InitializeAsync();
        await using var db = world.Db();
        var older = new Board { Id = Guid.NewGuid(), ProjectId = world.ProjectId, Name = "BOARD!",
            CreatedAt = DateTime.UtcNow.AddDays(-1), UpdatedAt = DateTime.UtcNow, ArchivedAt = DateTime.UtcNow };
        db.Boards.Add(older);
        await db.SaveChangesAsync();
        using var provider = Events();
        var events = provider.GetRequiredService<IEventBus>();
        var service = Service(world, db);
        (await service.GetStatusAsync(world.BoardId, default)).Directory.ShouldBe($"docs/cards/board-{world.BoardId.ToString("N")[..8]}");
        older.Name = "Other";
        await db.SaveChangesAsync();
        await events.PublishToAllAsync("BoardChanged", new { boardId = older.Id });
        (await service.GetStatusAsync(world.BoardId, default)).Directory.ShouldBe("docs/cards/board");
        await service.SyncBoardAsync(world.BoardId);
        await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.Name, "New name"));
        await events.PublishToAllAsync("BoardChanged", new { boardId = world.BoardId });
        (await service.GetStatusAsync(world.BoardId, default)).Directory.ShouldBe("docs/cards/board");
        older.Name = "BOARD!";
        await db.SaveChangesAsync();
        await events.PublishToAllAsync("BoardChanged", new { boardId = older.Id });
        (await service.GetStatusAsync(older.Id, default)).Reason.ShouldBe("card_file_directory_conflict");
    }

    [Test]
    public async Task An_event_during_a_fill_discards_the_old_result_and_waiters_share_the_new_result()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        await world.InitializeAsync();
        var barrier = new FillBarrier();
        var counter = new QueryCounter();
        await using var read = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString))
            .AddInterceptors(counter, barrier).Options);
        await using var second = Context(isolated.ConnectionString, counter);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstFill = _lookup.GetAsync(read, deadline.Token);
        await barrier.Reached.Task.WaitAsync(deadline.Token);
        var waiter = _lookup.GetAsync(second, deadline.Token);
        try
        {
            await using var edit = world.Db();
            await edit.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.Name, "After event"), deadline.Token);
            using var provider = Events();
            await provider.GetRequiredService<IEventBus>().PublishToAllAsync("BoardChanged", new { boardId = world.BoardId }, deadline.Token);
        }
        finally { barrier.Release.TrySetResult(); }
        var results = await Task.WhenAll(firstFill, waiter);
        results[0].Boards.Single().Name.ShouldBe("After event");
        ReferenceEquals(results[0], results[1]).ShouldBeTrue();
        counter.Lookups.ShouldBe(2);
    }

    [Test]
    public async Task Transaction_local_lookups_neither_reuse_nor_populate_the_shared_snapshot()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        await world.InitializeAsync();
        await using var read = world.Db();
        var initial = await _lookup.GetAsync(read, default);
        await using var edit = world.Db();
        await using (var transaction = await edit.Database.BeginTransactionAsync())
        {
            await edit.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.Name, "Uncommitted"));
            (await _lookup.GetAsync(edit, default)).Boards.Single().Name.ShouldBe("Uncommitted");
            _lookup.Invalidate();
            (await _lookup.GetAsync(edit, default)).Boards.Single().Name.ShouldBe("Uncommitted");
            (await _lookup.GetAsync(read, default)).Boards.Single().Name.ShouldBe("Board");
            await transaction.RollbackAsync();
        }
        (await _lookup.GetAsync(read, default)).Boards.SequenceEqual(initial.Boards).ShouldBeTrue();
    }

    [Test]
    public async Task Opted_out_cleanup_failure_is_retried_and_an_opt_in_event_restores_sweeping()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        await world.InitializeAsync();
        await world.AddCardAsync();
        await world.SyncAsync(autoCommit: true);
        await using var db = world.Db();
        using var provider = Events();
        var events = provider.GetRequiredService<IEventBus>();
        var service = Service(world, db);
        await service.UpdateSettingsAsync(world.BoardId, false, true, events, default);
        (await service.SyncAllAsync()).Single().Policy!.GitRemovalPending.ShouldBe(true);
        (await service.SyncAllAsync()).Single().Policy!.GitRemovalPending.ShouldBe(true);
        await world.Repo.GitAsync("add", "-u");
        await world.Repo.GitAsync("commit", "-m", "Remove revoked exports");
        (await service.SyncAllAsync()).Single().Policy!.RemovalPending.ShouldBeFalse();
        (await service.SyncAllAsync()).ShouldBeEmpty();
        await service.UpdateSettingsAsync(world.BoardId, true, false, events, default);
        (await service.SyncAllAsync()).Single().Written.ShouldBe(2);
    }

    private ServiceProvider Events()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        services.AddSingleton(_lookup);
        services.AddSingleton<IEventBus, EventBus>();
        return services.BuildServiceProvider();
    }

    private sealed class FillBarrier : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _held;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("CardFileBoardLookup", StringComparison.Ordinal) && Interlocked.Exchange(ref _held, 1) == 0)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
