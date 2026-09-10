using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

internal sealed class LandingSafetyHarness : IAsyncDisposable
{
    public LandingGitFixture Fixture { get; }
    public LandingSafetyHarness(string? root = null, Guid? taskId = null) => Fixture = new(root, taskId);
    public IsolatedTestSchema Schema { get; private set; } = null!;
    public ServiceProvider Services { get; private set; } = null!;
    public ControlledVerifier Verifier { get; } = new();
    public SaveFault Fault { get; } = new();
    public IEventBus Events { get; set; } = new MockEventBus();
    public Action<IServiceCollection>? ConfigureServices { get; set; }
    public SessionMessageQueueService? Messages { get; set; }
    public Microsoft.Extensions.Logging.ILogger<AgentTaskLandService> Logger { get; set; } = NullLogger<AgentTaskLandService>.Instance;

    public async Task InitializeAsync()
    {
        await Fixture.InitializeAsync();
        Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        BuildServices();
        await SeedAsync();
    }

    private void BuildServices()
    {
        Fixture.Git.BeforeObservedCommand = async arguments =>
        {
            if (!arguments.Any(a => a is "rebase" or "merge" or "push" or "remove" or "update-ref")) return;
            // Separate connection at the mutation boundary: a tracked phase is not a receipt.
            await using var observer = CreateContext();
            var committed = await observer.AgentTaskLandings.AsNoTracking()
                .Where(o => o.TaskId == Fixture.TaskId && o.Active)
                .Select(o => new { o.Id, o.Phase, o.Publication, o.Cleanup, o.OriginalSourceSha,
                    o.RebasedSourceSha, o.VerifiedSourceSha, o.TargetBeforeSha, o.LocalTargetAfterSha,
                    o.RemoteConfirmedAt, o.ObservedRemoteTargetSha, o.CleanupStartedAt, o.ExpectedDeletionSha,
                    o.SourcePinned, o.TargetPinned, o.PreparedPinned, o.ChildOperation, o.ChildProcessId,
                    o.ChildProcessStartTicks }).SingleOrDefaultAsync();
            LandingEvidence.Write(Fixture.TaskId, "committed_before_mutation", new { arguments, committed });
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILandingGit>(Fixture.Git);
        services.AddSingleton<ILandingVerifier>(Verifier);
        services.AddScoped(_ => CreateContext());
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(Fixture.Root, "trees") });
        services.AddScoped<AgentTaskLandingProtocol>();
        ConfigureServices?.Invoke(services);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public async Task RestartServicesAsync()
    {
        await Services.DisposeAsync();
        BuildServices(); // New lease provider and graph; no tracked/in-process receipt is reused.
    }

    private async Task SeedAsync()
    {
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = Fixture.TaskId, RootTaskId = Fixture.TaskId, Title = "C448 fixture", Goal = "fixture",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Fixture.Repository, RepoPath = Fixture.Repository, WorktreePath = Fixture.Source,
            WorktreeBranch = Fixture.SourceRef[11..], MergeTargetRef = "master", Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
            LandRequestedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // Invoked in a separate OS process by the crash tests. The parent exclusively owns fixture
    // database/repository lifetime; this worker must not drop or dispose those shared fixtures.
    public static async Task RunCrashWorkerAsync(string root, string taskId, string cut, string ready)
    {
        var connection = Environment.GetEnvironmentVariable("ANTIPHON_C448_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Missing private fixture database");
        var h = new LandingSafetyHarness(root, Guid.Parse(taskId));
        h.Schema = new IsolatedTestSchema("worker-observer-only", connection);
        h.BuildServices();
        async Task PauseAsync()
        {
            await File.WriteAllTextAsync(ready, System.Text.Json.JsonSerializer.Serialize(new { cut, worker = Environment.ProcessId }));
            await Task.Delay(Timeout.Infinite);
        }
        h.Fault.AfterAcknowledged = async phase =>
        {
            if (cut == "C14" && phase == LandPhase.PublicationConfirmed) await PauseAsync();
        };
        h.Fixture.Git.AfterCommand = async (_, args, result) =>
        {
            if ((cut == "C03" && result.Succeeded && args[0] == "update-ref" && args.Any(a => a.EndsWith("/source", StringComparison.Ordinal)))
                || (cut == "C05" && args.Contains("rebase"))
                || (cut == "C09" && result.Succeeded && args.Contains("merge") && args.Contains("--ff-only"))
                || (cut == "C12" && result.Succeeded && args[0] == "push")
                || (cut == "C16" && result.Succeeded && args.Contains("worktree") && args.Contains("remove"))
                || (cut == "C17" && result.Succeeded && args[0] == "update-ref" && args.Contains("-d")))
                await PauseAsync();
        };
        await h.RunAsync();
        if (cut == "resume")
        {
            await File.WriteAllTextAsync(ready + ".resume-trace.json", System.Text.Json.JsonSerializer.Serialize(h.Fixture.Git.Trace));
            return;
        }
        throw new InvalidOperationException("Crash boundary was not reached: " + cut);
    }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>(
        TestDbFixture.CreateDbContextOptions(Schema.ConnectionString)).AddInterceptors(Fault, new TransactionFault(Fault)).Options);

    public Task<LandRunResult> RunAsync() => RunAsync(CancellationToken.None);

    public async Task<LandRunResult> RunAsync(CancellationToken ct)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Fixture.CaptureAsync("before_service");
        try { return await CreateLand(db, scope.ServiceProvider).RunAsync(Fixture.TaskId, null, ct); }
        finally
        {
            await Fixture.CaptureAsync("after_service");
            if (LandingEvidence.Enabled)
            {
                await using var observer = CreateContext();
                var operations = await observer.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == Fixture.TaskId).ToListAsync();
                var events = await observer.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == Fixture.TaskId).ToListAsync();
                var task = await observer.AgentTasks.AsNoTracking().Where(t => t.Id == Fixture.TaskId)
                    .Select(t => new { t.Id, t.ActiveLandingId, t.RepoPath, t.WorktreePath, t.WorktreeBranch, t.MergeTargetRef,
                        t.Status, t.LandAttempt, t.LandRequestedAt, t.LandStartedAt, t.LandVerifyFilter }).SingleOrDefaultAsync();
                var stages = await observer.StageOutcomes.AsNoTracking().Where(o => o.SubjectTaskId == Fixture.TaskId).ToListAsync();
                LandingEvidence.Write(Fixture.TaskId, "committed_after_service", new { task, operations, events, stages });
            }
        }
    }

    public async Task FailAsync(Exception error)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await CreateLand(db, scope.ServiceProvider).FailAsync(Fixture.TaskId, error, CancellationToken.None);
    }

    private AgentTaskLandService CreateLand(AppDbContext db, IServiceProvider services)
    {
        var tasks = new AgentTaskService(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxTasksPerRoot = 40, MaxDepth = 5 }), Events,
            new RecordingSessionStopper(), TimeProvider.System, NullLogger<AgentTaskService>.Instance);
        return new AgentTaskLandService(db, services.GetRequiredService<DelegationWorktreeService>(),
            tasks, new AgentTaskLandQueue(), Messages!, Events, TimeProvider.System,
            Options.Create(new DelegationSettings()), Logger,
            services.GetRequiredService<AgentTaskLandingProtocol>(),
            Services.GetRequiredService<IRepositoryMutationLease>(), Fixture.Git);
    }

    public async Task RepostAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await using var db = CreateContext();
        await CreateLand(db, scope.ServiceProvider).RequestAsync(Fixture.TaskId, null, CancellationToken.None);
    }

    public async Task<AgentTaskLanding?> OperationAsync()
    {
        await using var db = CreateContext();
        return await db.AgentTaskLandings.AsNoTracking().Where(o => o.TaskId == Fixture.TaskId && o.Active).SingleOrDefaultAsync();
    }

    public async Task<string> AddSourceAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(Fixture.Source, "feature.txt"), "valuable feature\n");
        await Fixture.RequiredAsync(Fixture.Source, "add", ".");
        await Fixture.RequiredAsync(Fixture.Source, "commit", "-m", "feature");
        return (await Fixture.RequiredAsync(Fixture.Source, "rev-parse", "HEAD")).Trim();
    }

    public async ValueTask DisposeAsync()
    {
        if (Services is not null) await Services.DisposeAsync();
        if (Schema is not null) await Schema.DisposeAsync();
        await Fixture.DisposeAsync();
    }

    internal sealed class ControlledVerifier : ILandingVerifier
    {
        public Func<Task>? Barrier { get; set; }
        public bool Passed { get; set; } = true;
        public int Calls { get; private set; }
        public async Task<LandingVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
        {
            Calls++;
            if (Barrier is not null) await Barrier();
            return new(Passed, "fixture verification");
        }
    }

    internal sealed class SaveFault : SaveChangesInterceptor
    {
        public LandPhase? Phase { get; set; }
        public Func<AgentTaskLanding, bool>? Matches { get; set; }
        public string? TerminalCut { get; set; }
        public bool AfterCommit { get; set; }
        public bool Triggered { get; private set; }
        public Func<LandPhase, Task>? AfterAcknowledged { get; set; }
        private bool _armed;
        internal bool AwaitingCommit { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            _armed = !Triggered && (TerminalCut is not null
                ? data.Context!.ChangeTracker.Entries<AgentTaskEvent>().Any(e => e.State == EntityState.Added && e.Entity.IsLandTerminal)
                : data.Context!.ChangeTracker.Entries<AgentTaskLanding>()
                .Any(e => e.State != EntityState.Unchanged
                    && (Phase is not null && e.Entity.Phase == Phase || Matches?.Invoke(e.Entity) == true)));
            if (_armed && (TerminalCut == "before-save" || TerminalCut is null && !AfterCommit)) { Triggered = true; throw new InjectedSaveFailure(); }
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (_armed && TerminalCut == "after-save") { Triggered = true; throw new InjectedSaveFailure(); }
            if (_armed && TerminalCut is "commit" or "after-commit") AwaitingCommit = true;
            if (_armed && AfterCommit)
            {
                if (data.Context!.Database.CurrentTransaction is not null) AwaitingCommit = true;
                else { Triggered = true; throw new InjectedSaveFailure(); }
            }
            if (AfterAcknowledged is not null)
                foreach (var entry in data.Context!.ChangeTracker.Entries<AgentTaskLanding>())
                    await AfterAcknowledged(entry.Entity.Phase);
            return result;
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
        public override Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { fault.Committed(); return Task.CompletedTask; }
    }

    internal sealed class InjectedSaveFailure : Exception;
}
