using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S3 D-4. An admitted Windows runner mints its branch in the repository and records a
/// sentinel checkout. It does not create <c>card-task-*</c>. The push reads that branch from the
/// repository. Defaults leave the desktop checkout path in place.
/// </summary>
[Category("Unit")]
[Timeout(60_000)]
public sealed class C0824WindowsSlotCheckoutTests
{
    private static readonly Guid TaskId = Guid.Parse("08240000-0000-4000-8000-000000000010");

    [Test]
    public async Task Admitted_windows_runner_records_a_sentinel_and_no_card_task_directory()
    {
        var git = new RecordingGit();
        var worktrees = new ThrowingWorktrees();
        var service = Service(worktrees, git, Admitted());
        var task = TaskRow();
        var decision = await service.CreateForTaskAsync(task, CancellationToken.None,
            cardBase: new ResolvedBase("refs/heads/master", WorktreeBaseSource.DefaultBranch, null));

        decision.ShouldNotBeNull();
        WindowsSlotCheckout.IsPending(task.WorktreePath).ShouldBeTrue();
        task.WorktreePath.ShouldBe(WindowsSlotCheckout.Pending(TaskId));
        task.WorktreeBranch.ShouldNotBeNullOrWhiteSpace();
        task.WorktreeBaseSha.ShouldBe(new string('b', 40));
        Directory.Exists(task.WorktreePath).ShouldBeFalse();
        worktrees.Created.ShouldBeFalse();
        git.Calls.All(call => call.Directory == task.RepoPath).ShouldBeTrue();
        git.Calls.Any(call => call.Arguments.Count > 0 && call.Arguments[0] == "branch").ShouldBeTrue();
    }

    [Test]
    public async Task Unlisted_windows_runner_does_not_take_the_sentinel()
    {
        var worktrees = new ThrowingWorktrees();
        var service = Service(worktrees, new RecordingGit(), new DelegationSettings { WorkspaceSlotsEnabled = true });
        var task = TaskRow();
        await Should.ThrowAsync<NotSupportedException>(() => service.CreateForTaskAsync(task, CancellationToken.None,
            cardBase: new ResolvedBase("refs/heads/master", WorktreeBaseSource.DefaultBranch, null)));
        task.WorktreePath.ShouldBeNull();
        worktrees.Created.ShouldBeFalse();
    }

    [Test]
    public async Task Pending_push_reads_the_branch_from_the_repository()
    {
        var git = new RecordingGit();
        var service = new RemoteWorkspaceService(
            new WindowsRunner("desktop-win"), git, NullLogger<RemoteWorkspaceService>.Instance);
        var task = TaskRow();
        task.WorktreePath = WindowsSlotCheckout.Pending(TaskId);
        task.WorktreeBranch = "feat/card-task-08240010";
        var pushed = await service.PushBranchAsync(task, CancellationToken.None);
        pushed.Pushed.ShouldBeTrue();
        pushed.Sha.ShouldBe(new string('b', 40));
        git.Calls.All(call => call.Directory == task.RepoPath).ShouldBeTrue();
        string.Join(" ", git.Calls[0].Arguments).ShouldBe("rev-parse refs/heads/feat/card-task-08240010");
        git.Calls.Any(call => call.Arguments.Count > 0 && call.Arguments[0] == "push").ShouldBeTrue();
    }

    private static DelegationWorktreeService Service(ThrowingWorktrees worktrees, RecordingGit git, DelegationSettings settings) =>
        new(worktrees, new MockGitService(), NullLogger<DelegationWorktreeService>.Instance,
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
            leases: new OpenLease(), landingGit: git, runners: new WindowsRunner("desktop-win"),
            delegationSettings: Options.Create(settings));

    private static DelegationSettings Admitted() => new()
    {
        WorkspaceSlotsEnabled = true,
        WindowsSlotRoles = "Investigate",
        WindowsSlotRunnerIds = ["desktop-win"],
    };

    private static AgentTask TaskRow() => new()
    {
        Id = TaskId,
        RootTaskId = TaskId,
        Title = "windows slot",
        Goal = "windows slot",
        Role = AgentTaskRole.Investigate,
        RunnerId = "desktop-win",
        Attempt = 1,
        RepoPath = "/tmp/card-0824-no-such-windows-slot-repo",
        CreatedAt = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc),
    };

    private sealed class OpenLease : IRepositoryMutationLease
    {
        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
            Task.FromResult<RepositoryLease?>(new Held());
        public bool Owns(RepositoryLease lease, string commonDirectory) => true;

        private sealed class Held : RepositoryLease
        {
            public override string CommonDirectory => "/repo";
            public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingWorktrees : IWorktreeManager
    {
        public bool Created { get; private set; }
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Created = true;
            throw new InvalidOperationException("card-task directory created");
        }
        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task TouchAsync(string worktreePath, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<int> PruneStaleAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class WindowsRunner(params string[] ids) : ISessionRunnerDirectory
    {
        private readonly FakeSessionRunnerClient _local = new();
        public ISessionRunnerClient Local => _local;
        public ISessionRunnerClient Resolve(string? id) => _local;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable(RunnerInventoryReasons.PhoneHomeUnavailable));
        public IReadOnlyList<string> KnownRunnerIds => ids;
        public Guid? GetLiveStoreId(string? id) => null;
        public Task<RunnerDescriptor?> DescribeAsync(string? id, CancellationToken ct)
        {
            if (id is null || !ids.Contains(id, StringComparer.Ordinal))
                return Task.FromResult<RunnerDescriptor?>(null);
            return Task.FromResult<RunnerDescriptor?>(new RunnerDescriptor(
                id, id, RunnerPlatformWire.Windows, DateTimeOffset.UnixEpoch, true, true, false, 2,
                new RunnerCapabilitiesDto("ModernConPty", "ModernConPty", "test", false,
                    Features: [RunnerCapabilityFeatures.WorkspaceSlotV1], Platform: RunnerPlatformWire.Windows)));
        }
    }

    private sealed class RecordingGit : ILandingGit
    {
        public List<(string Directory, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls.Add((repository, arguments));
            if (arguments.Count > 0 && arguments[0] == "rev-parse" && arguments.Contains("--verify"))
                return Task.FromResult(new LandingGitResult(1, "", "missing"));
            if (arguments.Count > 0 && arguments[0] == "rev-parse")
                return Task.FromResult(new LandingGitResult(0, new string('b', 40) + "\n", ""));
            if (arguments.Count > 0 && arguments[0] == "remote")
                return Task.FromResult(new LandingGitResult(0, "https://github.com/example/antiphon.git\n", ""));
            if (arguments.Count > 0 && arguments[0] is "branch" or "push")
                return Task.FromResult(new LandingGitResult(0, "", ""));
            throw new NotSupportedException(string.Join(' ', arguments));
        }

        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) =>
            Task.FromResult("/repo");

        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha, Func<int, long, CancellationToken, Task> started, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
