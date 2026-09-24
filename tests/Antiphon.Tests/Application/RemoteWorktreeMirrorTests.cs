using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0604 D-15/G-24. The DESKTOP worktree is canonical; the branch on origin is the unit of
// exchange. Settlement fast-forwards the desktop from the pushed branch, and a divergence or a
// dirty desktop tree is a refusal, never a reset: a reset would silently discard one side, and
// which side the operator wanted is not something this code can know. CARD-0657: the fixture
// supplies the task's captured identity and a lease; the sync consumes one pinned observation.
[Category("Unit")]
public sealed class RemoteWorktreeMirrorTests
{
    private const string Baseline = "1111111111111111111111111111111111111111";
    private const string Sha = "2222222222222222222222222222222222222222";
    private const string Diverged = "3333333333333333333333333333333333333333";
    private const string Repo = "/repos/antiphon";
    private const string Common = "/repos/antiphon/.git";
    private const string Worktree = "/worktrees/card-task-deadbeef";
    private const string FullRef = "refs/heads/feat/card-task-deadbeef";
    private const string Fingerprint = "ABCDEF";

    [Test]
    public async Task Sync_fast_forwards_desktop_worktree()
    {
        var git = new ScriptedGit(Baseline);
        git.On("merge", 0, "Fast-forward", after: () => git.Head = Sha);
        var progress = new FakeProgressGit(git);

        var result = await Service(git, progress).SyncAsync(Task(), CancellationToken.None);
        result.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        result.DesktopAfterSha.ShouldBe(Sha);
        result.Reason.ShouldBeNull();
        git.Commands.ShouldContain("-c merge.autoStash=false merge --ff-only " + Sha);
        git.Commands.ShouldNotContain(c => c.Contains("reset", StringComparison.Ordinal));
        git.Commands.ShouldNotContain(c => c.Contains("FETCH_HEAD", StringComparison.Ordinal));
    }

    [Test]
    public async Task Non_fast_forward_sync_is_refused()
    {
        var git = new ScriptedGit(Diverged);
        var progress = new FakeProgressGit(git);
        progress.Ancestors.Add((Baseline, Sha));

        var result = await Service(git, progress).SyncAsync(Task(), CancellationToken.None);
        result.State.ShouldBe(RemoteSettlementSyncState.Refused);
        result.Reason.ShouldBe(RemoteSettlementSyncReasons.Diverged);
        // The desktop worktree is left exactly as found.
        git.Commands.ShouldNotContain(c => c.Contains("merge --ff-only", StringComparison.Ordinal));
        git.Commands.ShouldNotContain(c => c.Contains("reset", StringComparison.Ordinal));
        git.Commands.ShouldNotContain(c => c.Contains("--force", StringComparison.Ordinal));
    }

    [Test]
    public async Task Dirty_desktop_tree_is_never_fast_forwarded_over()
    {
        var git = new ScriptedGit(Baseline);
        git.On("status", 0, " M server/Program.cs\0");
        var progress = new FakeProgressGit(git);

        var result = await Service(git, progress).SyncAsync(Task(), CancellationToken.None);
        result.State.ShouldBe(RemoteSettlementSyncState.Refused);
        result.Reason.ShouldBe(RemoteSettlementSyncReasons.Dirty);
        // Nothing beyond the inspection ran: not even an observation of origin.
        progress.Observations.ShouldBe(0);
        git.Commands.ShouldNotContain(c => c.Contains("fetch", StringComparison.Ordinal)
            || c.Contains("merge", StringComparison.Ordinal));
    }

    [Test]
    public async Task Push_failure_reports_and_does_not_invent_a_sha()
    {
        var git = new ScriptedGit();
        git.On("rev-parse", 0, Sha);
        git.On("push", 128, "fatal: remote rejected");

        var result = await Service(git).PushBranchAsync(Task(), CancellationToken.None);
        result.Pushed.ShouldBeFalse();
        result.Warning.ShouldContain("git push failed");
    }

    [Test]
    public async Task Push_reports_the_head_it_pushed()
    {
        var git = new ScriptedGit();
        git.On("rev-parse", 0, Sha);
        git.On("push", 0, "");

        var result = await Service(git).PushBranchAsync(Task(), CancellationToken.None);
        result.Pushed.ShouldBeTrue();
        result.Sha.ShouldBe(Sha);
        git.Commands.ShouldContain(c => c.StartsWith("push -u origin feat/card-task-deadbeef", StringComparison.Ordinal));
    }

    [Test]
    public void Mirror_name_is_the_dispatchers_own_short_form()
    {
        var id = Guid.Parse("deadbeef-0000-0000-0000-000000000000");
        RemoteWorkspaceService.MirrorName(id).ShouldBe("task-deadbeef");
        // The runner's own name pattern is task-<8 hex>; anything else is refused there.
        System.Text.RegularExpressions.Regex.IsMatch(RemoteWorkspaceService.MirrorName(id), "^task-[0-9a-f]{8}$")
            .ShouldBeTrue();
    }

    private static RemoteWorkspaceService Service(ScriptedGit git, FakeProgressGit? progress = null) =>
        new(new UnusedDirectory(), git, NullLogger<RemoteWorkspaceService>.Instance,
            progress ?? new FakeProgressGit(git), new FakeLeases());

    private static AgentTask Task() => new()
    {
        Id = Guid.Parse("deadbeef-0000-0000-0000-000000000000"),
        Workspace = WorkspaceMode.Worktree,
        Role = AgentTaskRole.Code,
        RunnerId = "server2",
        RepoPath = Repo,
        WorktreePath = Worktree,
        WorktreeBranch = "feat/card-task-deadbeef",
        ProgressBaselineJson = TaskProgressJson.SerializeBaseline(new ProgressBaselineSnapshot(
            1, DateTime.UtcNow, DateTime.UtcNow,
            new ProgressSourceBaseline(Repo, Common, Guid.Parse("deadbeef-0000-0000-0000-000000000000"), Worktree,
                FullRef, Baseline, new ProgressRemoteBaseline(ProgressRemoteState.Missing, null, Fingerprint)),
            null)),
    };

    /// <summary>
    /// Records every git invocation and answers by the first word of the argument list. The
    /// checkout it describes is registered, on the task branch, clean, and at <see cref="Head"/>.
    /// </summary>
    private sealed class ScriptedGit(string head = Baseline) : ILandingGit
    {
        private readonly Dictionary<string, (int Code, string Output, Action? After)> _answers = new(StringComparer.Ordinal)
        {
            ["symbolic-ref"] = (0, FullRef, null),
            ["status"] = (0, "", null),
            ["update-ref"] = (0, "", null),
        };
        public List<string> Commands { get; } = [];
        public string Head { get; set; } = head;

        public void On(string verb, int code, string output, Action? after = null) => _answers[verb] = (code, output, after);

        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Commands.Add(string.Join(' ', arguments));
            var verb = arguments[0] == "-c" ? arguments[2] : arguments[0];
            var answer = _answers.TryGetValue(verb, out var scripted)
                ? scripted
                : (1, "no scripted answer for " + verb, null);
            if (answer.Item1 == 0) answer.Item3?.Invoke();
            return System.Threading.Tasks.Task.FromResult(
                new LandingGitResult(answer.Item1, answer.Item1 == 0 ? answer.Item2 : "", answer.Item1 == 0 ? "" : answer.Item2));
        }

        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(path);
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(Common);
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(false);
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult<IReadOnlyList<LandingRegistration>>(
                [new LandingRegistration(Repo, "refs/heads/master", Baseline, false, false),
                 new LandingRegistration(Worktree, FullRef, Head, false, false)]);

        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
            Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination,
            string sourceSha, string observationRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef,
            string observationPrefix, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha,
            Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>Origin advertises <see cref="Sha"/> on the task branch; ancestry is scripted.</summary>
    private sealed class FakeProgressGit(ScriptedGit git) : ITaskProgressGit
    {
        private const string Pin = "refs/antiphon/progress/deadbeef000000000000000000000000/observe-000000000000";
        public int Observations { get; private set; }
        public HashSet<(string Ancestor, string Descendant)> Ancestors { get; } = [(Baseline, Sha)];

        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
            git.RunAsync(repository, arguments, ct);
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => git.CanonicalDirectoryAsync(path, ct);
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => git.CommonDirectoryAsync(repository, ct);
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) =>
            git.RegistrationsAsync(repository, ct);
        public Task<ProgressRevParse> RevParseCommitAsync(string repository, string revision, CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult(new ProgressRevParse(true, revision == Pin ? Sha : git.Head, null));
        public Task<ProgressSymbolicHead> SymbolicHeadAsync(string repository, CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult(new ProgressSymbolicHead(true, FullRef, null));
        public Task<string?> EndpointFingerprintAsync(string repository, CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult<string?>(Fingerprint);
        public Task<bool> HasOriginAsync(string repository, CancellationToken ct) => System.Threading.Tasks.Task.FromResult(true);
        public Task<ProgressRemoteObservation> ObserveExactRefAsync(
            string repository, string fullRef, string? expectedFingerprint, Guid taskId, CancellationToken ct)
        {
            Observations++;
            return System.Threading.Tasks.Task.FromResult(
                new ProgressRemoteObservation(ProgressRemoteState.Present, Sha, Fingerprint, ObservationRef: Pin));
        }
        public Task<bool?> IsAncestorAsync(string repository, string ancestorSha, string descendantSha, CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult<bool?>(ancestorSha == descendantSha || Ancestors.Contains((ancestorSha, descendantSha)));
        public Task<ProgressCommitTime> CommitTimeAsync(string repository, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProgressPinResult> PinBaselineAsync(string repository, Guid taskId, string name, string sha, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListProgressPinsAsync(string repository, Guid taskId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeLeases : IRepositoryMutationLease
    {
        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult<RepositoryLease?>(new FakeLease());
        public bool Owns(RepositoryLease lease, string commonDirectory) => lease is FakeLease;

        private sealed class FakeLease : RepositoryLease
        {
            public override string CommonDirectory => Common;
            public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Sync and push never resolve a runner: they are pure desktop git.</summary>
    private sealed class UnusedDirectory : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => throw new NotSupportedException();
        public IReadOnlyList<string> KnownRunnerIds => [];
        public Guid? LiveStoreId => null;
        public ISessionRunnerClient Resolve(string? runnerId) => throw new NotSupportedException("the sync path resolves no runner");
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => throw new NotSupportedException();
    }
}
