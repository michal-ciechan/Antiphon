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
    private const string C566Menu = "Bash command\nrm -rf $R/$name\n"
        + "│ Dangerous rm operation on possibly-empty variable path: $R/$name in `rm -rf\n"
        + "│ $R/$name` (rewrite it as safe variables)\n"
        + "Do you want to proceed?\n❯ 1. Yes\n  2. No\nEsc to cancel · Tab to amend";

    [Test]
    public async Task C566_Unsafe_delete_refusal_requires_the_active_exact_menu()
    {
        var cases = new (string Name, string Screen, AgentKind Kind, bool Expected)[]
        {
            ("captured", "old (Y/n)\n" + C566Menu, AgentKind.ClaudeCode, true),
            ("ansi", C566Menu.Replace("Dangerous", "\u001b[31mDangerous\u001b[0m"), AgentKind.ClaudeCode, true),
            ("unrelated", C566Menu.Replace("Dangerous rm operation on possibly-empty variable path:", "Approve command:"), AgentKind.ClaudeCode, false),
            ("missing question", C566Menu.Replace("Do you want to proceed?", "Continue?"), AgentKind.ClaudeCode, false),
            ("reversed", C566Menu.Replace("1. Yes", "1. No").Replace("2. No", "2. Yes"), AgentKind.ClaudeCode, false),
            ("partial", C566Menu.Replace("  2. No", ""), AgentKind.ClaudeCode, false),
            ("extra choice", C566Menu.Replace("Esc to cancel", "  3. Maybe\nEsc to cancel"), AgentKind.ClaudeCode, false),
            ("history", C566Menu + "\n❯ ", AgentKind.ClaudeCode, false),
            ("provider", C566Menu, AgentKind.Raw, false),
        };
        foreach (var row in cases)
        {
            await using var fixture = await C566Fixture.CreateAsync(row.Screen, row.Kind);
            var sent = await fixture.Service.ScanAsync(CancellationToken.None);
            sent.ShouldBe(row.Expected ? 1 : 0, row.Name);
            fixture.Adapter.Inputs.ShouldBe(row.Expected ? ["2"] : [], row.Name);
            fixture.Bus.PublishedEvents.Count(e => e.EventName == "WatchdogAutoResponded")
                .ShouldBe(row.Expected ? 1 : 0, row.Name);
        }
    }

    [Test]
    public async Task C566_Unsafe_delete_refusal_preserves_a_pending_answer()
    {
        foreach (var answer in new[] { "1", "2", "custom" })
        {
            await using var fixture = await C566Fixture.CreateAsync("old (Y/n)\n" + C566Menu);
            fixture.Adapter.EchoTypedInputToScreen = false;
            await fixture.Runtime.SendInputAsync(fixture.Session.Id, answer, CancellationToken.None, trackManualTurn: false);
            var before = fixture.Adapter.Inputs.ToArray();
            (await fixture.Service.ScanAsync(CancellationToken.None)).ShouldBe(0);
            fixture.Clock.Advance(TimeSpan.FromMinutes(5));
            (await fixture.Service.ScanAsync(CancellationToken.None)).ShouldBe(0);
            fixture.Adapter.Inputs.ShouldBe(before);
            fixture.Bus.PublishedEvents.Count(e => e.EventName == "WatchdogAutoResponded").ShouldBe(0);
            await fixture.Runtime.SendInputAsync(fixture.Session.Id, new string('\b', answer.Length),
                CancellationToken.None, trackManualTurn: false);
            (await fixture.Service.ScanAsync(CancellationToken.None)).ShouldBe(1);
            fixture.Adapter.Inputs.Last().ShouldBe("2");
        }
        await using var rendered = await C566Fixture.CreateAsync(C566Menu.Replace("❯ 1. Yes", "❯ typed 1\n  1. Yes"));
        (await rendered.Service.ScanAsync(CancellationToken.None)).ShouldBe(0);
        rendered.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C566_Unsafe_delete_refusal_sends_verified_no_once_per_episode()
    {
        await using var fixture = await C566Fixture.CreateAsync(C566Menu);
        (await fixture.Service.ScanAsync(CancellationToken.None)).ShouldBe(1);
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        (await fixture.Service.ScanAsync(CancellationToken.None)).ShouldBe(0);
        fixture.Adapter.RenderedScreenOverride = "idle";
        (await fixture.Service.ScanAsync(CancellationToken.None)).ShouldBe(0);
        fixture.Adapter.RenderedScreenOverride = C566Menu;
        (await fixture.Service.ScanAsync(CancellationToken.None)).ShouldBe(1);
        fixture.Adapter.Inputs.ShouldBe(["2", "2"]);
        fixture.Bus.PublishedEvents.Count(e => e.EventName == "WatchdogAutoResponded").ShouldBe(2);
    }

    [Test]
    public async Task C566_Auto_refused_delete_finishes_failed_with_reason_and_caller_receipt()
    {
        var harness = OverdueSweepHarness.Create();
        await using var dispatcherProvider = harness.Provider;
        await using var fixture = await C566Fixture.CreateAsync(C566Menu, dispatcher: harness.Dispatcher);
        var parent = NewSession(fixture.Session.Card!);
        parent.AgentKind = AgentKind.ClaudeCode;
        fixture.Db.AgentSessions.Add(parent);
        await fixture.Db.SaveChangesAsync();
        await C566SeedEntryAsync(parent.Id, 1, TranscriptKinds.UserPrompt, "dispatch delegate");
        await C566SeedEntryAsync(parent.Id, 2, TranscriptKinds.AssistantText, "Dispatched.");
        await C566SeedEntryAsync(parent.Id, 3, TranscriptKinds.TurnEnd, null);
        var parentAdapter = new FakeAgentProtocolAdapter
        {
            ClaudeComposerChrome = true,
            OnSubmitted = async body =>
            {
                await using var parentDb = CreateContext();
                var seq = (await parentDb.TranscriptEntries
                    .Where(t => t.AgentSessionId == parent.Id).MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
                await C566SeedEntryAsync(parent.Id, seq, TranscriptKinds.UserPrompt, body);
                await C566SeedEntryAsync(parent.Id, seq + 1, TranscriptKinds.TurnEnd, null);
            }
        };
        harness.Provider.GetRequiredService<AgentSessionRuntime>().Register(parent.Id, parentAdapter);
        var taskId = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "unsafe cleanup",
            Goal = "run tests", Role = AgentTaskRole.Code, ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = fixture.Root,
            AgentSessionId = fixture.Session.Id, Status = AgentTaskStatus.Working,
            Attempt = 1, ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = parent.Id,
            CreatedAt = DateTime.UtcNow, DispatchedAt = DateTime.UtcNow
        };
        fixture.Db.AgentTasks.Add(task);
        await fixture.Db.SaveChangesAsync();
        (await fixture.Service.ScanAsync(CancellationToken.None)).ShouldBe(1);
        await using var verify = CreateContext();
        var saved = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        saved.Status.ShouldBe(AgentTaskStatus.Failed);
        saved.FailureReason.ShouldContain("Unsafe cleanup approval");
        saved.FailureReason.ShouldContain("watchdog selected No");
        saved.FailureReason.ShouldContain(fixture.Session.Id.ToString());
        fixture.Adapter.Inputs.ShouldBe(["2"]);
        harness.Stopper.Killed.ShouldBeEmpty();
        var note = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskId == task.Id);
        note.ConversationKey.ShouldBe($"task:{task.RootTaskId:N}");
        note.ContentDigest.ShouldBe(DelegationNoteDigest.Compute(saved.FailureReason!));
        var prompts = await verify.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == parent.Id && t.Kind == TranscriptKinds.UserPrompt)
            .ToListAsync();
        prompts.Count(t => t.Text == note.Body).ShouldBe(1);
        parentAdapter.Inputs.ShouldContain("\r");
    }
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

    private sealed class C566Fixture : IAsyncDisposable
    {
        public required string Root { get; init; }
        public required AppDbContext Db { get; init; }
        public required AgentSession Session { get; init; }
        public required FakeAgentProtocolAdapter Adapter { get; init; }
        public required MockEventBus Bus { get; init; }
        public required MutableTimeProvider Clock { get; init; }
        public required ServiceProvider Provider { get; init; }
        public required AgentSessionRuntime Runtime { get; init; }
        public required WatchdogService Service { get; init; }

        public static async Task<C566Fixture> CreateAsync(string rendered,
            AgentKind kind = AgentKind.ClaudeCode, AgentTaskDispatcher? dispatcher = null)
        {
            var root = NewTempRoot();
            var db = CreateContext();
            var graph = NewGraph(root);
            var session = NewSession(graph.Card);
            session.AgentKind = kind;
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            graph.Card.OwnerSessionId = session.Id;
            await db.SaveChangesAsync();
            var bus = new MockEventBus();
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var provider = BuildProvider(root, bus);
            var runtime = new AgentSessionRuntime(bus,
                Options.Create(new AgentSessionSettings { SessionLogPath = Path.Combine(root, "session-logs") }),
                provider.GetRequiredService<IServiceScopeFactory>(), clock,
                NullLogger<AgentSessionRuntime>.Instance);
            var adapter = new FakeAgentProtocolAdapter
            {
                RenderedScreenOverride = rendered,
                EchoTypedInputToScreen = false // the measured modal consumes '2', not the composer
            };
            runtime.Register(session.Id, adapter);
            var service = new WatchdogService(db, runtime, new WatchdogMatcher(),
                new WatchdogCooldownStore(), bus, Options.Create(new WatchdogSettings { CooldownMs = 1_000 }),
                clock, NullLogger<WatchdogService>.Instance, dispatcher);
            return new C566Fixture { Root = root, Db = db, Session = session, Adapter = adapter,
                Bus = bus, Clock = clock, Provider = provider, Runtime = runtime, Service = service };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Provider.DisposeAsync();
            await CleanupProjectsByTempRootAsync(Root);
            DeleteDirectoryBestEffort(Root);
        }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static async Task C566SeedEntryAsync(Guid sessionId, long sequence, string kind, string? body)
    {
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = sequence,
            Kind = kind, Text = body, CreatedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow,
            StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null
        });
        await db.SaveChangesAsync();
    }

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
        var sessionIds = await db.AgentSessions.Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value)).Select(s => s.Id).ToListAsync();
        var taskIds = await db.AgentTasks.Where(t => t.AgentSessionId != null && sessionIds.Contains(t.AgentSessionId.Value))
            .Select(t => t.Id).ToListAsync();
        await db.SessionQueuedMessages.Where(m => sessionIds.Contains(m.AgentSessionId) ||
            (m.SourceTaskId != null && taskIds.Contains(m.SourceTaskId.Value))).ExecuteDeleteAsync();
        await db.AgentTaskEvents.Where(e => taskIds.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
        await db.AgentTasks.Where(t => taskIds.Contains(t.Id)).ExecuteDeleteAsync();
        await db.TranscriptEntries.Where(e => sessionIds.Contains(e.AgentSessionId)).ExecuteDeleteAsync();
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
