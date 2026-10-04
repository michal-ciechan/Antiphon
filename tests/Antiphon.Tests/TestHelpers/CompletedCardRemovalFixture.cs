using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Stops after real durable admission, then exercises the guarded filesystem boundary.</summary>
internal sealed class CompletedCardRemovalFixture(CompletedCardCleanupFixture card) : IAsyncDisposable
{
    public CompletedCardCleanupFixture Card { get; } = card;
    public LandingSafetyHarness Host => Card.Host;
    public LandingGitFixture Git => Host.Fixture;
    public string Tree => Card.Tree;
    public string Ref => "refs/heads/feat/card-task-" + Card.TaskId.ToString("N")[..8];
    public TaskWorktreeRetirement Retirement { get; private set; } = null!;
    public int FullInspections => Git.Git.Commands.Count(c => c.Directory == Tree && c.Arguments[0] == "status");
    public int RemovalCalls => Git.Git.Trace.Count(a => a.Contains("--registration-only"));

    public static async Task<CompletedCardRemovalFixture> CreateAsync()
    {
        var h = new CompletedCardRemovalFixture(await CompletedCardCleanupFixture.CreateAsync());
        try
        {
            await Should.ThrowAsync<IntentPause>(() => h.Card.CleanupAsync(e =>
                e.AfterIntentAsync = _ => throw new IntentPause()));
            await using var db = h.Host.CreateContext();
            h.Retirement = await db.TaskWorktreeRetirements.AsNoTracking().SingleAsync(r => r.TaskId == h.Card.TaskId && r.Active);
            h.Retirement.CommandIntentId.ShouldNotBeNull();
            h.Git.Git.Trace.Clear(); h.Git.Git.Commands.Clear();
            return h;
        }
        catch { await h.DisposeAsync(); throw; }
    }

    public async Task<RepositoryLease> LeaseAsync()
    {
        var lease = await Host.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(Git.Repository, CancellationToken.None);
        return lease.ShouldNotBeNull();
    }

    public WorktreeRemovalRequest Request(RepositoryLease lease) => new(WorktreeRemovalPurpose.SettledTask,
        new(Card.TaskId, Git.Repository, Tree, Ref, Retirement.TargetFullRef), Retirement.CommonDirectory,
        Retirement.GitDirectory, Retirement.SourceSha, Retirement.ObservedTargetSha!, null, lease,
        ManagedRoot: Path.Combine(Git.Root, "trees"), RetirementId: Retirement.Id,
        HasDeletionIntent: true, CardDoneEndpointId: Card.EndpointId);

    public async Task<WorktreeRemoval> RemoveAsync(Func<WorktreeRemovalRequest, WorktreeRemovalRequest>? change = null)
    {
        await using var lease = await LeaseAsync();
        var request = Request(lease);
        return await Host.Services.GetRequiredService<GuardedWorktreeRemoval>()
            .RemoveAsync(change?.Invoke(request) ?? request, CancellationToken.None);
    }

    public async Task AssertHeldAsync(WorktreeRemoval result)
    {
        result.IsClean.ShouldBeFalse();
        Directory.Exists(Tree).ShouldBeTrue();
        (await Card.SentinelAsync()).ShouldBe(Card.OriginalBytes);
        RemovalCalls.ShouldBe(0);
    }

    public async Task<string> WriteAsync(string relative, string bytes = "protected fixture bytes\n")
    {
        var path = Path.Combine(Tree, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, bytes);
        return path;
    }

    public void BeforeStatus(int ordinal, Func<Task> action)
    {
        var seen = 0;
        Git.Git.BeforeCommand = async (repo, args) =>
        {
            if (repo == Tree && args[0] == "status" && ++seen == ordinal) await action();
            return null;
        };
    }

    public async Task<string> CommitAsync(string relative = "unique.txt")
    {
        await WriteAsync(relative, "unique committed source\n");
        await Git.RequiredAsync(Tree, "add", relative);
        await Git.RequiredAsync(Tree, "commit", "-m", "fixture unique commit");
        return (await Git.RequiredAsync(Tree, "rev-parse", "HEAD")).Trim();
    }

    public ValueTask DisposeAsync() => Card.DisposeAsync();
    private sealed class IntentPause : Exception;
}
