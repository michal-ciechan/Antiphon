using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Real Git + PostgreSQL retirement receipt for SettledTask removal.</summary>
internal sealed class SettledRemovalHarness : IAsyncDisposable
{
    public LandingSafetyHarness Host { get; }
    public Guid RetirementId { get; private set; }
    public string SourceSha { get; private set; } = "";
    public string TargetSha { get; private set; } = "";
    public string CommonDirectory { get; private set; } = "";
    public string GitDirectory { get; private set; } = "";
    public string Fingerprint { get; private set; } = "";
    public string ManagedRoot => Path.Combine(Host.Fixture.Root, "trees");
    public string NamedWorktree { get; private set; } = "";
    public string NamedRef { get; private set; } = "";

    private SettledRemovalHarness(LandingSafetyHarness host) => Host = host;

    public static async Task<SettledRemovalHarness> CreateAsync(bool namedLeaf = false)
    {
        var host = new LandingSafetyHarness();
        await host.InitializeAsync();
        var h = new SettledRemovalHarness(host);
        h.SourceSha = (await host.Fixture.RequiredAsync(host.Fixture.Source, "rev-parse", "HEAD")).Trim();
        h.TargetSha = (await host.Fixture.RequiredAsync(host.Fixture.Repository, "rev-parse", "HEAD")).Trim();
        h.CommonDirectory = await host.Fixture.Git.CommonDirectoryAsync(host.Fixture.Repository, CancellationToken.None);
        var inspect = await host.Fixture.Git.InspectAsync(host.Fixture.Coordinates, CancellationToken.None);
        inspect.Accepted.ShouldBeTrue(inspect.Reason);
        h.GitDirectory = inspect.Snapshot!.GitDirectory;
        var destination = await host.Fixture.Git.DestinationAsync(host.Fixture.Repository, host.Fixture.TargetRef, CancellationToken.None);
        h.Fingerprint = destination.Fingerprint;
        if (namedLeaf)
        {
            var shortId = DelegationReportFormatter.Short(host.Fixture.TaskId);
            h.NamedWorktree = Path.Combine(h.ManagedRoot, "card-task-" + shortId);
            h.NamedRef = "refs/heads/feat/card-task-" + shortId;
            await host.Fixture.RequiredAsync(host.Fixture.Repository, "worktree", "add", "-b",
                "feat/card-task-" + shortId, h.NamedWorktree, "HEAD");
            await using var db = host.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == host.Fixture.TaskId);
            task.WorktreePath = h.NamedWorktree;
            task.WorktreeBranch = "feat/card-task-" + shortId;
            task.WorktreeBaseSha = h.SourceSha;
            task.RepoPath = host.Fixture.Repository;
            task.CompletedAt = DateTime.UtcNow.AddHours(-3);
            task.Result = "done";
            await db.SaveChangesAsync();
        }
        else
        {
            await using var db = host.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == host.Fixture.TaskId);
            task.WorktreeBaseSha = h.SourceSha;
            task.CompletedAt = DateTime.UtcNow.AddHours(-3);
            task.Result = "done";
            await db.SaveChangesAsync();
            await h.SeedRetirementAsync();
        }

        host.Fixture.Git.Trace.Clear();
        return h;
    }

    public async Task SeedRetirementAsync(WorktreeRetirementState state = WorktreeRetirementState.Claimed)
    {
        await using var db = Host.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == Host.Fixture.TaskId);
        var existing = await db.TaskWorktreeRetirements.SingleOrDefaultAsync(r => r.TaskId == task.Id && r.Active);
        if (existing is not null)
        {
            RetirementId = existing.Id;
            existing.State = state;
            existing.SourceSha = SourceSha;
            existing.WorktreePath = task.WorktreePath ?? Host.Fixture.Source;
            existing.RepositoryPath = task.RepoPath ?? Host.Fixture.Repository;
            existing.CommonDirectory = CommonDirectory;
            existing.GitDirectory = GitDirectory;
            existing.SourceFullRef = FullRef(task.WorktreeBranch) ?? Host.Fixture.SourceRef;
            existing.TargetFullRef = Host.Fixture.TargetRef;
            existing.RemoteFingerprint = Fingerprint;
            existing.Active = true;
            await db.SaveChangesAsync();
            return;
        }

        RetirementId = Guid.NewGuid();
        db.TaskWorktreeRetirements.Add(new TaskWorktreeRetirement
        {
            Id = RetirementId,
            TaskId = task.Id,
            TaskAttempt = task.Attempt,
            TerminalStatus = task.Status,
            TaskCompletedAt = task.CompletedAt ?? DateTime.UtcNow.AddHours(-3),
            ReportDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(task.Result ?? ""))),
            ReleasedTaskRevision = task.ConcurrencyToken,
            CallerIdentity = "operator",
            ReleaseReason = "reviewed no further use",
            ReleasedAt = DateTime.UtcNow.AddHours(-1),
            HandoffDispositionJson = "[]",
            RepositoryPath = task.RepoPath ?? Host.Fixture.Repository,
            CommonDirectory = CommonDirectory,
            WorktreePath = task.WorktreePath ?? Host.Fixture.Source,
            GitDirectory = GitDirectory,
            SourceFullRef = string.IsNullOrWhiteSpace(task.WorktreeBranch)
                ? Host.Fixture.SourceRef
                : (task.WorktreeBranch.StartsWith("refs/", StringComparison.Ordinal)
                    ? task.WorktreeBranch
                    : "refs/heads/" + task.WorktreeBranch),
            SourceSha = SourceSha,
            TargetFullRef = Host.Fixture.TargetRef,
            RemoteName = "origin",
            DestinationFullRef = Host.Fixture.TargetRef,
            RemoteFingerprint = Fingerprint,
            ObservedTargetSha = TargetSha,
            State = state,
            Active = true,
            ClaimedAt = state == WorktreeRetirementState.Released ? null : DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public async Task<RepositoryLease> LeaseAsync()
    {
        var lease = await Host.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(Host.Fixture.Repository, CancellationToken.None);
        lease.ShouldNotBeNull();
        return lease!;
    }

    public WorktreeRemovalRequest Request(RepositoryLease lease, bool intent = true, LandSourceCoordinates? source = null) =>
        new(WorktreeRemovalPurpose.SettledTask,
            source ?? Host.Fixture.Coordinates,
            CommonDirectory, GitDirectory, SourceSha, TargetSha,
            null, lease, ManagedRoot: ManagedRoot, RetirementId: RetirementId, HasDeletionIntent: intent);

    public async Task<WorktreeRemoval> RemoveAsync(WorktreeRemovalRequest? request = null, RepositoryLease? lease = null)
    {
        var owned = lease ?? request?.Lease ?? await LeaseAsync();
        var dispose = lease is null && request?.Lease is null;
        try
        {
            return await Host.Services.GetRequiredService<IWorktreeManager>()
                .TryRemoveAsync(request ?? Request(owned), CancellationToken.None);
        }
        finally
        {
            if (dispose) await owned.DisposeAsync();
        }
    }

    public async Task<WorktreeRemoval> RemoveDirectAsync(WorktreeRemovalRequest request)
    {
        return await Host.Services.GetRequiredService<GuardedWorktreeRemoval>()
            .RemoveAsync(request, CancellationToken.None);
    }

    public int RemoveCalls => Host.Fixture.Git.Trace.Count(a => a.Contains("worktree") && a.Contains("remove"));
    public int BranchDeleteCalls => Host.Fixture.Git.Trace.Count(a =>
        a.Length > 0 && a[0] == "update-ref" && a.Contains("-d") && a.Any(x => x.Contains("feat/card-task", StringComparison.Ordinal)));
    public int InspectionCalls => Host.Fixture.Git.Trace.Count(a => a.Length > 0 && a[0] == "status");
    public int ForceOrRecursiveDeleteCalls => Host.Fixture.Git.Trace.Count(a =>
        a.Contains("--force") || a.Contains("-f") || a.Contains("rm") && a.Contains("-rf"));

    public TaskWorktreeRetirementService Retirement()
    {
        var scope = Host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
    }

    public async Task AssertRemoteUnchangedAsync()
    {
        var actual = await Host.Fixture.RequiredAsync(Host.Fixture.Remote, "rev-parse", "--verify",
            Host.Fixture.TargetRef + "^{commit}");
        actual.Trim().ShouldBe(Host.Fixture.SeedSha);
    }

    public ValueTask DisposeAsync() => Host.DisposeAsync();

    private static string? FullRef(string? value) => value is null ? null
        : value.StartsWith("refs/", StringComparison.Ordinal) ? value : "refs/heads/" + value;
}
