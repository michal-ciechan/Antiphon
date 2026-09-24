using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-0641 V-5: name the in-process lease owner while the OS lock is held.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RepositoryMutationLeaseOwnerTests
{
    [Test]
    [Timeout(60_000)]
    public async Task C641_Lease_exposes_task_owner_while_os_lock_is_held(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c641-owner-held");
        await repo.CommitFileAsync("seed.txt", "seed\n");
        var git = new LandingGit();
        IRepositoryMutationLease leases = new RepositoryMutationLease(git);
        var taskId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        await using var held = await leases.TryAcquireAsync(
            repo.Path, new RepositoryLeaseOwnerTag(taskId, RepositoryLeasePurposes.Land), ct);
        held.ShouldNotBeNull();

        var owner = await leases.FindOwnerAsync(repo.Path, ct);
        owner.ShouldNotBeNull("lease owner attribution is absent");
        owner.State.ShouldBe(RepositoryLeaseOwnerState.Known);
        owner.TaskId.ShouldBe(taskId);
        owner.Purpose.ShouldBe(RepositoryLeasePurposes.Land);
        owner.AcquisitionId.ShouldNotBeNull();
        owner.AcquisitionId!.Value.ShouldNotBe(Guid.Empty);
        owner.AcquiredAt.ShouldNotBeNull();
        owner.AcquiredAt.Value.ShouldBeGreaterThanOrEqualTo(before);
        owner.AcquiredAt.Value.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddSeconds(2));

        var other = new RepositoryMutationLease(git);
        (await other.TryAcquireAsync(repo.Path, ct)).ShouldBeNull("lookup must not release the OS lock");
        leases.Owns(held, held.CommonDirectory).ShouldBeTrue();
        var again = await leases.FindOwnerAsync(repo.Path, ct);
        again.ShouldNotBeNull();
        again.AcquisitionId.ShouldBe(owner.AcquisitionId);
        again.TaskId.ShouldBe(taskId);
    }

    [Test]
    [Timeout(60_000)]
    public async Task C641_Releasing_old_lease_cannot_erase_new_owner(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c641-owner-release");
        await repo.CommitFileAsync("seed.txt", "seed\n");
        var git = new LandingGit();
        IRepositoryMutationLease leases = new RepositoryMutationLease(git);
        var taskA = Guid.NewGuid();
        var taskB = Guid.NewGuid();
        var first = await leases.TryAcquireAsync(
            repo.Path, new RepositoryLeaseOwnerTag(taskA, RepositoryLeasePurposes.Land), ct);
        first.ShouldNotBeNull();
        (await leases.FindOwnerAsync(repo.Path, ct))!.TaskId.ShouldBe(taskA);
        await first.DisposeAsync();
        (await leases.FindOwnerAsync(repo.Path, ct))!.State.ShouldBe(RepositoryLeaseOwnerState.Unknown);

        await using var second = await leases.TryAcquireAsync(
            repo.Path, new RepositoryLeaseOwnerTag(taskB, RepositoryLeasePurposes.Dispatch), ct);
        second.ShouldNotBeNull();
        await first.DisposeAsync();
        var owner = await leases.FindOwnerAsync(repo.Path, ct);
        owner.ShouldNotBeNull();
        owner.State.ShouldBe(RepositoryLeaseOwnerState.Known);
        owner.TaskId.ShouldBe(taskB);
        owner.Purpose.ShouldBe(RepositoryLeasePurposes.Dispatch);
        owner.AcquisitionId.ShouldNotBeNull();
        owner.AcquisitionId!.Value.ShouldNotBe(Guid.Empty);
    }

    [Test]
    [Arguments("external")]
    [Arguments("journal")]
    [Timeout(60_000)]
    public async Task C641_Unknown_and_journal_only_holds_do_not_invent_an_owner(string variant, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c641-owner-unknown");
        await repo.CommitFileAsync("seed.txt", "seed\n");
        var git = new LandingGit();
        IRepositoryMutationLease leases = new RepositoryMutationLease(git);
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var lockPath = Path.Combine(common, "antiphon", "landing.lock");
        if (variant == "external")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            await using var external = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var owner = await leases.FindOwnerAsync(repo.Path, ct);
            owner.ShouldNotBeNull();
            owner.State.ShouldBe(RepositoryLeaseOwnerState.Unknown);
            owner.TaskId.ShouldBeNull();
            owner.Purpose.ShouldBeNull();
            (await leases.DescribeUnavailableAsync(repo.Path, ct)).ShouldBeNull();
            (await leases.TryAcquireAsync(repo.Path, ct)).ShouldBeNull();
            Should.Throw<IOException>(() => new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None));
            return;
        }

        var children = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(children);
        var journal = Path.Combine(children, "not-a-journal.txt");
        await File.WriteAllTextAsync(journal, "residue", ct);
        var before = await File.ReadAllTextAsync(journal, ct);
        var described = await leases.DescribeUnavailableAsync(repo.Path, ct);
        var ownerOnly = await leases.FindOwnerAsync(repo.Path, ct);
        ownerOnly.ShouldNotBeNull();
        ownerOnly.State.ShouldBe(RepositoryLeaseOwnerState.Unknown);
        ownerOnly.TaskId.ShouldBeNull();
        described.ShouldNotBeNull();
        described.ShouldContain("children");
        described.ShouldContain("recover-repository-children.ps1");
        File.Exists(lockPath).ShouldBeFalse("journal lookup must not acquire landing.lock");
        (await File.ReadAllTextAsync(journal, ct)).ShouldBe(before);
    }

    [Test]
    [Timeout(60_000)]
    public async Task C641_Owner_lookup_keeps_exclusion_and_Owns_semantics(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c641-owner-owns");
        await repo.CommitFileAsync("seed.txt", "seed\n");
        var git = new LandingGit();
        IRepositoryMutationLease leases = new RepositoryMutationLease(git);
        var other = new RepositoryMutationLease(git);
        await using var held = await leases.TryAcquireAsync(repo.Path, ct);
        held.ShouldNotBeNull();
        var common = held.CommonDirectory;
        var owner = await leases.FindOwnerAsync(repo.Path, ct);
        owner.ShouldNotBeNull();
        owner.State.ShouldBe(RepositoryLeaseOwnerState.Untagged);
        owner.TaskId.ShouldBeNull();
        owner.Purpose.ShouldBeNull();
        leases.Owns(held, common).ShouldBeTrue();
        other.Owns(held, common).ShouldBeFalse();
        (await other.TryAcquireAsync(repo.Path, ct)).ShouldBeNull();
        (await leases.FindOwnerAsync(repo.Path, ct))!.State.ShouldBe(RepositoryLeaseOwnerState.Untagged);
        (await other.TryAcquireAsync(repo.Path, ct)).ShouldBeNull();

        await held.DisposeAsync();
        leases.Owns(held, common).ShouldBeFalse();
        (await leases.FindOwnerAsync(repo.Path, ct))!.State.ShouldBe(RepositoryLeaseOwnerState.Unknown);
        await using var after = await other.TryAcquireAsync(repo.Path, ct);
        after.ShouldNotBeNull();
    }

    [Test]
    [Arguments("dispatch")]
    [Arguments("commit")]
    [Timeout(120_000)]
    public async Task C641_Shared_dispatch_and_commit_tag_the_lease(string path, CancellationToken ct)
    {
        if (path == "dispatch")
        {
            await AssertDispatchTagsAsync(ct);
            return;
        }

        await AssertCommitTagsAsync(ct);
    }

    [Test]
    [Timeout(60_000)]
    public async Task C641_Worktree_settlement_preserves_owner_tag(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c641-owner-settle");
        await repo.CommitFileAsync("seed.txt", "seed\n");
        var git = new LandingGit();
        var inner = new RepositoryMutationLease(git);
        var spy = new OwnerSpy(inner, git);
        var service = new DelegationWorktreeService(
            new UnusedWorktrees(),
            new GitService(NullLogger<GitService>.Instance),
            NullLogger<DelegationWorktreeService>.Instance,
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
            spy,
            git);
        var taskId = Guid.NewGuid();
        var missing = Path.Combine(repo.WorktreeRoot, "missing-settlement");
        var task = new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "settlement owner",
            Goal = "tag the settlement lease",
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path,
            RepoPath = repo.Path,
            WorktreePath = missing,
            WorktreeBranch = "feat/card-task-c641",
            Status = AgentTaskStatus.Succeeded,
            CreatedAt = DateTime.UtcNow,
        };

        var outcome = await service.TryMergeBackAsync(task, ct);
        outcome.Result.ShouldBe(DelegationWorktreeService.MergeResult.Failed);
        outcome.Detail.ShouldBe("source_registration_unknown");
        spy.Excluded.ShouldBeTrue();
        spy.Observed.ShouldNotBeNull("worktree settlement owner attribution is absent");
        spy.Observed.State.ShouldBe(RepositoryLeaseOwnerState.Known);
        spy.Observed.TaskId.ShouldBe(taskId);
        spy.Observed.Purpose.ShouldBe(RepositoryLeasePurposes.WorktreeSettlement);
        (await spy.FindOwnerAsync(repo.Path, ct))!.State.ShouldBe(RepositoryLeaseOwnerState.Unknown);
        await using var released = await inner.TryAcquireAsync(repo.Path, ct);
        released.ShouldNotBeNull();
    }

    private static async Task AssertCommitTagsAsync(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c641-owner-commit");
        await repo.CommitFileAsync("seed.txt", "seed\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "note.txt"), "note\n", ct);
        var git = new LandingGit();
        IRepositoryMutationLease leases = new RepositoryMutationLease(git);
        var taskId = Guid.NewGuid();
        RepositoryLeaseOwner? during = null;
        var excluded = false;
        var spy = new RecordingGitWorkspaceService
        {
            BeforeRun = async _ =>
            {
                if (during is not null) return;
                during = await leases.FindOwnerAsync(repo.Path, CancellationToken.None);
                var contender = await new RepositoryMutationLease(git).TryAcquireAsync(repo.Path, CancellationToken.None);
                excluded = contender is null;
                if (contender is not null) await contender.DisposeAsync();
            },
        };
        var gate = new GatedCommitService(spy, (RepositoryMutationLease)leases, NullLogger<GatedCommitService>.Instance);
        var committed = await gate.CommitAsync(
            repo.Path, ["note.txt"], "task owner\n\nbody", Trailers(taskId), ct);
        committed.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        excluded.ShouldBeTrue();
        during.ShouldNotBeNull("gated commit owner attribution is absent");
        during.State.ShouldBe(RepositoryLeaseOwnerState.Known);
        during.TaskId.ShouldBe(taskId);
        during.Purpose.ShouldBe(RepositoryLeasePurposes.GatedCommit);
        (await leases.FindOwnerAsync(repo.Path, ct))!.State.ShouldBe(RepositoryLeaseOwnerState.Unknown);

        var callerId = Guid.NewGuid();
        during = null;
        excluded = false;
        await using var caller = await leases.TryAcquireAsync(
            repo.Path, new RepositoryLeaseOwnerTag(callerId, "caller-held"), ct);
        caller.ShouldNotBeNull();
        var held = await gate.CommitAsync(repo.Path, null, "held\n\nbody", Trailers(Guid.NewGuid()), caller!, ct);
        held.Outcome.ShouldBe(GatedCommitOutcome.NothingToCommit);
        excluded.ShouldBeTrue();
        during.ShouldNotBeNull();
        during.TaskId.ShouldBe(callerId);
        during.Purpose.ShouldBe("caller-held");

        await Should.ThrowAsync<InvalidOperationException>(() => gate.CommitAsync(
            repo.Path, null, "missing trailer\n\nbody", [("antiphon", "true")], ct));
        var still = await leases.FindOwnerAsync(repo.Path, ct);
        still.ShouldNotBeNull();
        still.TaskId.ShouldBe(callerId);
        still.Purpose.ShouldBe("caller-held");
    }

    private static async Task AssertDispatchTagsAsync(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var repo = new ScratchGitRepo("c641-owner-dispatch");
        await repo.CommitFileAsync("seed.txt", "seed\n");
        var git = new LandingGit();
        var inner = new RepositoryMutationLease(git);
        var spy = new OwnerSpy(inner, git);
        using var cancel = new CancellationTokenSource();
        spy.CancelAfter = cancel;
        await using var world = CreateWorld(schema.ConnectionString, spy);
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "shared admission",
                Goal = "tag the dispatch lease",
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = repo.Path,
                RepoPath = repo.Path,
                Status = AgentTaskStatus.Queued,
                Ephemeral = false,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);
        }

        try
        {
            await world.Dispatcher.TickAsync(cancel.Token);
            throw new InvalidOperationException("dispatch tick completed without cancellation");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }

        spy.Excluded.ShouldBeTrue();
        spy.Observed.ShouldNotBeNull("dispatch owner attribution is absent");
        spy.Observed.State.ShouldBe(RepositoryLeaseOwnerState.Known);
        spy.Observed.TaskId.ShouldBe(taskId);
        spy.Observed.Purpose.ShouldBe(RepositoryLeasePurposes.Dispatch);
        await using (var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            (await verify.AgentTasks.SingleAsync(t => t.Id == taskId, ct)).Status.ShouldBe(AgentTaskStatus.Queued);
        }
    }

    private static (string Key, string Value)[] Trailers(Guid taskId) =>
        [("antiphon", "true"), ("antiphon-task", taskId.ToString("D")), ("antiphon-commit", "gated")];

    private static World CreateWorld(string connectionString, OwnerSpy lease)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton(Options.Create(new SupervisionSettings
        {
            DeliveryVerification = new(),
            CapacityRecovery = new CapacityRecoverySettings
            {
                Enabled = true,
                AdmissionIntervalSeconds = 1,
                JitterSeconds = 0,
                MaxEpisodeAttempts = 3,
            },
        }));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 6,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton<IRepositoryMutationLease>(lease);
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c641-owner-wt"),
        });
        services.AddSingleton<CapacityRecoveryService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<ModelAvailability>();
        services.AddScoped<RoutingPinService>();
        services.AddScoped<ComplexityRoutingService>();
        services.AddScoped<AgentTaskDispatcher>();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new World(provider, scope, scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>());
    }

    private sealed class OwnerSpy(RepositoryMutationLease inner, ILandingGit git) : IRepositoryMutationLease
    {
        public RepositoryLeaseOwner? Observed { get; private set; }
        public bool Excluded { get; private set; }
        public CancellationTokenSource? CancelAfter { get; set; }

        public async Task<RepositoryLease?> TryAcquireAsync(string repository, RepositoryLeaseOwnerTag owner, CancellationToken ct)
        {
            var lease = await ((IRepositoryMutationLease)inner).TryAcquireAsync(repository, owner, ct);
            if (lease is not null)
                await NoteAsync(repository);
            return lease;
        }

        public async Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct)
        {
            var lease = await inner.TryAcquireAsync(repository, ct);
            if (lease is not null)
                await NoteAsync(repository);
            return lease;
        }

        public bool Owns(RepositoryLease lease, string commonDirectory) => inner.Owns(lease, commonDirectory);

        public Task<string?> DescribeUnavailableAsync(string repository, CancellationToken ct) =>
            inner.DescribeUnavailableAsync(repository, ct);

        public Task<RepositoryLeaseOwner?> FindOwnerAsync(string repository, CancellationToken ct) =>
            ((IRepositoryMutationLease)inner).FindOwnerAsync(repository, ct);

        private async Task NoteAsync(string repository)
        {
            Observed = await ((IRepositoryMutationLease)inner).FindOwnerAsync(repository, CancellationToken.None);
            var contender = await new RepositoryMutationLease(git).TryAcquireAsync(repository, CancellationToken.None);
            Excluded = contender is null;
            if (contender is not null)
                await contender.DisposeAsync();
            CancelAfter?.Cancel();
        }
    }

    private sealed class World(ServiceProvider provider, IServiceScope scope, AgentTaskDispatcher dispatcher)
        : IAsyncDisposable
    {
        public AgentTaskDispatcher Dispatcher { get; } = dispatcher;

        public async ValueTask DisposeAsync()
        {
            scope.Dispose();
            await provider.DisposeAsync();
        }
    }
}

file sealed class UnusedWorktrees : IWorktreeManager
{
    public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct) =>
        throw new InvalidOperationException("not used");

    public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
        throw new InvalidOperationException("not used");

    public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) =>
        throw new InvalidOperationException("not used");

    public Task TouchAsync(string worktreePath, CancellationToken ct) =>
        throw new InvalidOperationException("not used");

    public Task<int> PruneStaleAsync(CancellationToken ct) =>
        throw new InvalidOperationException("not used");
}
