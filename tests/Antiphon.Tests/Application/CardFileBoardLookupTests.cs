using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    private static AppDbContext Context(string connection, QueryCounter queries) => new(
        new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(connection))
            .AddInterceptors(queries).Options);

    private CardTaskFileService Service(CardFilePrivacyWorld world, AppDbContext db) => new(db, world.Gate,
        new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance), NullLogger<CardTaskFileService>.Instance,
        new CardFileTestRepository(), Options.Create(new CardFileSyncSettings { IntervalSeconds = 0 }), _lookup);

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads { get; private set; }
        public int Lookups { get; private set; }
        public void Reset() { Reads = 0; Lookups = 0; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Reads++;
            if (command.CommandText.Contains("FROM \"Boards\"", StringComparison.Ordinal)
                && !command.CommandText.Contains("WHERE b.\"Id\" =", StringComparison.Ordinal)) Lookups++;
            return ValueTask.FromResult(result);
        }
    }
}
