using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFilePrivacyHostedServiceTests
{
    [Test]
    [Arguments(false, 60)]
    [Arguments(true, 0)]
    public async Task Disabled_or_manual_only_driver_resolves_no_sync(bool enabled, int interval)
    {
        using var hosted = new CardTaskFileSyncHostedService(new RefusingScopes(),
            Options.Create(new CardFileSyncSettings { Enabled = enabled, IntervalSeconds = interval }), NullLogger<CardTaskFileSyncHostedService>.Instance);
        await hosted.StartAsync(default);
        await hosted.ExecuteTask!;
        await hosted.StopAsync(default);
    }

    [Test]
    public async Task Real_bounded_tick_matches_manual_and_dry_cleanup_for_equivalent_state()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        await world.InitializeAsync(); await world.AddCardAsync(); await world.SyncAsync();
        await using (var db = world.Db())
            await db.Boards.Where(b => b.Id == world.BoardId).ExecuteUpdateAsync(s => s.SetProperty(b => b.SyncCardFiles, false));
        var original = Directory.GetFiles(world.DirectoryPath, "*.md").ToDictionary(p => p, File.ReadAllBytes);
        var dry = await world.SyncAsync(dryRun: true);
        var manual = await world.SyncAsync();
        dry.Deleted.ShouldBe(2); manual.Deleted.ShouldBe(dry.Deleted);
        manual.Written.ShouldBe(dry.Written); manual.Unchanged.ShouldBe(dry.Unchanged);
        manual.EligibleCards.ShouldBe(dry.EligibleCards); manual.ExcludedCards.ShouldBe(dry.ExcludedCards);
        manual.WriteSkipReason.ShouldBe(dry.WriteSkipReason);
        // Restore the exact synthetic working state; neither prior arm changed policy or Git state.
        foreach (var (path, bytes) in original) await File.WriteAllBytesAsync(path, bytes);
        var deleted = 0; var written = 0;
        var repository = new CardFileTestRepository { AfterDelete = () => Interlocked.Increment(ref deleted), BeforeWrite = () => { Interlocked.Increment(ref written); return Task.CompletedTask; } };
        var services = new ServiceCollection();
        services.AddScoped(_ => world.Db());
        services.AddScoped(p => world.Service(p.GetRequiredService<AppDbContext>(), repository: repository));
        await using var provider = services.BuildServiceProvider();
        using var hosted = new CardTaskFileSyncHostedService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CardFileSyncSettings { IntervalSeconds = 5 }), NullLogger<CardTaskFileSyncHostedService>.Instance);
        await hosted.StartAsync(default);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (Directory.GetFiles(world.DirectoryPath, "*.md").Length > 0)
                await Task.Delay(50, deadline.Token);
            (await world.Repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
        }
        finally { await hosted.StopAsync(default); }
        deleted.ShouldBe(manual.Deleted); written.ShouldBe(manual.Written);
        await using var check = world.Db(); var status = await world.Service(check).GetStatusAsync(world.BoardId, default);
        System.Text.Json.JsonSerializer.Serialize(status).ShouldBe(System.Text.Json.JsonSerializer.Serialize(manual.Policy));
    }
    private sealed class RefusingScopes : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new InvalidOperationException("Disabled driver resolved a sync scope.");
    }
}
