using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public sealed class WorktreeResidueRegistrationTests
{
    private readonly AntiphonWebAppFactory _factory;
    public WorktreeResidueRegistrationTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task C459_OneScheduler()
    {
        var janitorHostedServices = _factory.Services.GetServices<IHostedService>()
            .Where(s => s is WorktreeJanitorHostedService)
            .ToList();
        janitorHostedServices.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_DisabledSchedulerStaysOff()
    {
        var storage = _factory.Services.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        var residueRecurringJobs = connection.GetRecurringJobs()
            .Where(j => j.Id.Contains("worktree-residue", StringComparison.OrdinalIgnoreCase))
            .ToList();
        residueRecurringJobs.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_ScheduledWorkerExecutesBothLanes()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = DateTime.UtcNow;
        var settledId = Guid.NewGuid();
        var publishedId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = settledId, RootTaskId = settledId, Title = "settled", Goal = "settled",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = @"C:\trees\card-task-settled", WorktreePath = @"C:\trees\card-task-settled",
                WorktreeBranch = "feat/card-task-settled", Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = now.AddHours(-5), CompletedAt = now.AddHours(-3), Result = "done",
            });
            db.TaskWorktreeRetirements.Add(new TaskWorktreeRetirement
            {
                Id = Guid.NewGuid(), TaskId = settledId, TaskAttempt = 1, TerminalStatus = AgentTaskStatus.Succeeded,
                TaskCompletedAt = now.AddHours(-3), ReleasedTaskRevision = Guid.NewGuid(), CallerIdentity = "op",
                ReleaseReason = "x", ReleasedAt = now, RepositoryPath = @"C:\repo", CommonDirectory = @"C:\repo\.git",
                WorktreePath = @"C:\trees\card-task-settled", GitDirectory = @"C:\repo\.git",
                SourceFullRef = "refs/heads/feat/card-task-settled", SourceSha = new string('a', 40),
                TargetFullRef = "refs/heads/master", State = WorktreeRetirementState.Released, Active = true, UpdatedAt = now,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = publishedId, RootTaskId = publishedId, Title = "published", Goal = "published",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = @"C:\trees\card-task-pub", WorktreePath = @"C:\trees\card-task-pub",
                Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = now.AddHours(-5), CompletedAt = now.AddHours(-3),
            });
            db.AgentTaskLandings.Add(new AgentTaskLanding
            {
                Id = operationId, TaskId = publishedId, SchemaVersion = 1, Active = true,
                Publication = LandPublicationOutcome.Landed, Cleanup = LandCleanupStatus.Pending,
                RepositoryPath = @"C:\repo", WorktreePath = @"C:\trees\card-task-pub", CommonDirectory = @"C:\repo",
                GitDirectory = @"C:\repo\.git", SourceFullRef = "refs/heads/feat/x", TargetFullRef = "refs/heads/master",
                OriginalSourceSha = new string('a', 40), TargetBeforeSha = new string('b', 40),
                RecoveryRefPrefix = $"refs/antiphon/land/{publishedId:N}/{operationId:N}",
                CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var storage = new InMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings()));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)));
        services.AddSingleton(Options.Create(new WorktreeResidueSettings { Execute = false, MaxActionsPerRun = 25, MinSettledMinutes = 120 }));
        services.AddSingleton(Options.Create(new GitSettings { DefaultBranch = "master" }));
        services.AddSingleton<IWorktreeManager>(new EmptyWorktrees());
        services.AddScoped<WorktreeResidueSweepService>();
        services.AddScoped<TaskWorktreeRetirementService>();
        services.AddScoped<WorktreeResidueJob>();
        services.AddHangfire(config => config.UseStorage(storage));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        GlobalConfiguration.Configuration.UseStorage(storage).UseActivator(new ScopeJobActivator(provider));
        var manager = new RecurringJobManager(storage);
        HangfireConfiguration.AddOrUpdateWorktreeResidueJob(manager, new WorktreeResidueSettings());
        using (var connection = storage.GetConnection())
            connection.GetRecurringJobs().ShouldHaveSingleItem().Id.ShouldBe("antiphon:worktree-residue");

        var client = new BackgroundJobClient(storage);
        client.Enqueue<WorktreeResidueJob>(job => job.ExecuteAsync(CancellationToken.None));
        using (var server = new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 1, SchedulePollingInterval = TimeSpan.FromMilliseconds(50) }, storage))
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!budget.IsCancellationRequested)
            {
                await using var probe = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                if (await probe.WorktreeResidueRuns.AnyAsync(budget.Token)) break;
                await Task.Delay(50, budget.Token);
            }
        }

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var run = await verify.WorktreeResidueRuns.AsNoTracking().OrderBy(r => r.StartedAt).LastAsync();
        run.FinishedAt.ShouldNotBeNull();
        var lanes = await verify.WorktreeResidueRunCandidates.AsNoTracking()
            .Where(c => c.RunId == run.Id).Select(c => c.Lane).ToListAsync();
        lanes.ShouldContain(nameof(WorktreeResidueLane.SettledTask));
        lanes.ShouldContain(nameof(WorktreeResidueLane.Publication));

        client.Enqueue<WorktreeResidueJob>(job => job.ExecuteAsync(CancellationToken.None));
        using (var server = new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 1, SchedulePollingInterval = TimeSpan.FromMilliseconds(50) }, storage))
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!budget.IsCancellationRequested)
            {
                await using var probe = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                if (await probe.WorktreeResidueRuns.CountAsync(budget.Token) >= 2) break;
                await Task.Delay(50, budget.Token);
            }
        }

        (await verify.WorktreeResidueRuns.CountAsync()).ShouldBeGreaterThanOrEqualTo(2);
    }

    private sealed class ScopeJobActivator(IServiceProvider provider) : JobActivator
    {
        public override object ActivateJob(Type jobType) => provider.CreateScope().ServiceProvider.GetRequiredService(jobType);

        public override JobActivatorScope BeginScope(JobActivatorContext context) => new ProviderScope(provider);

        private sealed class ProviderScope(IServiceProvider provider) : JobActivatorScope
        {
            private readonly IServiceScope _scope = provider.CreateScope();
            public override object Resolve(Type type) => _scope.ServiceProvider.GetRequiredService(type);
            public override void DisposeScope() => _scope.Dispose();
        }
    }

    private sealed class EmptyWorktrees : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
