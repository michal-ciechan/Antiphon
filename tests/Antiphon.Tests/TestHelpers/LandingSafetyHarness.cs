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
    public LandingGitFixture Fixture { get; } = new();
    public IsolatedTestSchema Schema { get; private set; } = null!;
    public ServiceProvider Services { get; private set; } = null!;
    public ControlledVerifier Verifier { get; } = new();
    public SaveFault Fault { get; } = new();

    public async Task InitializeAsync()
    {
        await Fixture.InitializeAsync();
        Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILandingGit>(Fixture.Git);
        services.AddSingleton<ILandingVerifier>(Verifier);
        services.AddScoped(_ => CreateContext());
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Path.Combine(Fixture.Root, "trees") });
        services.AddScoped<AgentTaskLandingProtocol>();
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
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

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>(
        TestDbFixture.CreateDbContextOptions(Schema.ConnectionString)).AddInterceptors(Fault).Options);

    public async Task<LandRunResult> RunAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tasks = new AgentTaskService(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxTasksPerRoot = 40, MaxDepth = 5 }), new MockEventBus(),
            new RecordingSessionStopper(), TimeProvider.System, NullLogger<AgentTaskService>.Instance);
        var land = new AgentTaskLandService(db, scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>(),
            tasks, new AgentTaskLandQueue(), null!, new MockEventBus(), TimeProvider.System,
            Options.Create(new DelegationSettings()), NullLogger<AgentTaskLandService>.Instance,
            scope.ServiceProvider.GetRequiredService<AgentTaskLandingProtocol>(),
            Services.GetRequiredService<IRepositoryMutationLease>(), Fixture.Git);
        return await land.RunAsync(Fixture.TaskId, null, CancellationToken.None);
    }

    public async Task RepostAsync()
    {
        await using var db = CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == Fixture.TaskId);
        task.LandRequestedAt = DateTime.UtcNow;
        task.LandStartedAt = null;
        task.LandAttempt = 0;
        await db.SaveChangesAsync();
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
        public bool AfterCommit { get; set; }
        public bool Triggered { get; private set; }
        private bool _armed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            _armed = !Triggered && Phase is not null && data.Context!.ChangeTracker.Entries<AgentTaskLanding>()
                .Any(e => e.Entity.Phase == Phase && e.State != EntityState.Unchanged);
            if (_armed && !AfterCommit) { Triggered = true; throw new InjectedSaveFailure(); }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (_armed && AfterCommit) { Triggered = true; throw new InjectedSaveFailure(); }
            return ValueTask.FromResult(result);
        }
    }

    internal sealed class InjectedSaveFailure : Exception;
}
