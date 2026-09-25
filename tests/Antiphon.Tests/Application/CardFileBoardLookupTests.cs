using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public partial class CardFileBoardLookupTests
{
    private readonly CardFileBoardLookup _lookup = new();

    [Test]
    public async Task Repeated_inspection_has_no_board_lookup_queries()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        await world.InitializeAsync();
        var queries = new QueryCounter();
        await using var db = Context(isolated.ConnectionString, queries);
        var service = Service(world, db);
        var first = await service.GetStatusAsync(world.BoardId, default);
        first.Directory.ShouldBe("docs/cards/board");
        queries.Lookups.ShouldBeGreaterThan(0);
        queries.Reset();
        await using var otherScope = Context(isolated.ConnectionString, queries);
        (await Service(world, otherScope).GetStatusAsync(world.BoardId, default)).Directory.ShouldBe(first.Directory);
        queries.Lookups.ShouldBe(0);
    }

    [Test]
    public async Task Clean_opted_out_board_is_not_queried_on_repeated_sweeps()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        await world.InitializeAsync(enabledBoard: false);
        var queries = new QueryCounter();
        await using var db = Context(isolated.ConnectionString, queries);
        var service = Service(world, db);
        await service.SyncAllAsync(); // Initial legacy-residue inspection.
        queries.Reset();
        (await service.SyncAllAsync()).ShouldBeEmpty();
        queries.Reads.ShouldBe(0);
    }

    [Test]
    public async Task Opt_out_cleanup_is_completed_before_the_board_leaves_the_sweep()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        await world.InitializeAsync();
        await world.AddCardAsync();
        await world.SyncAsync();
        await using (var edit = world.Db())
            await edit.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.SyncCardFiles, false));
        var queries = new QueryCounter();
        await using var db = Context(isolated.ConnectionString, queries);
        var service = Service(world, db);
        var cleaned = (await service.SyncAllAsync()).Single();
        cleaned.Deleted.ShouldBe(2);
        cleaned.Policy!.RemovalPending.ShouldBeFalse();
        queries.Reset();
        (await service.SyncAllAsync()).ShouldBeEmpty();
        queries.Reads.ShouldBe(0);
        Directory.GetFiles(world.DirectoryPath, "*.md").ShouldBeEmpty();
    }

    [Test]
    public async Task Restored_opted_out_export_is_removed_when_reinspection_is_requested()
    {
        await using var prepared = await PrepareSkippedExportAsync();
        prepared.Queries.Reset();
        _lookup.RequestOptedOutReinspection();
        var removed = (await prepared.Service.SyncAllAsync()).Single();
        removed.Deleted.ShouldBe(2);
        prepared.Queries.Lookups.ShouldBe(0);
        Directory.GetFiles(prepared.World.DirectoryPath, "*.md").ShouldBeEmpty();
    }

    [Test]
    public async Task Server_checkout_reinspects_a_restored_opted_out_export()
    {
        await using var prepared = await PrepareSkippedExportAsync();
        var git = new LandingGit(_lookup);
        var before = _lookup.Reinspection;
        await git.RunAsync(prepared.World.Repo.Path, ["status", "--porcelain"], default);
        _lookup.Reinspection.ShouldBe(before);
        Directory.GetFiles(prepared.World.DirectoryPath, "*.md").Length.ShouldBe(2);
        prepared.Queries.Reset();
        await git.RunAsync(prepared.World.Repo.Path, ["checkout", "--", "."], default);
        _lookup.Reinspection.ShouldBe(before + 1);
        var removed = (await prepared.Service.SyncAllAsync()).Single();
        removed.Deleted.ShouldBe(2);
        prepared.Queries.Lookups.ShouldBe(0);
        Directory.GetFiles(prepared.World.DirectoryPath, "*.md").ShouldBeEmpty();
    }

    [Test]
    public async Task Startup_reinspection_removes_a_restored_opted_out_export()
    {
        await using var prepared = await PrepareSkippedExportAsync();
        using var hosted = new CardTaskFileSyncHostedService(new RefusingScopes(),
            Options.Create(new CardFileSyncSettings { Enabled = true, IntervalSeconds = 0 }),
            NullLogger<CardTaskFileSyncHostedService>.Instance, _lookup);
        await hosted.StartAsync(default);
        await hosted.ExecuteTask!;
        await hosted.StopAsync(default);
        prepared.Queries.Reset();
        var removed = (await prepared.Service.SyncAllAsync()).Single();
        removed.Deleted.ShouldBe(2);
        prepared.Queries.Lookups.ShouldBe(0);
        Directory.GetFiles(prepared.World.DirectoryPath, "*.md").ShouldBeEmpty();
    }

    private async Task<SkippedExport> PrepareSkippedExportAsync()
    {
        var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        AppDbContext? db = null;
        try
        {
            await world.InitializeAsync();
            await world.AddCardAsync();
            await world.SyncAsync();
            var exported = Directory.GetFiles(world.DirectoryPath, "*.md").ToDictionary(path => path, File.ReadAllBytes);
            exported.Count.ShouldBe(2);
            await using (var edit = world.Db())
                await edit.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.SyncCardFiles, false));
            var queries = new QueryCounter();
            db = Context(isolated.ConnectionString, queries);
            var service = Service(world, db);
            var cleaned = (await service.SyncAllAsync()).Single();
            cleaned.Deleted.ShouldBe(2);
            cleaned.Policy!.RemovalPending.ShouldBeFalse();
            (await service.SyncAllAsync()).ShouldBeEmpty();
            foreach (var (path, bytes) in exported)
                await File.WriteAllBytesAsync(path, bytes);
            Directory.GetFiles(world.DirectoryPath, "*.md").Length.ShouldBe(2);
            var prepared = new SkippedExport(isolated, world, db, service, queries);
            db = null;
            return prepared;
        }
        catch
        {
            if (db is not null) await db.DisposeAsync();
            await world.DisposeAsync();
            await isolated.DisposeAsync();
            throw;
        }
    }

    private static AppDbContext Context(string connection, QueryCounter queries) => new(
        new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(connection))
            .AddInterceptors(queries).Options);

    private CardTaskFileService Service(CardFilePrivacyWorld world, AppDbContext db) => new(db, world.Gate,
        new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance), NullLogger<CardTaskFileService>.Instance,
        new CardFileTestRepository(), Options.Create(new CardFileSyncSettings { IntervalSeconds = 0 }), _lookup);

    private sealed class RefusingScopes : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("Startup reinspection resolved a sync scope.");
    }

    private sealed class SkippedExport(
        IAsyncDisposable isolated, CardFilePrivacyWorld world, AppDbContext db, CardTaskFileService service, QueryCounter queries)
        : IAsyncDisposable
    {
        public CardFilePrivacyWorld World { get; } = world;
        public CardTaskFileService Service { get; } = service;
        public QueryCounter Queries { get; } = queries;
        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
            await world.DisposeAsync();
            await isolated.DisposeAsync();
        }
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        private int _reads;
        private int _lookups;
        public int Reads => Volatile.Read(ref _reads);
        public int Lookups => Volatile.Read(ref _lookups);
        public void Reset() { Interlocked.Exchange(ref _reads, 0); Interlocked.Exchange(ref _lookups, 0); }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Count(command);
            return result;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count(command);
            return ValueTask.FromResult(result);
        }
        private void Count(DbCommand command)
        {
            Interlocked.Increment(ref _reads);
            if (command.CommandText.Contains("FROM \"Boards\"", StringComparison.Ordinal)
                && !command.CommandText.Contains("WHERE b.\"Id\" =", StringComparison.Ordinal)) Interlocked.Increment(ref _lookups);
        }
    }
}
