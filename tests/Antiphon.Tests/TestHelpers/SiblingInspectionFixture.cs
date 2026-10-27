using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal sealed class SiblingInspectionFixture : IAsyncDisposable
{
    public ScratchGitRepo Repo { get; } = new("c1349-scheduling");
    public IsolatedTestSchema Schema { get; private set; } = null!;
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public ServiceProvider Provider { get; private set; } = null!;
    public InspectionGit Git { get; } = new();
    public Card Card { get; private set; } = null!;
    public AgentTask Source { get; private set; } = null!;
    public AgentTask Task { get; private set; } = null!;
    public CardSiblingInspectionQueue Queue => Provider.GetRequiredService<CardSiblingInspectionQueue>();
    public AppDbContext Read() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

    public static async Task<SiblingInspectionFixture> CreateAsync(Func<SiblingInspectionFixture, Task>? onLease = null,
        Action<IServiceCollection>? configure = null)
    {
        var f = new SiblingInspectionFixture();
        await f.Repo.CommitFileAsync("seed.txt", "seed");
        f.Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using (var db = f.Read())
        {
            f.Card = await DispatchSiblingInspectionTests.CardAsync(db);
            f.Source = await DispatchSiblingInspectionTests.SourceAsync(db, f.Repo, f.Card.Id, "source", "master");
            f.Source.Status = AgentTaskStatus.Blocked;
            f.Task = f.NewTask();
            db.AgentTasks.Add(f.Task);
            await db.SaveChangesAsync();
        }
        f.Provider = AgentTaskDispatchBaseGuardTests.CreateProvider(f.Schema.ConnectionString, f.Repo.WorktreeRoot,
            git: f.Git, clock: f.Clock, onLeaseAcquired: onLease is null ? null : () => onLease(f), configure: configure);
        await new CardSiblingInspectionHostedService(f.Queue).StartAsync(default);
        return f;
    }

    public AgentTask NewTask()
    {
        var id = Guid.NewGuid();
        return new AgentTask { Id = id, RootTaskId = id, Title = "inspection recipient", Goal = "inspect before launch",
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = Repo.Path, RepoPath = Repo.Path,
            CardId = Card.Id, Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
            Status = AgentTaskStatus.Queued, RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = Clock.GetUtcNow().UtcDateTime, ConcurrencyToken = Guid.NewGuid() };
    }
    public async Task<AgentTaskDispatcher.TickResult> TickAsync()
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
    }
    public async Task ObserveAsync()
    {
        await TickAsync();
        await Queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
    }
    public async Task RestartAsync()
    {
        await Provider.DisposeAsync();
        Provider = AgentTaskDispatchBaseGuardTests.CreateProvider(Schema.ConnectionString, Repo.WorktreeRoot,
            git: Git, clock: Clock);
        await new CardSiblingInspectionHostedService(Queue).StartAsync(default);
    }
    public async ValueTask DisposeAsync()
    {
        Git.Release.TrySetResult();
        if (Provider is not null) await Provider.DisposeAsync();
        if (Schema is not null) await Schema.DisposeAsync();
        Repo.Dispose();
    }
}

internal sealed class InspectionGit : LandingGit
{
    public ConcurrentQueue<string[]> Calls { get; } = new();
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource TwoEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void HoldNextWave()
    {
        Blocked.ShouldBe(0, "join the preceding wave before replacing its barrier");
        Release.Task.IsCompleted.ShouldBeTrue();
        Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Block = true;
    }
    public bool Block { get; set; }
    public bool Fail { get; set; }
    public int Blocked => Volatile.Read(ref _blocked);
    public int Canceled => Volatile.Read(ref _canceled);
    private int _blocked;
    private int _canceled;
    public Func<IReadOnlyList<string>, CancellationToken, Task<LandingGitResult?>>? Before { get; set; }
    public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args, CancellationToken ct)
    {
        Calls.Enqueue(args.ToArray());
        if (Before is not null && await Before(args, ct) is { } overridden) return overridden;
        if (args[0] == "for-each-ref")
        {
            if (Fail) throw new IOException("owned inspection I/O failure");
            if (Block)
            {
                if (Interlocked.Increment(ref _blocked) == 2) TwoEntered.TrySetResult();
                Entered.TrySetResult();
                try { await Release.Task.WaitAsync(ct); }
                catch (OperationCanceledException) { Interlocked.Increment(ref _canceled); throw; }
                finally { Interlocked.Decrement(ref _blocked); }
            }
        }
        return await base.RunAsync(repository, args, ct);
    }
}
