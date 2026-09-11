using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0475 S4: isolated DB + controlled ILandingGit. Never constructs LandingGitFixture,
/// ScratchGitRepo, LandingGit, or a real verifier.
/// </summary>
internal sealed class LandingProtocolHarness : IAsyncDisposable
{
    public ControlledLandingGit Git { get; }
    public ProtocolFixture Fixture { get; }
    public IsolatedTestSchema Schema { get; private set; } = null!;
    public ServiceProvider Services { get; private set; } = null!;
    public TimeProvider Clock { get; set; } = TimeProvider.System;
    public AgentTaskLandQueue Queue { get; private set; } = new();
    public ControlledVerifier Verifier { get; } = new();
    public SaveFault Fault { get; } = new();
    public IEventBus Events { get; set; } = new MockEventBus();
    public Action<IServiceCollection>? ConfigureServices { get; set; }
    public SessionMessageQueueService? Messages { get; set; }
    public Microsoft.Extensions.Logging.ILogger<AgentTaskLandService> Logger { get; set; } =
        NullLogger<AgentTaskLandService>.Instance;
    public ControlledWorktreeManager Worktrees { get; }

    public LandingProtocolHarness(string? root = null, Guid? taskId = null)
    {
        Git = new ControlledLandingGit(root, taskId);
        Fixture = new ProtocolFixture(Git);
        Worktrees = new ControlledWorktreeManager(Git);
    }

    public async Task InitializeAsync()
    {
        Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        BuildServices();
        await SeedAsync();
    }

    private void BuildServices()
    {
        Queue = new();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Clock);
        services.AddSingleton<ILandingGit>(Git);
        services.AddSingleton<ILandingVerifier>(Verifier);
        services.AddSingleton<IWorktreeManager>(Worktrees);
        services.AddScoped(_ => CreateContext());
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(Git.Root, "trees") });
        services.AddScoped<AgentTaskLandingProtocol>();
        ConfigureServices?.Invoke(services);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Worktrees.Removal = new GuardedWorktreeRemoval(
            Git,
            Services.GetRequiredService<IRepositoryMutationLease>(),
            Services.GetRequiredService<IWorktreeRemovalEvidence>());
    }

    public async Task RestartServicesAsync()
    {
        await Services.DisposeAsync();
        BuildServices();
    }

    private async Task SeedAsync()
    {
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = Git.TaskId, RootTaskId = Git.TaskId, Title = "C475 fixture", Goal = "fixture",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Git.Repository, RepoPath = Git.Repository, WorktreePath = Git.Source,
            WorktreeBranch = Git.SourceRef[11..], MergeTargetRef = "master", Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>(
        TestDbFixture.CreateDbContextOptions(Schema.ConnectionString)).AddInterceptors(Fault, new TransactionFault(Fault)).Options);

    public Task<LandRunResult> RunAsync() => RunAsync(CancellationToken.None);

    public async Task<LandRunResult> RunAsync(CancellationToken ct)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var observer = CreateContext();
        var seeded = await observer.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == Git.TaskId);
        if (seeded.LandRequestedAt is null)
            await RequestAsync();
        return await CreateLand(db, scope.ServiceProvider).RunAsync(Git.TaskId, null, ct);
    }

    public async Task FailAsync(Exception error)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CreateLand(db, scope.ServiceProvider).FailAsync(Git.TaskId, error, CancellationToken.None);
    }

    internal AgentTaskLandService CreateLand(AppDbContext db, IServiceProvider services)
    {
        var tasks = new AgentTaskService(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxTasksPerRoot = 40, MaxDepth = 5 }), Events,
            new RecordingSessionStopper(), Clock, NullLogger<AgentTaskService>.Instance);
        var git = services.GetRequiredService<ILandingGit>();
        return new AgentTaskLandService(db, services.GetRequiredService<DelegationWorktreeService>(),
            tasks, Queue, Messages!, Events, Clock,
            Options.Create(new DelegationSettings()), Logger,
            services.GetRequiredService<AgentTaskLandingProtocol>(),
            Services.GetRequiredService<IRepositoryMutationLease>(), git);
    }

    public ILandingGit RegisteredGit => Services.GetRequiredService<ILandingGit>();

    public async Task<LandRequestResult> RequestAsync(string? filter = null, string? expectedSourceSha = null,
        Guid? reviewEvidenceId = null)
    {
        expectedSourceSha ??= Git.SourceHead;
        await using var scope = Services.CreateAsyncScope();
        return await CreateLand(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider)
            .RequestAsync(Git.TaskId, new LandAgentTaskRequest(filter, expectedSourceSha, reviewEvidenceId),
                CancellationToken.None);
    }

    public async Task SweepAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await CreateLand(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider)
            .SweepAsync(CancellationToken.None);
    }

    public async Task<LandRunResult> RunQueuedAsync(string? filter = null)
    {
        if (!Queue.TryDequeue(out var request) || request.TaskId != Git.TaskId || request.VerifyFilter != filter)
            throw new InvalidOperationException("Expected exact fixture task/filter queue claim");
        try
        {
            await using var scope = Services.CreateAsyncScope();
            return await CreateLand(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider)
                .RunRequestAsync(request.TaskId, request.RequestId, request.VerifyFilter, CancellationToken.None);
        }
        finally { Queue.Release(request.TaskId); }
    }

    public async Task RepostAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await using var db = CreateContext();
        await RequestAsync();
    }

    public async Task<AgentTaskLanding?> OperationAsync()
    {
        await using var db = CreateContext();
        return await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == Git.TaskId && o.Active).SingleOrDefaultAsync();
    }

    public async Task<string> AddSourceAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(Git.Source, "feature.txt"), "valuable feature\n");
        await Git.RequiredAsync(Git.Source, "add", "feature.txt");
        await Git.RequiredAsync(Git.Source, "commit", "-m", "feature");
        return Git.SourceHead;
    }

    public async ValueTask DisposeAsync()
    {
        if (Services is not null) await Services.DisposeAsync();
        if (Schema is not null) await Schema.DisposeAsync();
        Git.Dispose();
    }

    internal sealed class ProtocolFixture(ControlledLandingGit git)
    {
        public ControlledLandingGit Git { get; } = git;
        public string Root => git.Root;
        public string Repository => git.Repository;
        public string Source => git.Source;
        public string Remote => git.Remote;
        public string SourceRef => git.SourceRef;
        public string TargetRef => git.TargetRef;
        public Guid TaskId => git.TaskId;
        public string SeedSha => git.SeedSha;
        public Task<string> RequiredAsync(string path, params string[] arguments) => git.RequiredAsync(path, arguments);
        public Task AssertRemoteSourceAsync() => git.AssertRemoteSourceAsync();
    }

    internal sealed class ControlledVerifier : ILandingVerifier
    {
        public Func<Task>? Barrier { get; set; }
        public bool Passed { get; set; } = true;
        public int Calls { get; private set; }
        public List<(string Worktree, string? Filter)> Invocations { get; } = [];
        public async Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
        {
            Calls++;
            Invocations.Add((worktree, filter));
            if (Barrier is not null) await Barrier();
            return new(Passed, "fixture verification");
        }
    }

    internal sealed class ControlledWorktreeManager : IWorktreeManager
    {
        private readonly ControlledLandingGit _git;
        public GuardedWorktreeRemoval? Removal { get; set; }
        public List<string> Unsupported { get; } = [];
        public ControlledWorktreeManager(ControlledLandingGit git) => _git = git;

        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
            => throw Record("CreateAsync");

        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, RepositoryLease lease, CancellationToken ct)
            => throw Record("CreateAsync");

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct)
            => throw Record("RemoveAsync");

        public Task<WorktreeRemoval> TryRemoveAsync(string repoPath, string worktreePath, string? mergedInto, CancellationToken ct)
            => throw Record("TryRemoveAsync-legacy");

        public Task<WorktreeRemoval> TryRemoveAsync(WorktreeRemovalRequest request, CancellationToken ct)
            => (Removal ?? throw new InvalidOperationException("guarded removal not bound")).RemoveAsync(request, ct);

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);

        private InvalidOperationException Record(string name)
        {
            Unsupported.Add(name);
            return new InvalidOperationException("unsupported worktree operation: " + name);
        }
    }

    internal sealed class SaveFault : SaveChangesInterceptor
    {
        public LandPhase? Phase { get; set; }
        public Func<AgentTaskLanding, bool>? Matches { get; set; }
        public string? TerminalCut { get; set; }
        public AgentTaskEventType? EventKind { get; set; }
        public bool AfterCommit { get; set; }
        public bool AfterSave { get; set; }
        public Func<DbContext, Task>? AfterSaveAcknowledged { get; set; }
        public bool Triggered { get; private set; }
        public Func<LandPhase, Task>? AfterAcknowledged { get; set; }
        private bool _armed;
        internal bool AwaitingCommit { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            _armed = !Triggered && (TerminalCut is not null
                ? data.Context!.ChangeTracker.Entries<AgentTaskEvent>().Any(e => e.State == EntityState.Added && (EventKind is null ? e.Entity.IsLandTerminal : e.Entity.Type == EventKind))
                : data.Context!.ChangeTracker.Entries<AgentTaskLanding>()
                .Any(e => e.State != EntityState.Unchanged
                    && (Phase is not null && e.Entity.Phase == Phase || Matches?.Invoke(e.Entity) == true)));
            if (_armed && (TerminalCut == "before-save" || TerminalCut is null && !AfterCommit && !AfterSave)) { Triggered = true; throw new InjectedSaveFailure(); }
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (AfterSaveAcknowledged is not null) await AfterSaveAcknowledged(data.Context!);
            if (_armed && (AfterSave || TerminalCut == "after-save")) { Triggered = true; throw new InjectedSaveFailure(); }
            if (_armed && TerminalCut is "commit" or "after-commit") AwaitingCommit = true;
            if (_armed && AfterCommit)
            {
                if (data.Context!.Database.CurrentTransaction is not null) AwaitingCommit = true;
                else { Triggered = true; throw new InjectedSaveFailure(); }
            }
            if (data.Context!.Database.CurrentTransaction is null) await AcknowledgedAsync(data.Context);
            return result;
        }
        internal async Task AcknowledgedAsync(DbContext? context)
        {
            if (AfterAcknowledged is not null && context is not null)
                foreach (var entry in context.ChangeTracker.Entries<AgentTaskLanding>())
                    await AfterAcknowledged(entry.Entity.Phase);
        }
        internal void Committed()
        {
            if (!AwaitingCommit) return;
            AwaitingCommit = false;
            Triggered = true;
            throw new InjectedSaveFailure();
        }
        internal void Committing()
        {
            if (AwaitingCommit && TerminalCut == "commit")
            { AwaitingCommit = false; Triggered = true; throw new InjectedSaveFailure(); }
        }
    }

    private sealed class TransactionFault(SaveFault fault) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(System.Data.Common.DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { fault.Committing(); return ValueTask.FromResult(result); }
        public override async Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { fault.Committed(); await fault.AcknowledgedAsync(eventData.Context); }
    }

    internal sealed class InjectedSaveFailure : Exception;
}
