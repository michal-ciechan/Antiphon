using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0604 D-15/G-24. The DESKTOP worktree is canonical; the branch on origin is the unit of
// exchange. Settlement fast-forwards the desktop from the pushed branch, and a divergence or a
// dirty desktop tree is a WARNING, never a reset: a reset would silently discard one side, and
// which side the operator wanted is not something this code can know.
[Category("Unit")]
public sealed class RemoteWorktreeMirrorTests
{
    private const string Sha = "1111111111111111111111111111111111111111";

    [Test]
    public async Task Sync_fast_forwards_desktop_worktree()
    {
        var git = new ScriptedGit();
        git.On("status", 0, "");
        git.On("fetch", 0, "");
        git.On("merge", 0, "Fast-forward");
        git.On("rev-parse", 0, Sha);

        var result = await Service(git).SyncAsync(Task(), CancellationToken.None);
        result.Synced.ShouldBeTrue();
        result.Sha.ShouldBe(Sha);
        result.Warning.ShouldBeNull();
        git.Commands.ShouldContain(c => c.StartsWith("merge --ff-only", StringComparison.Ordinal));
        git.Commands.ShouldNotContain(c => c.Contains("reset", StringComparison.Ordinal));
    }

    [Test]
    public async Task Non_fast_forward_sync_is_refused()
    {
        var git = new ScriptedGit();
        git.On("status", 0, "");
        git.On("fetch", 0, "");
        git.On("merge", 128, "fatal: Not possible to fast-forward, aborting.");

        var result = await Service(git).SyncAsync(Task(), CancellationToken.None);
        result.Synced.ShouldBeFalse();
        result.Warning.ShouldContain("diverged");
        // The desktop worktree is left exactly as found.
        git.Commands.ShouldNotContain(c => c.Contains("reset", StringComparison.Ordinal));
        git.Commands.ShouldNotContain(c => c.Contains("--force", StringComparison.Ordinal));
    }

    [Test]
    public async Task Dirty_desktop_tree_is_never_fast_forwarded_over()
    {
        var git = new ScriptedGit();
        git.On("status", 0, " M server/Program.cs");

        var result = await Service(git).SyncAsync(Task(), CancellationToken.None);
        result.Synced.ShouldBeFalse();
        result.Warning.ShouldContain("uncommitted");
        // Nothing beyond the status read ran: not even a fetch.
        git.Commands.Count.ShouldBe(1);
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

    private static RemoteWorkspaceService Service(ILandingGit git) =>
        new(new UnusedDirectory(), git, NullLogger<RemoteWorkspaceService>.Instance);

    private static AgentTask Task() => new()
    {
        Id = Guid.Parse("deadbeef-0000-0000-0000-000000000000"),
        RunnerId = "server2",
        WorktreePath = @"C:\Antiphon\worktrees\card-task-deadbeef",
        WorktreeBranch = "feat/card-task-deadbeef",
    };

    /// <summary>Records every git invocation and answers by the first word of the argument list.</summary>
    private sealed class ScriptedGit : ILandingGit
    {
        private readonly Dictionary<string, (int Code, string Output)> _answers = new(StringComparer.Ordinal);
        public List<string> Commands { get; } = [];

        public void On(string verb, int code, string output) => _answers[verb] = (code, output);

        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Commands.Add(string.Join(' ', arguments));
            var answer = _answers.TryGetValue(arguments[0], out var scripted)
                ? scripted
                : (1, "no scripted answer for " + arguments[0]);
            return System.Threading.Tasks.Task.FromResult(
                new LandingGitResult(answer.Item1, answer.Item1 == 0 ? answer.Item2 : "", answer.Item1 == 0 ? "" : answer.Item2));
        }

        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
            Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
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
