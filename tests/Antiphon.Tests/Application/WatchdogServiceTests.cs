using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("Watchdog")]
public class WatchdogServiceTests
{
    [Test]
    public void Watchdog_matches_known_prompt_patterns()
    {
        var matcher = new WatchdogMatcher();
        var settings = new WatchdogSettings();

        matcher.Match("Press Enter to continue", settings.Rules)!.Response.ShouldBe("\r");
        matcher.Match("Do you want to proceed? (Y/n)", settings.Rules)!.Response.ShouldBe("y\r");
        matcher.Match("Delete generated file? [y/N]", settings.Rules)!.Response.ShouldBe("n\r");
    }

    [Test]
    public async Task Watchdog_auto_responds_with_configured_input_and_respects_cooldown()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var session = NewSession(graph.Card);
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            graph.Card.OwnerSessionId = session.Id;
            await db.SaveChangesAsync();

            var adapter = new FakeAgentProtocolAdapter();
            var eventBus = new MockEventBus();
            await using var provider = BuildProvider(tempRoot, eventBus);
            var runtime = new AgentSessionRuntime(
                eventBus,
                Options.Create(new AgentSessionSettings
                {
                    SessionLogPath = Path.Combine(tempRoot, "session-logs")
                }),
                provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                NullLogger<AgentSessionRuntime>.Instance);
            runtime.Register(session.Id, adapter);
            adapter.Emit("Press Enter to continue");
            await WaitUntilAsync(() => runtime.GetBufferSnapshot(session.Id).Buffer.Contains("Press Enter", StringComparison.Ordinal));
            var service = new WatchdogService(
                db,
                runtime,
                new WatchdogMatcher(),
                new WatchdogCooldownStore(),
                eventBus,
                Options.Create(new WatchdogSettings { CooldownMs = 60_000 }),
                TimeProvider.System,
                NullLogger<WatchdogService>.Instance);

            var first = await service.ScanAsync(CancellationToken.None);
            var second = await service.ScanAsync(CancellationToken.None);

            first.ShouldBe(1);
            second.ShouldBe(0);
            adapter.SentInput.ShouldBe("\r");
            eventBus.PublishedEvents.ShouldContain(e => e.EventName == "WatchdogAutoResponded");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task WatchdogHostedService_polls_and_auto_responds_with_configured_input()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var session = NewSession(graph.Card);
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            graph.Card.OwnerSessionId = session.Id;
            await db.SaveChangesAsync();

            var adapter = new FakeAgentProtocolAdapter();
            var eventBus = new MockEventBus();
            await using var provider = BuildProviderWithWatchdog(tempRoot, eventBus);
            var runtime = provider.GetRequiredService<AgentSessionRuntime>();
            runtime.Register(session.Id, adapter);
            adapter.Emit("Do you want to proceed? (Y/n)");
            var hosted = provider.GetRequiredService<WatchdogHostedService>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await hosted.StartAsync(cts.Token);
            await WaitUntilAsync(() => adapter.SentInput.Contains("y\r", StringComparison.Ordinal));
            await hosted.StopAsync(CancellationToken.None);

            eventBus.PublishedEvents.ShouldContain(e => e.EventName == "WatchdogAutoResponded");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Watchdog_does_not_respond_to_same_stale_prompt_after_cooldown_until_prompt_clears()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var session = NewSession(graph.Card);
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            graph.Card.OwnerSessionId = session.Id;
            await db.SaveChangesAsync();

            var adapter = new FakeAgentProtocolAdapter { RenderedScreenOverride = "Press Enter to continue" };
            var eventBus = new MockEventBus();
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            await using var provider = BuildProvider(tempRoot, eventBus);
            var runtime = new AgentSessionRuntime(
                eventBus,
                Options.Create(new AgentSessionSettings
                {
                    SessionLogPath = Path.Combine(tempRoot, "session-logs")
                }),
                provider.GetRequiredService<IServiceScopeFactory>(),
                clock,
                NullLogger<AgentSessionRuntime>.Instance);
            runtime.Register(session.Id, adapter);
            var service = new WatchdogService(
                db,
                runtime,
                new WatchdogMatcher(),
                new WatchdogCooldownStore(),
                eventBus,
                Options.Create(new WatchdogSettings { CooldownMs = 1 }),
                clock,
                NullLogger<WatchdogService>.Instance);

            var first = await service.ScanAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromSeconds(5));
            var stale = await service.ScanAsync(CancellationToken.None);
            adapter.RenderedScreenOverride = "prompt cleared";
            var clear = await service.ScanAsync(CancellationToken.None);
            adapter.RenderedScreenOverride = "Press Enter to continue";
            clock.Advance(TimeSpan.FromSeconds(5));
            var fresh = await service.ScanAsync(CancellationToken.None);

            first.ShouldBe(1);
            stale.ShouldBe(0);
            clear.ShouldBe(0);
            fresh.ShouldBe(1);
            adapter.SentInput.ShouldBe("\r\r");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Watchdog_skips_live_session_when_rendered_snapshot_is_temporarily_unavailable()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var session = NewSession(graph.Card);
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            graph.Card.OwnerSessionId = session.Id;
            await db.SaveChangesAsync();

            var adapter = new FakeAgentProtocolAdapter { ThrowOnRenderedSnapshot = true };
            var eventBus = new MockEventBus();
            await using var provider = BuildProvider(tempRoot, eventBus);
            var runtime = new AgentSessionRuntime(
                eventBus,
                Options.Create(new AgentSessionSettings
                {
                    SessionLogPath = Path.Combine(tempRoot, "session-logs")
                }),
                provider.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                NullLogger<AgentSessionRuntime>.Instance);
            runtime.Register(session.Id, adapter);
            adapter.Emit("Press Enter to continue");
            await WaitUntilAsync(() => runtime.GetBufferSnapshot(session.Id).Buffer.Contains("Press Enter", StringComparison.Ordinal));
            var service = new WatchdogService(
                db,
                runtime,
                new WatchdogMatcher(),
                new WatchdogCooldownStore(),
                eventBus,
                Options.Create(new WatchdogSettings()),
                TimeProvider.System,
                NullLogger<WatchdogService>.Instance);

            var responded = await service.ScanAsync(CancellationToken.None);

            responded.ShouldBe(0);
            adapter.SentInput.ShouldBeEmpty();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Watchdog_clears_previous_active_rule_when_prompt_type_switches()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var session = NewSession(graph.Card);
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            graph.Card.OwnerSessionId = session.Id;
            await db.SaveChangesAsync();

            var adapter = new FakeAgentProtocolAdapter { RenderedScreenOverride = "Press Enter to continue" };
            var eventBus = new MockEventBus();
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            await using var provider = BuildProvider(tempRoot, eventBus);
            var runtime = new AgentSessionRuntime(
                eventBus,
                Options.Create(new AgentSessionSettings
                {
                    SessionLogPath = Path.Combine(tempRoot, "session-logs")
                }),
                provider.GetRequiredService<IServiceScopeFactory>(),
                clock,
                NullLogger<AgentSessionRuntime>.Instance);
            runtime.Register(session.Id, adapter);
            var service = new WatchdogService(
                db,
                runtime,
                new WatchdogMatcher(),
                new WatchdogCooldownStore(),
                eventBus,
                Options.Create(new WatchdogSettings { CooldownMs = 1 }),
                clock,
                NullLogger<WatchdogService>.Instance);

            var enter = await service.ScanAsync(CancellationToken.None);
            adapter.RenderedScreenOverride = "Do you want to proceed? (Y/n)";
            var yes = await service.ScanAsync(CancellationToken.None);
            adapter.RenderedScreenOverride = "Press Enter to continue";
            clock.Advance(TimeSpan.FromSeconds(5));
            var enterAgain = await service.ScanAsync(CancellationToken.None);

            enter.ShouldBe(1);
            yes.ShouldBe(1);
            enterAgain.ShouldBe(1);
            adapter.SentInput.ShouldBe("\ry\r\r");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static ServiceProvider BuildProvider(string tempRoot, MockEventBus eventBus)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            SessionLogPath = Path.Combine(tempRoot, "session-logs")
        }));
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildProviderWithWatchdog(string tempRoot, MockEventBus eventBus)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            SessionLogPath = Path.Combine(tempRoot, "session-logs")
        }));
        services.AddSingleton<IOptions<WatchdogSettings>>(Options.Create(new WatchdogSettings
        {
            ScanIntervalMs = 100,
            CooldownMs = 60_000
        }));
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<WatchdogMatcher>();
        services.AddSingleton<WatchdogCooldownStore>();
        services.AddScoped<WatchdogService>();
        services.AddSingleton<WatchdogHostedService>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static Graph NewGraph(string tempRoot)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Watchdog Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/watchdog.git",
            LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath);
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Watchdog Board {Guid.NewGuid():N}",
            TrackerKind = TrackerKind.Internal,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "in-progress",
            Name = "In Progress",
            ColumnOrder = 0,
            CardStatus = CardStatus.InProgress,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.Columns.Add(column);
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = "CARD-0001",
            Title = "Watchdog card",
            Description = "Watch prompts",
            Status = CardStatus.InProgress,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board,
            BoardColumn = column
        };
        board.Cards.Add(card);
        column.Cards.Add(card);
        return new Graph(project, board, card);
    }

    private static AgentSession NewSession(Card card)
    {
        var now = DateTime.UtcNow;
        return new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            DefinitionName = "watchdog",
            AgentKind = AgentKind.Raw,
            Status = SessionStatus.Running,
            Cwd = "D:/worktrees/watchdog",
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            Card = card
        };
    }

    private static async Task CleanupProjectsByTempRootAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
            return;

        var boardIds = await db.Boards.Where(b => projectIds.Contains(b.ProjectId)).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        var sessionIds = await db.AgentSessions.Where(s => cardIds.Contains(s.CardId)).Select(s => s.Id).ToListAsync();
        await db.Cards
            .Where(c => cardIds.Contains(c.Id))
            .ExecuteUpdateAsync(updates => updates.SetProperty(c => c.OwnerSessionId, (Guid?)null));
        await db.RunAttempts.Where(a => cardIds.Contains(a.CardId)).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(25);
        }

        predicate().ShouldBeTrue();
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-watchdog-tests-{Guid.NewGuid():N}");

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private sealed record Graph(Project Project, Board Board, Card Card);

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
