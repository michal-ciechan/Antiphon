using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

[Category("GitIntegration")]
[NotInParallel("Pty")]
public class AgentSessionServiceIntegrationTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task AgentSessionService_start_to_first_text_delta_with_real_worktree_and_raw_pty()
    {
        SkipIfNotWindows();
        await GitTestEnvironment.SkipIfGitUnavailableAsync();
        await using var db = CreateContext();
        using var git = await GitTestEnvironment.CreateAsync();
        var graph = CreateGraph(git.RepoPath);
        db.Add(graph.Project);
        await db.SaveChangesAsync();

        var eventBus = new MockEventBus();
        await using var provider = BuildProvider();
        var service = BuildService(db, git.WorktreeRoot, eventBus, provider);
        var request = new StartAgentSessionRequest(
            graph.Card.Id,
            "raw-cmd",
            AgentKind.Raw,
            "echo HELLO_E05",
            Cols: 120,
            Rows: 30);
        var spec = new AgentLaunchSpec(
            "raw-cmd",
            AgentKind.Raw,
            Cmd,
            ["/d", "/q", "/k", "@echo off & prompt $G"],
            new Dictionary<string, string>(),
            git.RepoPath,
            120,
            30);

        var result = await service.StartAsync(request, spec, CancellationToken.None);

        result.FirstDeltaReceived.ShouldBeTrue();
        await WaitUntilAsync(() =>
            eventBus.PublishedEvents.Any(e =>
                e.Group == AgentSessionGroups.Session(result.SessionId)
                && e.EventName == "AgentTextDelta"
                && GetPayloadValue<string>(e.Payload, "text").Contains("HELLO_E05", StringComparison.OrdinalIgnoreCase)));
        service.GetBuffer(result.SessionId).ShouldContain("HELLO_E05", Case.Insensitive);

        var session = await db.AgentSessions.SingleAsync(s => s.Id == result.SessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        session.Cwd.ShouldBe(Path.GetFullPath(Path.Combine(git.WorktreeRoot, $"card-{graph.Card.Identifier}")));
        var attempt = await db.RunAttempts.SingleAsync(a => a.Id == result.RunAttemptId);
        attempt.Phase.ShouldBe(RunPhase.Succeeded);
        attempt.CompletedAt.ShouldNotBeNull();
        attempt.WorktreeId.ShouldBe(result.WorktreeId);
        var card = await db.Cards.SingleAsync(c => c.Id == graph.Card.Id);
        card.OwnerSessionId.ShouldBe(result.SessionId);
        card.CurrentWorktreeId.ShouldBe(result.WorktreeId);

        await service.KillAsync(result.SessionId, CancellationToken.None);
    }

    [Test]
    public async Task AgentSessionService_first_delta_timeout_ignores_startup_output_before_prompt()
    {
        await using var db = CreateContext();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-fake-{Guid.NewGuid():N}");
        var repoPath = Path.Combine(tempRoot, "repo");
        var worktreePath = Path.Combine(tempRoot, "worktree");
        Directory.CreateDirectory(repoPath);

        try
        {
            var graph = CreateGraph(repoPath);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var eventBus = new MockEventBus();
            var adapter = new FakeAgentProtocolAdapter
            {
                StartupOutput = "READY_BEFORE_PROMPT",
                NoiseDuringSendPrompt = "LATE_STARTUP_NOISE"
            };
            await using var provider = BuildProvider();
            var (service, _) = BuildServiceWithFakes(
                db,
                eventBus,
                provider,
                adapter,
                worktreePath,
                new AgentSessionSettings
                {
                    FirstDeltaTimeoutMs = 100,
                    KillGraceMs = 100,
                    SignalRMaxChunkChars = 16 * 1024,
                    ReplayBufferMaxChars = 128 * 1024,
                    SessionLogPath = Path.Combine(tempRoot, "session-logs")
                });
            var request = new StartAgentSessionRequest(
                graph.Card.Id,
                "fake",
                AgentKind.Raw,
                "produce output after prompt",
                Cols: 120,
                Rows: 30);
            var spec = new AgentLaunchSpec(
                "fake",
                AgentKind.Raw,
                "fake",
                [],
                new Dictionary<string, string>(),
                repoPath,
                120,
                30);

            var result = await service.StartAsync(request, spec, CancellationToken.None);

            result.FirstDeltaReceived.ShouldBeFalse();
            adapter.SentPrompt.ShouldBe("produce output after prompt");
            adapter.Killed.ShouldBeTrue();
            adapter.Disposed.ShouldBeTrue();
            service.GetBuffer(result.SessionId).ShouldContain("READY_BEFORE_PROMPT");
            service.GetBuffer(result.SessionId).ShouldContain("LATE_STARTUP_NOISE");
            var attempt = await db.RunAttempts.SingleAsync(a => a.Id == result.RunAttemptId);
            attempt.Phase.ShouldBe(RunPhase.TimedOut);
            attempt.CompletedAt.ShouldNotBeNull();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == result.SessionId);
            session.Status.ShouldBe(SessionStatus.Failed);
            session.FailureReason.ShouldBe("Timed out waiting for first agent output.");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task RunAttempt_records_memory_killed_exit_reason()
    {
        await using var db = CreateContext();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-memory-{Guid.NewGuid():N}");
        var repoPath = Path.Combine(tempRoot, "repo");
        var worktreePath = Path.Combine(tempRoot, "worktree");
        Directory.CreateDirectory(repoPath);

        try
        {
            var graph = CreateGraph(repoPath);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var eventBus = new MockEventBus();
            var adapter = new FakeAgentProtocolAdapter
            {
                PromptOutput = "ALLOCATING",
                ExitReason = AgentExitReason.MemoryKilled,
                ExitCode = 137
            };
            await using var provider = BuildProvider();
            var (service, _) = BuildServiceWithFakes(
                db,
                eventBus,
                provider,
                adapter,
                worktreePath,
                new AgentSessionSettings
                {
                    FirstDeltaTimeoutMs = 1_000,
                    KillGraceMs = 100,
                    SignalRMaxChunkChars = 16 * 1024,
                    ReplayBufferMaxChars = 128 * 1024,
                    SessionLogPath = Path.Combine(tempRoot, "session-logs"),
                    MemoryLimitMb = 128
                });
            var request = new StartAgentSessionRequest(
                graph.Card.Id,
                "fake",
                AgentKind.Raw,
                "allocate memory",
                Cols: 120,
                Rows: 30);
            var spec = new AgentLaunchSpec(
                "fake",
                AgentKind.Raw,
                "fake",
                [],
                new Dictionary<string, string>(),
                repoPath,
                120,
                30);

            var result = await service.StartAsync(request, spec, CancellationToken.None);

            adapter.MemoryLimitMb.ShouldBe(128);
            adapter.Disposed.ShouldBeTrue();
            var session = await db.AgentSessions.SingleAsync(s => s.Id == result.SessionId);
            session.Status.ShouldBe(SessionStatus.Failed);
            session.FailureReason.ShouldNotBeNull();
            session.FailureReason.ShouldContain("MemoryKilled");
            session.ExitCode.ShouldBe(137);
            var attempt = await db.RunAttempts.SingleAsync(a => a.Id == result.RunAttemptId);
            attempt.Phase.ShouldBe(RunPhase.Failed);
            attempt.ErrorDetails.ShouldNotBeNull();
            attempt.ErrorDetails.ShouldContain("MemoryKilled");
            attempt.ExitCode.ShouldBe(137);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task RunAttempt_uses_definition_version_at_launch_not_current()
    {
        await using var db = CreateContext();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-pin-{Guid.NewGuid():N}");
        var repoPath = Path.Combine(tempRoot, "repo");
        var worktreePath = Path.Combine(tempRoot, "worktree");
        Directory.CreateDirectory(repoPath);

        try
        {
            var graph = CreateGraph(repoPath);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var pinnedDefinition = await db.BoardWorkflowDefinitions.SingleAsync(d => d.BoardId == graph.Card.BoardId);
            pinnedDefinition.Name = "Pinned V1";
            pinnedDefinition.Content = """
                ---
                name: Pinned V1
                ---
                V1 prompt for {{ issue.title }} on {{ workspace.branch }}
                """;
            pinnedDefinition.IsActive = false;
            var currentDefinition = new BoardWorkflowDefinition
            {
                Id = Guid.NewGuid(),
                BoardId = graph.Card.BoardId,
                Version = 2,
                Name = "Current V2",
                Content = """
                    ---
                    name: Current V2
                    ---
                    V2 prompt for {{ issue.title }}
                    """,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Board = graph.Card.Board
            };
            db.BoardWorkflowDefinitions.Add(currentDefinition);
            await db.SaveChangesAsync();

            var eventBus = new MockEventBus();
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "PIN_OK" };
            await using var provider = BuildProvider();
            var (service, _) = BuildServiceWithFakes(
                db,
                eventBus,
                provider,
                adapter,
                worktreePath,
                new AgentSessionSettings
                {
                    FirstDeltaTimeoutMs = 1_000,
                    KillGraceMs = 100,
                    SignalRMaxChunkChars = 16 * 1024,
                    ReplayBufferMaxChars = 128 * 1024,
                    SessionLogPath = Path.Combine(tempRoot, "session-logs")
                });
            var request = new StartAgentSessionRequest(
                graph.Card.Id,
                "fake",
                AgentKind.Raw,
                "fallback prompt",
                Cols: 120,
                Rows: 30,
                BoardWorkflowDefinitionId: pinnedDefinition.Id,
                UseWorkflowPrompt: true);
            var spec = new AgentLaunchSpec(
                "fake",
                AgentKind.Raw,
                "fake",
                [],
                new Dictionary<string, string>(),
                repoPath,
                120,
                30);

            var result = await service.StartAsync(request, spec, CancellationToken.None);

            adapter.SentPrompt.ShouldContain("V1 prompt");
            adapter.SentPrompt.ShouldContain($"feat/card-{graph.Card.Identifier}");
            adapter.SentPrompt.ShouldNotContain("V2 prompt");
            var attempt = await db.RunAttempts.SingleAsync(a => a.Id == result.RunAttemptId);
            attempt.BoardWorkflowDefinitionId.ShouldBe(pinnedDefinition.Id);
            attempt.Prompt.ShouldBe(adapter.SentPrompt);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task AgentSessionService_kill_marks_active_attempt_canceled_and_disposes_adapter()
    {
        await using var db = CreateContext();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-kill-{Guid.NewGuid():N}");
        var repoPath = Path.Combine(tempRoot, "repo");
        var worktreePath = Path.Combine(tempRoot, "worktree");
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(worktreePath);

        try
        {
            var graph = CreateGraph(repoPath);
            db.Add(graph.Project);
            await db.SaveChangesAsync();
            var now = DateTime.UtcNow;
            var worktree = new Worktree
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                RepoPath = repoPath,
                Path = worktreePath,
                Branch = $"feat/card-{graph.Card.Identifier}",
                BaseRef = "main",
                Status = WorktreeStatus.Active,
                CreatedAt = now,
                LastTouchedAt = now,
                Card = graph.Card
            };
            var session = new AgentSession
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                WorktreeId = worktree.Id,
                DefinitionName = "fake",
                AgentKind = AgentKind.Raw,
                Status = SessionStatus.Running,
                Cwd = worktreePath,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                Card = graph.Card,
                Worktree = worktree
            };
            var attempt = new RunAttempt
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                AgentSessionId = session.Id,
                WorktreeId = worktree.Id,
                AttemptNumber = 1,
                Phase = RunPhase.StreamingTurn,
                CreatedAt = now,
                StartedAt = now,
                LastEventAt = now,
                PhaseStartedAt = now,
                Prompt = "long running",
                Card = graph.Card,
                AgentSession = session,
                Worktree = worktree
            };
            db.AddRange(worktree, session, attempt);
            await db.SaveChangesAsync();

            var eventBus = new MockEventBus();
            var adapter = new FakeAgentProtocolAdapter();
            await using var provider = BuildProvider();
            var (service, runtime) = BuildServiceWithFakes(
                db,
                eventBus,
                provider,
                adapter,
                worktreePath,
                new AgentSessionSettings
                {
                    FirstDeltaTimeoutMs = 100,
                    KillGraceMs = 100,
                    SignalRMaxChunkChars = 16 * 1024,
                    ReplayBufferMaxChars = 128 * 1024,
                    SessionLogPath = Path.Combine(tempRoot, "session-logs")
                });
            runtime.Register(session.Id, adapter);

            await service.KillAsync(session.Id, CancellationToken.None);

            adapter.Killed.ShouldBeTrue();
            adapter.Disposed.ShouldBeTrue();
            session.Status.ShouldBe(SessionStatus.Stopped);
            session.EndedAt.ShouldNotBeNull();
            attempt.Phase.ShouldBe(RunPhase.Canceled);
            attempt.CompletedAt.ShouldNotBeNull();
            eventBus.PublishedEvents.Single(e => e.EventName == "SessionExited")
                .Group.ShouldBe(AgentSessionGroups.Session(session.Id));
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task AgentSessionService_kill_force_kills_after_grace_period()
    {
        await using var db = CreateContext();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-kill-timeout-{Guid.NewGuid():N}");
        var repoPath = Path.Combine(tempRoot, "repo");
        var worktreePath = Path.Combine(tempRoot, "worktree");
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(worktreePath);

        try
        {
            var graph = CreateGraph(repoPath);
            db.Add(graph.Project);
            await db.SaveChangesAsync();
            var now = DateTime.UtcNow;
            var worktree = new Worktree
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                RepoPath = repoPath,
                Path = worktreePath,
                Branch = $"feat/card-{graph.Card.Identifier}",
                BaseRef = "main",
                Status = WorktreeStatus.Active,
                CreatedAt = now,
                LastTouchedAt = now,
                Card = graph.Card
            };
            var session = new AgentSession
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                WorktreeId = worktree.Id,
                DefinitionName = "fake",
                AgentKind = AgentKind.Raw,
                Status = SessionStatus.Running,
                Cwd = worktreePath,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                Card = graph.Card,
                Worktree = worktree
            };
            var attempt = new RunAttempt
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                AgentSessionId = session.Id,
                WorktreeId = worktree.Id,
                AttemptNumber = 1,
                Phase = RunPhase.StreamingTurn,
                CreatedAt = now,
                StartedAt = now,
                LastEventAt = now,
                PhaseStartedAt = now,
                Prompt = "long running",
                Card = graph.Card,
                AgentSession = session,
                Worktree = worktree
            };
            db.AddRange(worktree, session, attempt);
            await db.SaveChangesAsync();

            var eventBus = new MockEventBus();
            var adapter = new FakeAgentProtocolAdapter { KillResult = false };
            await using var provider = BuildProvider();
            var (service, runtime) = BuildServiceWithFakes(
                db,
                eventBus,
                provider,
                adapter,
                worktreePath,
                new AgentSessionSettings
                {
                    FirstDeltaTimeoutMs = 100,
                    KillGraceMs = 100,
                    SignalRMaxChunkChars = 16 * 1024,
                    ReplayBufferMaxChars = 128 * 1024,
                    SessionLogPath = Path.Combine(tempRoot, "session-logs")
                });
            runtime.Register(session.Id, adapter);

            await service.KillAsync(session.Id, CancellationToken.None);

            adapter.Killed.ShouldBeTrue();
            adapter.Disposed.ShouldBeTrue();
            session.Status.ShouldBe(SessionStatus.Failed);
            session.FailureReason.ShouldBe("Agent process did not exit within the configured grace period.");
            attempt.Phase.ShouldBe(RunPhase.Failed);
            attempt.ErrorDetails.ShouldBe(session.FailureReason);
            eventBus.PublishedEvents.Single(e => e.EventName == "SessionExited")
                .Group.ShouldBe(AgentSessionGroups.Session(session.Id));
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task AgentSessionService_turn_completion_timeout_marks_session_failed_and_retry_reuses_worktree()
    {
        await using var db = CreateContext();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-retry-{Guid.NewGuid():N}");
        var repoPath = Path.Combine(tempRoot, "repo");
        var worktreePath = Path.Combine(tempRoot, "worktree");
        Directory.CreateDirectory(repoPath);

        try
        {
            var graph = CreateGraph(repoPath);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var settings = new AgentSessionSettings
            {
                FirstDeltaTimeoutMs = 1_000,
                KillGraceMs = 100,
                SignalRMaxChunkChars = 16 * 1024,
                ReplayBufferMaxChars = 128 * 1024,
                SessionLogPath = Path.Combine(tempRoot, "session-logs")
            };
            var eventBus = new MockEventBus();
            await using var firstProvider = BuildProvider();
            var firstAdapter = new FakeAgentProtocolAdapter
            {
                PromptOutput = "FIRST_DELTA",
                TurnCompleted = false
            };
            var (firstService, _) = BuildServiceWithFakes(
                db,
                eventBus,
                firstProvider,
                firstAdapter,
                worktreePath,
                settings);
            var request = new StartAgentSessionRequest(
                graph.Card.Id,
                "fake",
                AgentKind.Raw,
                "first attempt",
                Cols: 120,
                Rows: 30);
            var spec = new AgentLaunchSpec(
                "fake",
                AgentKind.Raw,
                "fake",
                [],
                new Dictionary<string, string>(),
                repoPath,
                120,
                30);

            var firstResult = await firstService.StartAsync(request, spec, CancellationToken.None);

            firstResult.FirstDeltaReceived.ShouldBeTrue();
            firstAdapter.Killed.ShouldBeTrue();
            firstAdapter.Disposed.ShouldBeTrue();
            var firstSession = await db.AgentSessions.SingleAsync(s => s.Id == firstResult.SessionId);
            firstSession.Status.ShouldBe(SessionStatus.Failed);
            firstSession.FailureReason.ShouldBe("Timed out waiting for the agent turn to complete.");
            var firstAttempt = await db.RunAttempts.SingleAsync(a => a.Id == firstResult.RunAttemptId);
            firstAttempt.Phase.ShouldBe(RunPhase.TimedOut);

            await using var secondProvider = BuildProvider();
            var secondAdapter = new FakeAgentProtocolAdapter { PromptOutput = "SECOND_DELTA" };
            var (secondService, _) = BuildServiceWithFakes(
                db,
                eventBus,
                secondProvider,
                secondAdapter,
                worktreePath,
                settings);

            var secondResult = await secondService.StartAsync(
                request with { Prompt = "second attempt" },
                spec,
                CancellationToken.None);

            secondResult.FirstDeltaReceived.ShouldBeTrue();
            secondResult.WorktreeId.ShouldBe(firstResult.WorktreeId);
            var secondAttempt = await db.RunAttempts.SingleAsync(a => a.Id == secondResult.RunAttemptId);
            secondAttempt.Phase.ShouldBe(RunPhase.Succeeded);
            secondAttempt.AttemptNumber.ShouldBe(2);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static void SkipIfNotWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("Raw PTY integration uses Windows ConPTY in this test.");
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        return services.BuildServiceProvider();
    }

    private static AgentSessionService BuildService(
        AppDbContext db,
        string worktreeRoot,
        MockEventBus eventBus,
        ServiceProvider provider)
    {
        var sessionSettings = Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 5_000,
            KillGraceMs = 2_000,
            SignalRMaxChunkChars = 16 * 1024,
            ReplayBufferMaxChars = 128 * 1024,
            SessionLogPath = Path.Combine(worktreeRoot, "session-logs")
        });
        var runtime = new AgentSessionRuntime(
            eventBus,
            sessionSettings,
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
        var hookRunner = new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance);
        var hookService = new WorkspaceHookService(hookRunner, NullLogger<WorkspaceHookService>.Instance);
        var worktreeManager = new WorktreeManager(
            Options.Create(new GitSettings
            {
                WorktreeBasePath = worktreeRoot,
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24
            }),
            TimeProvider.System,
            NullLogger<WorktreeManager>.Instance);
        var adapterFactory = new AgentProtocolAdapterFactory(Options.Create(new AgentRegistrySettings
        {
            DefaultDefinition = "raw-cmd",
            Definitions = { ["raw-cmd"] = new AgentDefinition { Kind = "Raw", Exe = Cmd } }
        }));

        return new AgentSessionService(
            db,
            worktreeManager,
            hookService,
            adapterFactory,
            runtime,
            eventBus,
            sessionSettings,
            TimeProvider.System,
            NullLogger<AgentSessionService>.Instance);
    }

    private static (AgentSessionService Service, AgentSessionRuntime Runtime) BuildServiceWithFakes(
        AppDbContext db,
        MockEventBus eventBus,
        ServiceProvider provider,
        IAgentProtocolAdapter adapter,
        string worktreePath,
        AgentSessionSettings settings)
    {
        var sessionSettings = Options.Create(settings);
        var runtime = new AgentSessionRuntime(
            eventBus,
            sessionSettings,
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
        var hookRunner = new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance);
        var hookService = new WorkspaceHookService(hookRunner, NullLogger<WorkspaceHookService>.Instance);
        var service = new AgentSessionService(
            db,
            new FakeWorktreeManager(worktreePath),
            hookService,
            new FakeAdapterFactory(adapter),
            runtime,
            eventBus,
            sessionSettings,
            TimeProvider.System,
            NullLogger<AgentSessionService>.Instance);

        return (service, runtime);
    }

    private static Graph CreateGraph(string repoPath)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = repoPath,
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Board {Guid.NewGuid():N}",
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.Columns.Add(column);
        var definition = new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = 1,
            Name = "Default",
            Content = """
                name: E05
                stages:
                  - name: Run
                    executorType: raw
                """,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.WorkflowDefinitions.Add(definition);
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = $"E05-{Guid.NewGuid():N}"[..20],
            Title = "Start raw PTY",
            Status = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board,
            BoardColumn = column
        };
        board.Cards.Add(card);
        column.Cards.Add(card);
        return new Graph(project, card);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(50);
        }

        predicate().ShouldBeTrue();
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp worktree/session directories.
        }
    }

    private static T GetPayloadValue<T>(object payload, string propertyName)
    {
        var value = payload.GetType().GetProperty(propertyName)!.GetValue(payload);
        return value.ShouldBeOfType<T>();
    }

    private sealed record Graph(Project Project, Card Card);

    private sealed class FakeAdapterFactory : IAgentProtocolAdapterFactory
    {
        private readonly IAgentProtocolAdapter _adapter;

        public FakeAdapterFactory(IAgentProtocolAdapter adapter)
        {
            _adapter = adapter;
        }

        public IAgentProtocolAdapter Create(AgentKind kind) => _adapter;
    }

    private sealed class FakeWorktreeManager : IWorktreeManager
    {
        private readonly string _worktreePath;

        public FakeWorktreeManager(string worktreePath)
        {
            _worktreePath = worktreePath;
        }

        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(_worktreePath);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new WorktreeInfo(
                cardId,
                repoPath,
                _worktreePath,
                $"feat/card-{cardId}",
                baseRef,
                now,
                now));
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) =>
            Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) =>
            Task.FromResult(0);
    }

    private sealed class GitTestEnvironment : IDisposable
    {
        private GitTestEnvironment(string tempRoot, string repoPath, string worktreeRoot)
        {
            TempRoot = tempRoot;
            RepoPath = repoPath;
            WorktreeRoot = worktreeRoot;
        }

        public string TempRoot { get; }
        public string RepoPath { get; }
        public string WorktreeRoot { get; }

        public static async Task SkipIfGitUnavailableAsync()
        {
            try
            {
                await RunProcessAsync(Environment.CurrentDirectory, "git", ["--version"], throwOnError: true);
            }
            catch (Exception ex)
            {
                throw new SkipTestException($"git is required for AgentSessionService integration tests: {ex.Message}");
            }
        }

        public static async Task<GitTestEnvironment> CreateAsync()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-tests-{Guid.NewGuid():N}");
            var repoPath = Path.Combine(tempRoot, "repo");
            var worktreeRoot = Path.Combine(tempRoot, "worktrees");
            Directory.CreateDirectory(repoPath);
            Directory.CreateDirectory(worktreeRoot);

            await RunGitAsync(repoPath, "init");
            await RunGitAsync(repoPath, "config", "user.email", "test@antiphon.dev");
            await RunGitAsync(repoPath, "config", "user.name", "Antiphon Test");
            await File.WriteAllTextAsync(Path.Combine(repoPath, "README.md"), "# Test Repo");
            await RunGitAsync(repoPath, "add", "README.md");
            await RunGitAsync(repoPath, "commit", "-m", "Initial commit");
            await RunGitAsync(repoPath, "branch", "-M", "main");

            return new GitTestEnvironment(tempRoot, repoPath, worktreeRoot);
        }

        private static Task<string> RunGitAsync(string workingDirectory, params string[] arguments) =>
            RunProcessAsync(workingDirectory, "git", arguments, throwOnError: true);

        private static async Task<string> RunProcessAsync(
            string workingDirectory,
            string fileName,
            IReadOnlyList<string> arguments,
            bool throwOnError)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start {fileName}.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (throwOnError && process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"{fileName} {string.Join(" ", arguments)} failed with exit code {process.ExitCode}: {stderr}");

            return stdout;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(TempRoot))
                {
                    foreach (var file in Directory.EnumerateFiles(TempRoot, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);

                    Directory.Delete(TempRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for PTY/git integration tests.
            }
        }
    }
}
