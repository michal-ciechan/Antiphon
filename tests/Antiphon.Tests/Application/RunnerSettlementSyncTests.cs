using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0657 S1. Settlement synchronizes the canonical desktop worktree to the commit the runner
// pushed to the task's OWN branch, and nothing else. Every case uses real Git: a bare origin, a
// desktop repository with the task worktree at baseline B, and a separate clone standing in for
// the runner. Only the clone commits and pushes, so the desktop has never seen S before the sync.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerSettlementSyncTests
{
    [Test]
    public async Task Pushed_tip_fast_forwards_exact_owned_checkout()
    {
        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "runner work");

        // The publication is absent from the desktop until the sync runs.
        (await world.HeadAsync()).ShouldBe(world.Baseline);
        (await world.HasObjectAsync(s)).ShouldBeFalse();

        var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

        // The reason names which rule refused or which step was unavailable when this goes red.
        result.State.ShouldBe(RemoteSettlementSyncState.Synchronized, "reason=" + result.Reason);
        result.Reason.ShouldBeNull();
        result.FullRef.ShouldBe(world.FullRef);
        result.BaselineSha.ShouldBe(world.Baseline);
        result.RemoteSha.ShouldBe(s);
        result.DesktopBeforeSha.ShouldBe(world.Baseline);
        result.DesktopAfterSha.ShouldBe(s);
        result.ObservationRef.ShouldNotBeNull();
        (await world.RevParseAsync(result.ObservationRef!)).ShouldBe(s);
        (await world.HeadAsync()).ShouldBe(s);
        (await world.SymbolicHeadAsync()).ShouldBe(world.FullRef);
        (await world.StatusAsync()).ShouldBeEmpty();
        File.ReadAllText(Path.Combine(world.Worktree, "work.txt")).ShouldBe("runner work");
    }

    [Test]
    public async Task Rejects_foreign_task_branch_before_fetch()
    {
        await using var world = await SyncWorld.CreateAsync();
        await world.RunnerPushAsync("work.txt", "own");
        var other = "feat/card-task-" + Guid.NewGuid().ToString("N")[..8];
        await world.PublishForeignBranchAsync(other, "foreign.txt");

        foreach (var branch in new[] { other, "master", "--upload-pack=true", "refs/heads/master:refs/heads/x" })
        {
            world.Git.Clear();
            var task = world.TaskWith(t => t.WorktreeBranch = branch);

            var result = await world.Service().SyncAsync(task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.Refused, branch);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.BranchMismatch, branch);
            world.Git.Commands.ShouldNotContain(c => c.Contains("fetch", StringComparison.Ordinal)
                || c.Contains("ls-remote", StringComparison.Ordinal) || c.Contains("merge", StringComparison.Ordinal), branch);
            (await world.HeadAsync()).ShouldBe(world.Baseline, branch);
        }
    }

    [Test]
    public async Task Rejects_changed_checkout_identity()
    {
        // A replacement repository: the baseline names another repository's common directory.
        await using (var world = await SyncWorld.CreateAsync())
        {
            await world.RunnerPushAsync("work.txt", "runner");
            var foreign = await world.InitForeignRepositoryAsync();
            var task = world.TaskWithBaseline(b => b with
            {
                Primary = b.Primary with { CanonicalCommonDirectory = foreign },
            });

            var result = await world.Service().SyncAsync(task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.Refused);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.IdentityMismatch);
            world.Git.Commands.ShouldNotContain(c => c.Contains("merge", StringComparison.Ordinal));
            (await world.HeadAsync()).ShouldBe(world.Baseline);
        }

        // Detached HEAD at the baseline commit.
        await using (var world = await SyncWorld.CreateAsync())
        {
            await world.RunnerPushAsync("work.txt", "runner");
            await world.RunAsync(world.Worktree, "checkout", "--detach");

            var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.Refused);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.BranchMismatch);
            world.Git.Commands.ShouldNotContain(c => c.Contains("merge", StringComparison.Ordinal));
            (await world.HeadAsync()).ShouldBe(world.Baseline);
            (await world.SymbolicHeadAsync()).ShouldBeNull();
        }

        // The checkout switched to a different branch.
        await using (var world = await SyncWorld.CreateAsync())
        {
            await world.RunnerPushAsync("work.txt", "runner");
            await world.RunAsync(world.Worktree, "checkout", "-b", "elsewhere");

            var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.Refused);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.BranchMismatch);
            world.Git.Commands.ShouldNotContain(c => c.Contains("merge", StringComparison.Ordinal));
            (await world.HeadAsync()).ShouldBe(world.Baseline);
            (await world.SymbolicHeadAsync()).ShouldBe("refs/heads/elsewhere");
        }

        // A locked registration is not a checkout this sync may mutate.
        await using (var world = await SyncWorld.CreateAsync())
        {
            await world.RunnerPushAsync("work.txt", "runner");
            await world.RunAsync(world.Desktop, "worktree", "lock", world.Worktree);

            var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.Refused);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.IdentityMismatch);
            world.Git.Commands.ShouldNotContain(c => c.Contains("merge", StringComparison.Ordinal));
            (await world.HeadAsync()).ShouldBe(world.Baseline);
        }
    }

    [Test]
    public async Task Dirty_or_sequenced_checkout_is_preserved()
    {
        var cases = new (string Name, Func<SyncWorld, Task> Arrange, string Reason)[]
        {
            ("submodule", async w =>
            {
                await w.RunAsync(w.Worktree, "-c", "protocol.file.allow=always", "submodule", "update", "--init");
                File.WriteAllText(Path.Combine(w.Worktree, "sub", "lib.txt"), "edited inside the submodule");
            }, RemoteSettlementSyncReasons.Dirty),
            ("index", async w =>
            {
                File.WriteAllText(Path.Combine(w.Worktree, "README.md"), "staged edit");
                await w.RunAsync(w.Worktree, "add", "README.md");
            }, RemoteSettlementSyncReasons.Dirty),
            ("tracked", w =>
            {
                File.WriteAllText(Path.Combine(w.Worktree, "README.md"), "unstaged edit");
                return System.Threading.Tasks.Task.CompletedTask;
            }, RemoteSettlementSyncReasons.Dirty),
            ("untracked", w =>
            {
                File.WriteAllText(Path.Combine(w.Worktree, "scratch.txt"), "operator notes");
                return System.Threading.Tasks.Task.CompletedTask;
            }, RemoteSettlementSyncReasons.Dirty),
            ("sequencer", async w =>
            {
                var gitDir = await w.RunAsync(w.Worktree, "rev-parse", "--absolute-git-dir");
                File.WriteAllText(Path.Combine(gitDir, "MERGE_HEAD"), w.Baseline + "\n");
            }, RemoteSettlementSyncReasons.Sequencer),
        };

        foreach (var (name, arrange, reason) in cases)
        {
            await using var world = await SyncWorld.CreateAsync(withSubmodule: name == "submodule");
            await world.RunnerPushAsync("work.txt", "runner");
            await arrange(world);
            var statusBefore = await world.StatusAsync();
            var filesBefore = world.SnapshotWorktree();

            var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.Refused, name);
            result.Reason.ShouldBe(reason, name);
            (await world.HeadAsync()).ShouldBe(world.Baseline, name);
            (await world.StatusAsync()).ShouldBe(statusBefore, name);
            world.SnapshotWorktree().ShouldBe(filesBefore, name);
            world.Git.Commands.ShouldNotContain(c => c.Contains("fetch", StringComparison.Ordinal)
                || c.Contains("merge", StringComparison.Ordinal) || c.Contains("reset", StringComparison.Ordinal)
                || c.Contains("stash", StringComparison.Ordinal) || c.Contains("checkout", StringComparison.Ordinal), name);
        }
    }

    [Test]
    public async Task Diverged_rewound_and_local_ahead_tips_refuse()
    {
        // Diverged: the desktop and the runner both committed on B.
        await using (var world = await SyncWorld.CreateAsync())
        {
            await world.RunnerPushAsync("work.txt", "runner");
            var local = await world.DesktopCommitAsync("desk.txt", "desktop");

            var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.Refused);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.Diverged);
            (await world.HeadAsync()).ShouldBe(local);
            File.Exists(Path.Combine(world.Worktree, "work.txt")).ShouldBeFalse();
        }

        // Local ahead: the desktop already holds S plus an unpublished commit on top of it.
        await using (var world = await SyncWorld.CreateAsync())
        {
            var s = await world.DesktopCommitAsync("pushed.txt", "pushed");
            await world.RunAsync(world.Worktree, "push", "origin", world.Branch);
            var ahead = await world.DesktopCommitAsync("ahead.txt", "unpublished");

            var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.Refused);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.LocalAhead);
            result.DesktopAfterSha.ShouldBeNull();
            (await world.HeadAsync()).ShouldBe(ahead);
            s.ShouldNotBe(ahead);
        }

        // Rewound: origin's task branch was force-moved behind the baseline.
        await using (var world = await SyncWorld.CreateAsync(extraMasterCommit: true))
        {
            await world.EnsureRunnerAsync();
            var parent = await world.RunAsync(world.Worktree, "rev-parse", world.Baseline + "^");
            await world.RunAsync(world.Runner, "push", "--force", "origin", parent + ":refs/heads/" + world.Branch);

            var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.Refused);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.Rewound);
            (await world.HeadAsync()).ShouldBe(world.Baseline);
        }
    }

    [Test]
    public async Task Missing_and_unchanged_remote_are_not_fetch_errors()
    {
        // The exact branch was never published: definite absence, not a transport failure.
        await using (var world = await SyncWorld.CreateAsync(pushBranch: false))
        {
            var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.NoPushedProgress);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.BranchNotPushed);
            (await world.HeadAsync()).ShouldBe(world.Baseline);
        }

        // Published, but still at the baseline: nothing new was pushed.
        await using (var world = await SyncWorld.CreateAsync())
        {
            var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.NoPushedProgress);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.NoPushedProgress);
            result.RemoteSha.ShouldBe(world.Baseline);
            result.DesktopAfterSha.ShouldBe(world.Baseline);
        }

        // The origin cannot be read at all: uncertainty, never absence.
        await using (var world = await SyncWorld.CreateAsync())
        {
            await world.RunnerPushAsync("work.txt", "runner");
            Directory.Move(world.Origin, world.Origin + "-offline");

            var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.FetchUnavailable);
            (await world.HeadAsync()).ShouldBe(world.Baseline);
        }
    }

    [Test]
    public async Task Pins_exact_ref_without_fetch_head_or_sibling_fetch()
    {
        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "own");

        // Plant a foreign commit where a loose sync would pick it up: FETCH_HEAD and the
        // remote-tracking ref for this very branch both name another task's descendant of B.
        var other = "feat/card-task-" + Guid.NewGuid().ToString("N")[..8];
        var foreign = await world.PublishForeignBranchAsync(other, "foreign.txt");
        await world.RunAsync(world.Desktop, "fetch", "--no-tags", "origin", $"refs/heads/{other}:refs/remotes/origin/{world.Branch}");
        var commonDir = await world.RunAsync(world.Desktop, "rev-parse", "--path-format=absolute", "--git-common-dir");
        var fetchHead = Path.Combine(commonDir, "FETCH_HEAD");
        File.WriteAllText(fetchHead, foreign + "\t\tbranch '" + world.Branch + "' of origin\n");
        var fetchHeadBefore = File.ReadAllText(fetchHead);
        world.Git.Clear();

        var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

        result.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        result.DesktopAfterSha.ShouldBe(s);
        (await world.HeadAsync()).ShouldBe(s);
        File.ReadAllText(fetchHead).ShouldBe(fetchHeadBefore);
        world.Git.Commands.ShouldNotContain(c => c.Contains("FETCH_HEAD", StringComparison.Ordinal));
        var fetches = world.Git.Commands.Where(c => c.StartsWith("fetch ", StringComparison.Ordinal)).ToList();
        fetches.ShouldNotBeEmpty();
        foreach (var fetch in fetches)
        {
            fetch.ShouldContain("--no-write-fetch-head");
            fetch.ShouldContain("--no-tags");
            var refspecs = fetch.Split(' ').Where(p => p.Contains(':') && p.StartsWith("refs/", StringComparison.Ordinal)).ToList();
            refspecs.Count.ShouldBe(1, fetch);
            refspecs[0].ShouldStartWith(world.FullRef + ":refs/antiphon/progress/" + world.Task.Id.ToString("N") + "/");
        }
        world.Git.Commands.Where(c => c.Contains("--ff-only", StringComparison.Ordinal))
            .ShouldAllBe(c => c.EndsWith(" " + s, StringComparison.Ordinal));
    }

    [Test]
    public async Task Lease_contention_never_mutates()
    {
        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "runner");
        await using var held = await world.Leases.TryAcquireAsync(world.Desktop, CancellationToken.None);
        held.ShouldNotBeNull();
        world.Git.Clear();

        var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

        result.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
        result.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
        world.Git.Commands.ShouldNotContain(c => c.StartsWith("fetch", StringComparison.Ordinal)
            || c.Contains("merge --ff-only", StringComparison.Ordinal) || c.StartsWith("update-ref", StringComparison.Ordinal));
        (await world.HeadAsync()).ShouldBe(world.Baseline);
        (await world.HasObjectAsync(s)).ShouldBeFalse();
    }

    [Test]
    public async Task Endpoint_change_is_not_followed()
    {
        var cases = new (string Name, Func<SyncWorld, Task> Arrange, string Reason)[]
        {
            ("changed", async w =>
            {
                var elsewhere = await w.CloneOriginElsewhereAsync();
                await w.RunAsync(w.Desktop, "remote", "set-url", "origin", elsewhere);
            }, RemoteSettlementSyncReasons.EndpointChanged),
            ("missing", async w => await w.RunAsync(w.Desktop, "remote", "remove", "origin"),
                RemoteSettlementSyncReasons.EndpointAmbiguous),
            ("ambiguous", async w =>
            {
                var elsewhere = await w.CloneOriginElsewhereAsync();
                await w.RunAsync(w.Desktop, "remote", "set-url", "--add", "--push", "origin", w.Origin);
                await w.RunAsync(w.Desktop, "remote", "set-url", "--add", "--push", "origin", elsewhere);
            }, RemoteSettlementSyncReasons.EndpointAmbiguous),
        };

        foreach (var (name, arrange, reason) in cases)
        {
            await using var world = await SyncWorld.CreateAsync();
            await world.RunnerPushAsync("work.txt", "runner");
            await arrange(world);
            world.Git.Clear();

            var result = await world.Service().SyncAsync(world.Task, CancellationToken.None);

            result.State.ShouldBe(RemoteSettlementSyncState.Refused, name);
            result.Reason.ShouldBe(reason, name);
            world.Git.Commands.ShouldNotContain(c => c.StartsWith("fetch", StringComparison.Ordinal)
                || c.StartsWith("ls-remote", StringComparison.Ordinal) || c.Contains("merge", StringComparison.Ordinal), name);
            (await world.HeadAsync()).ShouldBe(world.Baseline, name);
            (result.Reason ?? "").ShouldNotContain(world.Root);
        }
    }

    [Test]
    public async Task Timeout_and_cancellation_await_child_exit()
    {
        // The sync's own deadline expires while a Git child is in flight.
        await using (var world = await SyncWorld.CreateAsync())
        {
            await world.RunnerPushAsync("work.txt", "runner");
            world.Git.BlockOn = args => args.Contains("--ff-only");
            var clock = new FakeTimeProvider();
            var service = world.Service(clock);

            // The budget is measured on a controlled clock, so however slow process start is, the
            // sync reaches and is held in the merge child before any of it elapses. Only then does
            // the whole budget run out.
            var sync = service.SyncAsync(world.Task, CancellationToken.None);
            (await System.Threading.Tasks.Task.WhenAny(world.Git.Entered, sync)).ShouldBe(world.Git.Entered);
            clock.Advance(service.SyncBudget);
            var result = await sync;

            result.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
            result.Reason.ShouldBe(RemoteSettlementSyncReasons.Timeout);
            world.Git.InFlight.ShouldBe(0);
            world.Git.Blocked.ShouldBe(1);
            (await world.HeadAsync()).ShouldBe(world.Baseline);
        }

        // The caller cancels: that is not a verdict and must propagate.
        await using (var world = await SyncWorld.CreateAsync())
        {
            await world.RunnerPushAsync("work.txt", "runner");
            using var cts = new CancellationTokenSource();
            world.Git.BlockOn = args => args.Contains("--ff-only");
            world.Git.OnBlocked = () => cts.Cancel();

            Exception? caught = null;
            try { await world.Service().SyncAsync(world.Task, cts.Token); }
            catch (Exception ex) { caught = ex; }
            caught.ShouldBeAssignableTo<OperationCanceledException>();
            world.Git.InFlight.ShouldBe(0);
            (await world.HeadAsync()).ShouldBe(world.Baseline);
        }
    }

    [Test]
    public async Task Competing_retirement_is_fenced_for_the_whole_sync()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)));
        await using var provider = services.BuildServiceProvider();
        var journal = new WorkspaceReservationJournal(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);

        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "runner");
        // Retirement's own coordinates: worktree path, source full ref, repository path.
        var retirement = new WorkspaceReservationCommand(
            WorkspaceReservationKey.For(world.Task.WorktreePath, world.FullRef, world.Task.RepoPath),
            WorkspaceReservationKind.Retirement, world.TaskId, RetirementId: Guid.NewGuid());
        world.Git.BlockOn = args => args.Contains("--ff-only");

        // Retirement races the sync after every check and before the fast-forward.
        var sync = world.Service(reservations: journal).SyncAsync(world.Task, CancellationToken.None);
        (await System.Threading.Tasks.Task.WhenAny(world.Git.Entered, sync)).ShouldBe(world.Git.Entered);
        var during = await journal.TryClaimRetirementAsync(retirement, CancellationToken.None);
        world.Git.OpenGate();
        var result = await sync;

        during.Accepted.ShouldBeFalse();
        during.Reason.ShouldBe("workspace_in_use");
        result.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        (await world.HeadAsync()).ShouldBe(s);

        // The consumer ends with the sync, so retirement is then admitted...
        (await journal.TryClaimRetirementAsync(retirement, CancellationToken.None)).Accepted.ShouldBeTrue();

        // ...and a sync that finds the workspace claimed refuses before reading the checkout.
        world.Git.BlockOn = null;
        world.Git.Clear();
        var refused = await world.Service(reservations: journal).SyncAsync(world.Task, CancellationToken.None);
        refused.State.ShouldBe(RemoteSettlementSyncState.Refused);
        refused.Reason.ShouldBe(RemoteSettlementSyncReasons.RetirementReserved);
        world.Git.Commands.ShouldBeEmpty();
    }

    [Test]
    public async Task Unknown_post_merge_head_is_not_success()
    {
        await using var world = await SyncWorld.CreateAsync();
        var s = await world.RunnerPushAsync("work.txt", "runner");
        world.Git.FailFirstAfterMerge = true;

        var first = await world.Service().SyncAsync(world.Task, CancellationToken.None);

        first.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
        first.Reason.ShouldBe(RemoteSettlementSyncReasons.PostconditionUnavailable);
        first.DesktopAfterSha.ShouldBeNull();
        first.Confirmed.ShouldBeFalse();

        // Retry reinspects the checkout already at S and confirms it without a second merge.
        world.Git.FailFirstAfterMerge = false;
        world.Git.Clear();
        var retry = await world.Service().SyncAsync(world.Task, CancellationToken.None);

        retry.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        retry.DesktopBeforeSha.ShouldBe(s);
        retry.DesktopAfterSha.ShouldBe(s);
        world.Git.Commands.ShouldNotContain(c => c.Contains("merge --ff-only", StringComparison.Ordinal));
    }

    [Test]
    public async Task Excluded_workspaces_never_sync()
    {
        var root = Path.Combine(Path.GetTempPath(), "c657-excluded-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var git = new RecordingGit(null);
            var service = new RemoteWorkspaceService(
                new UnusedDirectory(), git, NullLogger<RemoteWorkspaceService>.Instance, git, new NoLease());
            var id = Guid.NewGuid();
            var branch = "feat/card-task-" + id.ToString("N")[..8];
            AgentTask Make(Action<AgentTask> shape)
            {
                var task = new AgentTask
                {
                    Id = id, Workspace = WorkspaceMode.Worktree, Role = AgentTaskRole.Code, RunnerId = "server2",
                    WorktreePath = root, WorkingDirectory = root, RepoPath = root, WorktreeBranch = branch,
                    RemoteWorktreePath = "/work/worktrees/task-" + id.ToString("N")[..8],
                };
                shape(task);
                return task;
            }

            var excluded = new (string Name, AgentTask Task)[]
            {
                ("local", Make(t => t.RunnerId = null)),
                ("blank-runner", Make(t => t.RunnerId = "  ")),
                ("shared", Make(t => t.Workspace = WorkspaceMode.Shared)),
                ("readonly", Make(t => t.Workspace = WorkspaceMode.ReadOnly)),
                ("mutation", Make(t => t.Role = AgentTaskRole.Mutation)),
                ("source-landing", Make(t => t.SourceLandingOperationId = Guid.NewGuid())),
            };

            foreach (var (name, task) in excluded)
            {
                git.Clear();
                var result = await service.SyncAsync(task, CancellationToken.None);
                result.State.ShouldBe(RemoteSettlementSyncState.NotApplicable, name);
                git.Commands.ShouldBeEmpty(name);
            }
        }
        finally
        {
            SyncWorld.DeleteTree(root);
        }
    }

    /// <summary>
    /// One scratch topology: bare origin, desktop repository with the task worktree at B, and a
    /// separate runner clone. Every Git child goes through <see cref="LandingGit"/>'s bounded path.
    /// </summary>
    internal sealed class SyncWorld : IAsyncDisposable
    {
        // Setup Git is bounded (timeout, kill, drained output) but not journaled: LandingGit's child
        // journal reads StartTime of an already-reaped fast child on Linux (a pre-existing race).

        public string Root { get; }
        public string Origin => Path.Combine(Root, "origin.git");
        public string Desktop => Path.Combine(Root, "desktop");
        public string Worktree => Path.Combine(Root, "wt");
        public string Runner => Path.Combine(Root, "runner");
        public Guid TaskId { get; } = Guid.NewGuid();
        public string Branch => "feat/card-task-" + TaskId.ToString("N")[..8];
        public string FullRef => "refs/heads/" + Branch;
        public string Baseline { get; private set; } = "";
        public RepositoryMutationLease Leases { get; } = new(new LandingGit());
        public RecordingGit Git { get; }
        public AgentTask Task { get; private set; } = new();
        private ProgressBaselineSnapshot _snapshot = null!;
        private bool _runnerCloned;

        private SyncWorld()
        {
            Root = Path.Combine(Path.GetTempPath(), "c657-" + Guid.NewGuid().ToString("N")[..12]);
            Git = new RecordingGit(Leases);
        }

        public static async Task<SyncWorld> CreateAsync(
            bool pushBranch = true, bool extraMasterCommit = false, bool withSubmodule = false)
        {
            var world = new SyncWorld();
            Directory.CreateDirectory(world.Root);
            await world.RunAsync(world.Root, "init", "--bare", "-b", "master", world.Origin);
            Directory.CreateDirectory(world.Desktop);
            await world.RunAsync(world.Desktop, "init", "-b", "master");
            await world.ConfigureAsync(world.Desktop);
            File.WriteAllText(Path.Combine(world.Desktop, "README.md"), "base\n");
            await world.RunAsync(world.Desktop, "add", "README.md");
            await world.RunAsync(world.Desktop, "commit", "-m", "base");
            if (extraMasterCommit)
            {
                File.WriteAllText(Path.Combine(world.Desktop, "second.txt"), "second\n");
                await world.RunAsync(world.Desktop, "add", "second.txt");
                await world.RunAsync(world.Desktop, "commit", "-m", "second");
            }
            if (withSubmodule)
            {
                // Part of baseline B; left uninitialized (clean) in the task worktree until a case uses it.
                var sub = Path.Combine(world.Root, "sub");
                Directory.CreateDirectory(sub);
                await world.RunAsync(sub, "init", "-b", "master");
                await world.ConfigureAsync(sub);
                File.WriteAllText(Path.Combine(sub, "lib.txt"), "lib\n");
                await world.RunAsync(sub, "add", "lib.txt");
                await world.RunAsync(sub, "commit", "-m", "lib");
                await world.RunAsync(world.Desktop, "-c", "protocol.file.allow=always",
                    "submodule", "add", sub.Replace('\\', '/'), "sub");
                await world.RunAsync(world.Desktop, "commit", "-m", "submodule");
            }
            await world.RunAsync(world.Desktop, "remote", "add", "origin", world.Origin);
            await world.RunAsync(world.Desktop, "push", "origin", "master");
            await world.RunAsync(world.Desktop, "worktree", "add", "-b", world.Branch, world.Worktree, "master");

            // The dispatcher's own capture, before remote preparation pushes the branch.
            var progress = new TaskProgressGit(world.Leases);
            var repo = await progress.CanonicalDirectoryAsync(world.Desktop, CancellationToken.None);
            var common = await progress.CommonDirectoryAsync(repo, CancellationToken.None);
            var worktree = await progress.CanonicalDirectoryAsync(world.Worktree, CancellationToken.None);
            var head = await progress.RevParseCommitAsync(worktree, "HEAD", CancellationToken.None);
            world.Baseline = head.Sha!;
            var remote = await progress.ObserveExactRefAsync(repo, world.FullRef, null, world.TaskId, CancellationToken.None);
            remote.EndpointFingerprint.ShouldNotBeNull();
            world._snapshot = new ProgressBaselineSnapshot(1, DateTime.UtcNow, DateTime.UtcNow,
                new ProgressSourceBaseline(repo, common, world.TaskId, worktree, world.FullRef, world.Baseline,
                    new ProgressRemoteBaseline(remote.State, remote.Sha, remote.EndpointFingerprint, remote.Reason)),
                null);
            world.Task = new AgentTask
            {
                Id = world.TaskId,
                Workspace = WorkspaceMode.Worktree,
                Role = AgentTaskRole.Code,
                RunnerId = "server2",
                RepoPath = repo,
                WorkingDirectory = repo,
                WorktreePath = worktree,
                WorktreeBranch = world.Branch,
                WorktreeBaseSha = world.Baseline,
                RemoteWorktreePath = "/work/worktrees/task-" + world.TaskId.ToString("N")[..8],
                ProgressBaselineJson = TaskProgressJson.SerializeBaseline(world._snapshot),
            };

            if (pushBranch)
                await world.RunAsync(world.Worktree, "push", "origin", world.Branch);
            world.Git.Clear();
            return world;
        }

        public RemoteWorkspaceService Service(TimeProvider? clock = null, IWorkspaceReservationJournal? reservations = null) =>
            new(new UnusedDirectory(), Git, NullLogger<RemoteWorkspaceService>.Instance, Git, Leases, reservations)
            {
                Clock = clock ?? TimeProvider.System,
            };

        public AgentTask TaskWith(Action<AgentTask> shape)
        {
            var copy = Clone(Task);
            shape(copy);
            return copy;
        }

        public AgentTask TaskWithBaseline(Func<ProgressBaselineSnapshot, ProgressBaselineSnapshot> shape) =>
            TaskWith(t => t.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(shape(_snapshot)));

        /// <summary>Commit and push on the runner clone only. Returns the pushed commit.</summary>
        public async Task<string> RunnerPushAsync(string file, string content)
        {
            await EnsureRunnerAsync();
            File.WriteAllText(Path.Combine(Runner, file), content);
            await RunAsync(Runner, "add", file);
            await RunAsync(Runner, "commit", "-m", "runner " + file);
            await RunAsync(Runner, "push", "origin", "HEAD:refs/heads/" + Branch);
            return await RunAsync(Runner, "rev-parse", "HEAD");
        }

        /// <summary>Another task's branch on the same origin, descending from B.</summary>
        public async Task<string> PublishForeignBranchAsync(string branch, string file)
        {
            await EnsureRunnerAsync();
            await RunAsync(Runner, "checkout", "-b", branch, "origin/master");
            File.WriteAllText(Path.Combine(Runner, file), "foreign");
            await RunAsync(Runner, "add", file);
            await RunAsync(Runner, "commit", "-m", "foreign " + file);
            await RunAsync(Runner, "push", "origin", "HEAD:refs/heads/" + branch);
            var sha = await RunAsync(Runner, "rev-parse", "HEAD");
            await RunAsync(Runner, "checkout", Branch);
            return sha;
        }

        public async Task<string> DesktopCommitAsync(string file, string content)
        {
            File.WriteAllText(Path.Combine(Worktree, file), content);
            await RunAsync(Worktree, "add", file);
            await RunAsync(Worktree, "commit", "-m", "desktop " + file);
            return await RunAsync(Worktree, "rev-parse", "HEAD");
        }

        public async Task<string> InitForeignRepositoryAsync()
        {
            var foreign = Path.Combine(Root, "foreign");
            Directory.CreateDirectory(foreign);
            await RunAsync(foreign, "init", "-b", "master");
            return await RunAsync(foreign, "rev-parse", "--path-format=absolute", "--git-common-dir");
        }

        /// <summary>A second bare repository carrying the same branch plus a commit.</summary>
        public async Task<string> CloneOriginElsewhereAsync()
        {
            var elsewhere = Path.Combine(Root, "elsewhere-" + Guid.NewGuid().ToString("N")[..6] + ".git");
            await RunAsync(Root, "clone", "--bare", Origin, elsewhere);
            return elsewhere;
        }

        public async Task<string> HeadAsync() => await RunAsync(Worktree, "rev-parse", "HEAD");

        public async Task<string?> SymbolicHeadAsync()
        {
            var result = await ExecAsync(Worktree, ["symbolic-ref", "-q", "HEAD"]);
            return result.Succeeded ? result.Output.Trim() : null;
        }

        public async Task<string> RevParseAsync(string revision) => await RunAsync(Desktop, "rev-parse", revision);

        public async Task<bool> HasObjectAsync(string sha) =>
            (await ExecAsync(Desktop, ["cat-file", "-e", sha + "^{commit}"])).Succeeded;

        public async Task<string> StatusAsync() =>
            await RunAsync(Worktree, "status", "--porcelain=v1", "--untracked-files=all", "--ignore-submodules=none");

        /// <summary>Every worktree file's bytes, keyed by relative path (the .git link excluded).</summary>
        public string SnapshotWorktree() => string.Join("\n",
            Directory.EnumerateFiles(Worktree, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(Worktree, p))
                .Where(p => p != ".git")
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => p + "=" + Convert.ToHexString(File.ReadAllBytes(Path.Combine(Worktree, p)))));

        public async Task<string> RunAsync(string directory, params string[] arguments)
        {
            var result = await ExecAsync(directory, arguments);
            result.ExitCode.ShouldBe(0, "git " + string.Join(' ', arguments));
            return result.Output.Trim();
        }

        private static async Task<LandingGitResult> ExecAsync(string directory, IReadOnlyList<string> arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)
                ?? throw new IOException("git_start_failed");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await process.WaitForExitAsync(budget.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await System.Threading.Tasks.Task.WhenAll(output, error);
                throw new TimeoutException("git " + string.Join(' ', arguments));
            }
            await System.Threading.Tasks.Task.WhenAll(output, error);
            return new LandingGitResult(process.ExitCode, await output, process.ExitCode == 0 ? "" : await error);
        }

        private async Task ConfigureAsync(string repository)
        {
            await RunAsync(repository, "config", "user.email", "c657@example.invalid");
            await RunAsync(repository, "config", "user.name", "CARD-0657 test");
            await RunAsync(repository, "config", "commit.gpgsign", "false");
        }

        public async Task EnsureRunnerAsync()
        {
            if (_runnerCloned) return;
            await RunAsync(Root, "clone", "-b", Branch, Origin, Runner);
            await ConfigureAsync(Runner);
            _runnerCloned = true;
        }

        private static AgentTask Clone(AgentTask task) => new()
        {
            Id = task.Id, Workspace = task.Workspace, Role = task.Role, RunnerId = task.RunnerId,
            RepoPath = task.RepoPath, WorkingDirectory = task.WorkingDirectory, WorktreePath = task.WorktreePath,
            WorktreeBranch = task.WorktreeBranch, WorktreeBaseSha = task.WorktreeBaseSha,
            RemoteWorktreePath = task.RemoteWorktreePath, ProgressBaselineJson = task.ProgressBaselineJson,
            SourceLandingOperationId = task.SourceLandingOperationId,
        };

        public ValueTask DisposeAsync()
        {
            DeleteTree(Root);
            return ValueTask.CompletedTask;
        }

        public static void DeleteTree(string root)
        {
            if (!Directory.Exists(root)) return;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The production Git I/O with a command trace and three controlled faults: a block that only
    /// ends on cancellation, and one failed command right after a successful merge.
    /// </summary>
    internal sealed class RecordingGit(IRepositoryMutationLease? leases) : TaskProgressGit(leases)
    {
        private readonly ConcurrentQueue<string> _commands = new();
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inFlight;
        private int _blocked;
        private bool _merged;

        public IReadOnlyList<string> Commands => _commands.ToArray();
        public int InFlight => Volatile.Read(ref _inFlight);
        public int Blocked => Volatile.Read(ref _blocked);
        public Func<IReadOnlyList<string>, bool>? BlockOn { get; set; }
        public Action? OnBlocked { get; set; }
        public bool FailFirstAfterMerge { get; set; }

        /// <summary>Completes when the sync has reached, and is held in, the blocked command.</summary>
        public System.Threading.Tasks.Task Entered => _entered.Task;

        /// <summary>Lets the held command run for real.</summary>
        public void OpenGate() => _gate.TrySetResult();

        public void Clear()
        {
            while (_commands.TryDequeue(out _)) { }
            _merged = false;
        }

        public override async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            _commands.Enqueue(string.Join(' ', arguments));
            Interlocked.Increment(ref _inFlight);
            try
            {
                if (BlockOn?.Invoke(arguments) == true)
                {
                    Interlocked.Increment(ref _blocked);
                    _entered.TrySetResult();
                    OnBlocked?.Invoke();
                    // Held until the gate opens or the token ends. Bounded so a sync that ignores its
                    // own deadline fails this test instead of hanging it.
                    var bound = System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(10), ct);
                    if (await System.Threading.Tasks.Task.WhenAny(_gate.Task, bound) == bound)
                    {
                        await bound;
                        return new LandingGitResult(1, "", "blocked_without_cancellation");
                    }
                }

                if (FailFirstAfterMerge && _merged)
                {
                    _merged = false;
                    FailFirstAfterMerge = false;
                    return new LandingGitResult(128, "", "git_exit_128");
                }

                var result = await base.RunAsync(repository, arguments, ct);
                if (result.Succeeded && arguments.Contains("merge") && arguments.Contains("--ff-only"))
                    _merged = true;
                return result;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    private sealed class NoLease : IRepositoryMutationLease
    {
        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
            throw new InvalidOperationException("an excluded task must not take the repository lease");
        public bool Owns(RepositoryLease lease, string commonDirectory) => false;
    }

    internal sealed class UnusedDirectory : ISessionRunnerDirectory
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
