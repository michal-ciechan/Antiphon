using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
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
[NotInParallel("AgentControl")]
public class AgentControlServiceIntegrationTests
{
    [Test]
    public async Task Start_with_remote_control_boots_queue_head_and_sends_rename_then_remote_control_before_work()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "BOOTED" };
            await using var harness = BuildHarness(tempRoot, [adapter]);

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Remote Control Board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Wire the thing"), CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Remote Claude", Path.Combine(tempRoot, "agent-workspace"), DefaultWorkflowTemplateId: template.Id),
                CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(card.Id), CancellationToken.None);

            var detail = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(RemoteControl: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            detail.Status.ShouldBe(AgentStatus.Working);
            detail.CurrentCardId.ShouldBe(card.Id);
            detail.PersistentSessionId.ShouldNotBeNull();

            // The rename + remote-control commands must arrive before the work prompt.
            adapter.Prompts.Count.ShouldBe(3);
            adapter.Prompts[0].ShouldBe("/rename Remote Claude");
            adapter.Prompts[1].ShouldBe("/remote-control");
            adapter.Prompts[2].ShouldNotBeNullOrWhiteSpace();
            adapter.Prompts[2].ShouldNotStartWith("/remote-control");

            await using var verify = CreateContext();
            var session = await verify.AgentSessions.SingleAsync(s => s.Id.ToString() == detail.PersistentSessionId);
            session.Status.ShouldBe(SessionStatus.Running);
            session.CardId.ShouldBe(card.Id);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_without_remote_control_sends_only_the_work_prompt()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "BOOTED" };
            await using var harness = BuildHarness(tempRoot, [adapter]);

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Plain Board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Plain work"), CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Plain Claude", Path.Combine(tempRoot, "agent-workspace"), DefaultWorkflowTemplateId: template.Id),
                CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(card.Id), CancellationToken.None);

            await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            adapter.Prompts.Count.ShouldBe(1);
            adapter.Prompts[0].ShouldNotStartWith("/rename");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_is_idempotent_when_a_live_session_already_exists()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            var template = NewWorkflowTemplate(tempRoot);
            db.Projects.Add(project);
            db.WorkflowTemplates.Add(template);
            await db.SaveChangesAsync();
            // Only one adapter is queued: a second spawn would throw "no fake adapter queued",
            // so a no-op second start is proven by the absence of that throw.
            var adapter = new FakeAgentProtocolAdapter { PromptOutput = "BOOTED" };
            await using var harness = BuildHarness(tempRoot, [adapter]);

            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Idempotent Board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Only-once work"), CancellationToken.None);
            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Once Claude", Path.Combine(tempRoot, "agent-workspace"), DefaultWorkflowTemplateId: template.Id),
                CancellationToken.None);
            await harness.AgentService.AssignCardAsync(
                agent.Id, new AssignAgentCardRequest(card.Id), CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            var second = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);

            second.PersistentSessionId.ShouldBe(first.PersistentSessionId);
            await using var verify = CreateContext();
            var sessionCount = await verify.AgentSessions.CountAsync(s => s.CardId == card.Id);
            sessionCount.ShouldBe(1);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_interactive_resumes_previous_claude_session_by_default()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var resumeAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [firstAdapter, resumeAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Resume Claude", workspace), CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            first.PersistentSessionId.ShouldNotBeNull();
            firstAdapter.StartedArgs.ShouldContain("--session-id");
            firstAdapter.StartedArgs.ShouldContain(first.PersistentSessionId);

            await MarkSessionEndedAsync(first.PersistentSessionId!, SessionStatus.Stopped);

            // A fresh scope mirrors a new HTTP request — no stale tracked entities.
            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var second = await control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            // Same session id, relaunched with --resume: the terminal picks up where it left off.
            second.PersistentSessionId.ShouldBe(first.PersistentSessionId);
            resumeAdapter.StartedArgs.ShouldContain("--resume");
            resumeAdapter.StartedArgs.ShouldContain(first.PersistentSessionId);
            resumeAdapter.StartedArgs.ShouldNotContain("--session-id");

            await using var verify = CreateContext();
            var sessions = await verify.AgentSessions.Where(s => s.Cwd == workspace).ToListAsync();
            sessions.Count.ShouldBe(1);
            sessions[0].Status.ShouldBe(SessionStatus.Running);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_interactive_falls_back_to_fresh_session_when_claude_conversation_is_missing()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var failingResumeAdapter = new FakeAgentProtocolAdapter { ReadyResult = false };
            var freshAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(
                tempRoot, [firstAdapter, failingResumeAdapter, freshAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Fallback Claude", workspace), CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            failingResumeAdapter.StartupOutput = $"No conversation found with session ID: {first.PersistentSessionId}";

            await MarkSessionEndedAsync(first.PersistentSessionId!, SessionStatus.Stopped);

            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var second = await control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            // The --resume attempt reported a missing conversation, so the launch fell back to a
            // fresh conversation under the same session id.
            failingResumeAdapter.StartedArgs.ShouldContain("--resume");
            failingResumeAdapter.Disposed.ShouldBeTrue();
            freshAdapter.StartedArgs.ShouldContain("--session-id");
            freshAdapter.StartedArgs.ShouldContain(first.PersistentSessionId);
            second.PersistentSessionId.ShouldBe(first.PersistentSessionId);

            await using var verify = CreateContext();
            var session = await verify.AgentSessions.SingleAsync(s => s.Id.ToString() == first.PersistentSessionId);
            session.Status.ShouldBe(SessionStatus.Running);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Start_interactive_with_fresh_request_starts_a_new_session()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var firstAdapter = new FakeAgentProtocolAdapter();
            var freshAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [firstAdapter, freshAdapter], defaultKind: "ClaudeCode");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Fresh Claude", workspace), CancellationToken.None);

            var first = await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            await MarkSessionEndedAsync(first.PersistentSessionId!, SessionStatus.Stopped);

            using var scope = harness.Provider.CreateScope();
            var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
            var second = await control.StartAsync(
                agent.Id, new StartAgentRequest(Fresh: true), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            second.PersistentSessionId.ShouldNotBeNull();
            second.PersistentSessionId.ShouldNotBe(first.PersistentSessionId);
            freshAdapter.StartedArgs.ShouldContain("--session-id");
            freshAdapter.StartedArgs.ShouldContain(second.PersistentSessionId);
            freshAdapter.StartedArgs.ShouldNotContain("--resume");

            await using var verify = CreateContext();
            var sessionCount = await verify.AgentSessions.CountAsync(s => s.Cwd == workspace);
            sessionCount.ShouldBe(2);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static async Task MarkSessionEndedAsync(string sessionId, SessionStatus status)
    {
        await using var db = CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id.ToString() == sessionId);
        session.Status = status;
        session.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(
        string tempRoot, IReadOnlyList<IAgentProtocolAdapter> adapters, string defaultKind = "Raw")
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 1_000,
            KillGraceMs = 100,
            SignalRMaxChunkChars = 16 * 1024,
            ReplayBufferMaxChars = 128 * 1024,
            SessionLogPath = Path.Combine(tempRoot, "session-logs")
        }));
        services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
        {
            InternalTrackerRepositoryPathPrefix = tempRoot
        }));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
        {
            DefaultDefinition = "fake",
            Definitions = { ["fake"] = new AgentDefinition { Kind = defaultKind, Exe = "fake" } }
        }));
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IWorktreeManager>(new FakeWorktreeManager(Path.Combine(tempRoot, "worktrees")));
        services.AddSingleton<IAgentProtocolAdapterFactory>(new QueueAdapterFactory(adapters));
        services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddScoped<OrchestratorService>();
        services.AddScoped<CardWorkflowRunFactory>();
        services.AddScoped<AgentService>();
        services.AddScoped<AgentControlService>();
        services.AddSingleton<Antiphon.Server.Application.Interfaces.IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(new System.IO.Abstractions.FileSystem()));
        services.AddScoped<BoardService>();
        services.AddScoped<CardService>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Harness(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<BoardService>(),
            scope.ServiceProvider.GetRequiredService<CardService>(),
            scope.ServiceProvider.GetRequiredService<AgentService>(),
            scope.ServiceProvider.GetRequiredService<AgentControlService>(),
            provider.GetRequiredService<AgentSessionLaunchQueue>(),
            eventBus);
    }

    private static Project NewProject(string tempRoot)
    {
        var repoPath = Path.Combine(tempRoot, "repo");
        Directory.CreateDirectory(repoPath);
        var now = DateTime.UtcNow;
        return new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = repoPath,
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static WorkflowTemplate NewWorkflowTemplate(string tempRoot)
    {
        var now = DateTime.UtcNow;
        return new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            Name = $"Agent Template {Guid.NewGuid():N}",
            Description = tempRoot,
            YamlDefinition = """
                name: One Shot
                description: Implement then review
                stages:
                  - name: Implement
                    executorType: agent
                    gateRequired: false
                  - name: Human Review
                    executorType: human
                    gateRequired: true
                """,
            IsBuiltIn = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-agent-control-tests-{Guid.NewGuid():N}");

    private static async Task CleanupProjectsByTempRootAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var workflowTemplateIds = await db.WorkflowTemplates
            .Where(t => t.Description == tempRoot)
            .Select(t => t.Id)
            .ToListAsync();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
        {
            // Interactive-only tests have no project: clean their agents and cardless sessions directly.
            await db.Agents
                .Where(a => a.WorkingDirectory.StartsWith(tempRoot))
                .ExecuteUpdateAsync(updates => updates.SetProperty(a => a.PersistentSessionId, (string?)null));
            await db.AgentSessions.Where(s => s.CardId == null && s.Cwd.StartsWith(tempRoot)).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.WorkingDirectory.StartsWith(tempRoot)).ExecuteDeleteAsync();
            await db.WorkflowTemplates.Where(t => workflowTemplateIds.Contains(t.Id)).ExecuteDeleteAsync();
            return;
        }

        var boardIds = await db.Boards
            .Where(b => projectIds.Contains(b.ProjectId))
            .Select(b => b.Id)
            .ToListAsync();
        var cardIds = await db.Cards
            .Where(c => boardIds.Contains(c.BoardId))
            .Select(c => c.Id)
            .ToListAsync();
        var sessionIds = await db.AgentSessions
            .Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value))
            .Select(s => s.Id)
            .ToListAsync();
        var workflowRunIds = await db.CardWorkflowRuns
            .Where(r => cardIds.Contains(r.CardId))
            .Select(r => r.Id)
            .ToListAsync();
        var agentIds = await db.Agents
            .Where(a => a.WorkingDirectory.StartsWith(tempRoot)
                || (a.CurrentCardId != null && cardIds.Contains(a.CurrentCardId.Value))
                || db.Cards.Any(c => cardIds.Contains(c.Id) && c.AssignedAgentId == a.Id)
                || db.CardWorkflowRuns.Any(r => workflowRunIds.Contains(r.Id) && r.AgentId == a.Id))
            .Select(a => a.Id)
            .ToListAsync();
        var attemptIds = await db.RunAttempts
            .Where(a => cardIds.Contains(a.CardId))
            .Select(a => a.Id)
            .ToListAsync();
        var worktreeIds = await db.Worktrees
            .Where(w => cardIds.Contains(w.CardId))
            .Select(w => w.Id)
            .ToListAsync();

        // Agents may reference a board (default board) and cards reference agents/sessions/runs —
        // null the cross-links before deleting so FK constraints don't block the teardown.
        await db.Agents
            .Where(a => agentIds.Contains(a.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(a => a.CurrentCardId, (Guid?)null)
                .SetProperty(a => a.BoardId, (Guid?)null)
                .SetProperty(a => a.PersistentSessionId, (string?)null));
        await db.Cards
            .Where(c => cardIds.Contains(c.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(c => c.OwnerSessionId, (Guid?)null)
                .SetProperty(c => c.CurrentWorktreeId, (Guid?)null)
                .SetProperty(c => c.AssignedAgentId, (Guid?)null)
                .SetProperty(c => c.ActiveWorkflowRunId, (Guid?)null));
        await db.CardWorkflowRuns
            .Where(r => workflowRunIds.Contains(r.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(r => r.CurrentStageId, (Guid?)null));
        await db.CardWorkflowStages.Where(s => workflowRunIds.Contains(s.CardWorkflowRunId)).ExecuteDeleteAsync();
        await db.CardWorkflowRuns.Where(r => workflowRunIds.Contains(r.Id)).ExecuteDeleteAsync();
        await db.TokenUsages.Where(t => attemptIds.Contains(t.RunAttemptId)).ExecuteDeleteAsync();
        await db.RetrySchedules.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.RunAttempts.Where(a => attemptIds.Contains(a.Id)).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        // Cardless interactive sessions are keyed only by their Cwd inside the temp root.
        await db.AgentSessions.Where(s => s.CardId == null && s.Cwd.StartsWith(tempRoot)).ExecuteDeleteAsync();
        await db.Worktrees.Where(w => worktreeIds.Contains(w.Id)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Agents.Where(a => agentIds.Contains(a.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
        await db.WorkflowTemplates.Where(t => workflowTemplateIds.Contains(t.Id)).ExecuteDeleteAsync();
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

    private sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        BoardService BoardService,
        CardService CardService,
        AgentService AgentService,
        AgentControlService Control,
        AgentSessionLaunchQueue LaunchQueue,
        MockEventBus EventBus) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private sealed class QueueAdapterFactory : IAgentProtocolAdapterFactory
    {
        private readonly Queue<IAgentProtocolAdapter> _adapters;

        public QueueAdapterFactory(IEnumerable<IAgentProtocolAdapter> adapters)
        {
            _adapters = new Queue<IAgentProtocolAdapter>(adapters);
        }

        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            if (_adapters.TryDequeue(out var adapter))
                return adapter;

            throw new InvalidOperationException("No fake adapter was queued for dispatch.");
        }
    }

    private sealed class FakeWorktreeManager : IWorktreeManager
    {
        private readonly string _worktreeRoot;

        public FakeWorktreeManager(string worktreeRoot)
        {
            _worktreeRoot = worktreeRoot;
        }

        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(_worktreeRoot);
            var worktreePath = Path.Combine(_worktreeRoot, $"card-{cardId}");
            Directory.CreateDirectory(worktreePath);
            var now = DateTimeOffset.UtcNow;
            var info = new WorktreeInfo(cardId, repoPath, worktreePath, $"feat/card-{cardId}", baseRef, now, now);
            return Task.FromResult(info);
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
    {
        public OptionsMonitorStub(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
