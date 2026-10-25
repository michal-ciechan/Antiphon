using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandVerificationLeaseWindowTests
{
    [Test]
    public async Task C1379_StartRefAndWorktreeFinishBeforeVerificationRelease()
    {
        await using var child = new LandVerificationWindowHarness();
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, landingVerifier: child.Verifier);
        using var originOnly = new ScratchGitRepo("c1379-origin-only");
        await originOnly.CommitFileAsync("fresh.txt", "only on origin\n");
        await originOnly.GitAsync("remote", "add", "origin", world.Remote);
        await originOnly.GitAsync("push", "origin", "HEAD:refs/heads/c1379-origin-only");
        var startSha = (await originOnly.GitReadAsync("rev-parse", "HEAD")).Trim();
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "cat-file", "-e", startSha)).Ok.ShouldBeFalse();
        await using (var db = world.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.Repair.Id);
            task.WorktreeBaseRequestedRef = startSha;
            await db.SaveChangesAsync();
        }
        await using var scope = world.Services.CreateAsyncScope();
        var lands = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
        await lands.RequestAsync(world.Owner.Id, new LandAgentTaskRequest("c1379-controlled-child", world.OwnerSha),
            CancellationToken.None);
        var landing = lands.RunAsync(world.Owner.Id, null, CancellationToken.None);
        try
        {
            await child.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await using (var probe = await world.Leases.TryAcquirePreflightAsync(world.Repo.Path,
                             RepositoryPreflight.Fetch(Guid.NewGuid(), world.OwnerSha),
                             new(null, RepositoryLeasePurposes.StartRefFetch), CancellationToken.None))
                probe.ShouldNotBeNull("c1379-dispatch-before-release: real verification must release R while retaining L");
            await world.Services.GetRequiredService<StartRefAvailability>()
                .EnsureAvailableAsync(world.Repo.Path, startSha, CancellationToken.None);
            (await ScratchGitRepo.GitInAsync(world.Repo.Path, "cat-file", "-e", startSha)).Ok.ShouldBeTrue();
            var (dispatched, _) = await world.DispatchAsync();
            dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
            Directory.Exists(dispatched.WorktreePath).ShouldBeTrue();
            (await ScratchGitRepo.GitInAsync(dispatched.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(startSha);
            await using var observer = world.CreateContext();
            (await observer.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == dispatched.Id)).AgentSessionId
                .ShouldNotBeNull("a separate connection sees the committed worktree claim before the verifier is released");
        }
        finally
        {
            child.Release();
            await landing;
        }
        await using var final = world.CreateContext();
        var operation = await final.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.TaskId == world.Owner.Id);
        operation.RemoteConfirmedAt.ShouldNotBeNull("publication must resume after compatible dispatch; refusal=" + operation.LastReason);
    }

    [Test]
    public async Task C1388_TicketFieldChangeRefusesResume() =>
        await RefuseAsync("c1388-ticket", "verification_ticket_changed", async world =>
        {
            await using var db = world.CreateContext();
            var operation = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == world.Owner.Id);
            operation.Phase.ShouldBe(LandPhase.Prepared);
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == operation.ApprovalLandRequestId);
            request.State.ShouldBe(LandRequestState.Running);
            operation.VerificationFilter = "c1388-filter";
            await db.SaveChangesAsync();
        });

    [Test]
    public async Task C1388_UnrelatedHoldStillRefusesResume() =>
        await RefuseAsync("c1388-held-writer", "verification_ticket_changed", async world =>
        {
            await using var db = world.CreateContext();
            var operation = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == world.Owner.Id);
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == operation.ApprovalLandRequestId);
            request.State = LandRequestState.Held;
            request.HoldReasonCode = "repository_or_source_writer";
            await db.SaveChangesAsync();
        });

    [Test]
    public async Task C1388_LandWorktreeIdentityChangeRefusesResume() =>
        await RefuseAsync("c1388-identity", "land_worktree_identity_changed", async world =>
        {
            var land = await LandPathAsync(world);
            var gitDir = (await ScratchGitRepo.GitInAsync(land, "rev-parse", "--absolute-git-dir")).StdOut.Trim();
            var locked = Path.Combine(gitDir, "locked");
            File.Exists(locked).ShouldBeTrue();
            await File.WriteAllTextAsync(locked, "c1388-not-antiphon-land");
        });

    [Test]
    public async Task C1388_TrackedTreeChangeRefusesResume() =>
        await RefuseAsync("c1388-tracked", "verification_tracked_tree_changed", async world =>
        {
            var readme = Path.Combine(await LandPathAsync(world), "README.md");
            File.Exists(readme).ShouldBeTrue();
            await File.AppendAllTextAsync(readme, "c1388 tracked\n");
        });

    [Test]
    public async Task C1388_SourceTreeChangeLandsWithResidue()
    {
        // CARD-1463 (CARD-0688 D-2/D-12): the task worktree is a live checkout, so a tracked change there during
        // the window is guarded-cleanup residue, never a refusal: the land publishes and cleanup keeps the tree.
        // Until CARD-1463 this case was C1388_SourceTreeChangeRefusesResume (verification_tracked_tree_changed).
        await using var child = new LandVerificationWindowHarness();
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, landingVerifier: child.Verifier);
        var origin = await ScratchGitRepo.GitInAsync(world.Remote, "rev-parse", "refs/heads/master");
        origin.Ok.ShouldBeTrue(origin.StdErr);
        var originSha = origin.StdOut.Trim();
        await using var scope = world.Services.CreateAsyncScope();
        var lands = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
        await lands.RequestAsync(world.Owner.Id, new LandAgentTaskRequest("c1463-source", world.OwnerSha), CancellationToken.None);
        var landing = lands.RunAsync(world.Owner.Id, null, CancellationToken.None);
        string owner;
        try
        {
            await child.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var land = await LandPathAsync(world);
            var source = await SourcePathAsync(world);
            SameDirectory(land, source).ShouldBeFalse();
            owner = Path.Combine(source, "owner.md");
            File.Exists(owner).ShouldBeTrue();
            await File.AppendAllTextAsync(owner, "c1463 source\n");
        }
        finally
        {
            child.Release();
            await landing;
        }

        await using var final = world.CreateContext();
        var operation = await final.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.TaskId == world.Owner.Id);
        operation.Publication.ShouldBe(LandPublicationOutcome.Landed, operation.LastReason);
        operation.RemoteConfirmedAt.ShouldNotBeNull();
        operation.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        operation.LastReason.ShouldBe("source_dirty");
        (await File.ReadAllTextAsync(owner)).ShouldEndWith("c1463 source\n");
        var after = await ScratchGitRepo.GitInAsync(world.Remote, "rev-parse", "refs/heads/master");
        after.Ok.ShouldBeTrue(after.StdErr);
        after.StdOut.Trim().ShouldNotBe(originSha, "the land must publish despite the task worktree's dirt");
    }

    [Test]
    public async Task C1388_LandWorktreeHeadChangeRefusesResume() =>
        await RefuseAsync("c1388-head", "land_worktree_changed", async world =>
        {
            var land = await LandPathAsync(world);
            // The registration list is parsed before this rev-parse. Moving HEAD during the pause
            // refuses as identity, so the move has to happen on the validation command itself.
            var armed = true;
            world.Git.BeforeCommand = async (directory, arguments) =>
            {
                if (!armed || !SameDirectory(directory, land) || !IsLandHeadVerify(arguments)) return null;
                armed = false;
                world.Git.BeforeCommand = null;
                await File.WriteAllTextAsync(Path.Combine(land, "c1388-head.txt"), "c1388 head moved\n");
                (await ScratchGitRepo.GitInAsync(land, "add", "--", "c1388-head.txt")).Ok.ShouldBeTrue();
                (await ScratchGitRepo.GitInAsync(land, "commit", "-m", "c1388 head")).Ok.ShouldBeTrue();
                return null;
            };
        });

    private static async Task RefuseAsync(string filter, string code, Func<RepairSourceWorld, Task> mutate)
    {
        await using var child = new LandVerificationWindowHarness();
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, landingVerifier: child.Verifier);
        var origin = await ScratchGitRepo.GitInAsync(world.Remote, "rev-parse", "refs/heads/master");
        origin.Ok.ShouldBeTrue(origin.StdErr);
        var originSha = origin.StdOut.Trim();
        await using var scope = world.Services.CreateAsyncScope();
        var lands = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
        await lands.RequestAsync(world.Owner.Id, new LandAgentTaskRequest(filter, world.OwnerSha), CancellationToken.None);
        var landing = lands.RunAsync(world.Owner.Id, null, CancellationToken.None);
        try
        {
            await child.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await mutate(world);
        }
        finally
        {
            child.Release();
            await landing;
        }

        await using var final = world.CreateContext();
        var operation = await final.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.TaskId == world.Owner.Id);
        operation.LastReason.ShouldBe(code);
        operation.Phase.ShouldBe(LandPhase.Refused);
        operation.Publication.ShouldBe(LandPublicationOutcome.Refused);
        operation.PushStartedAt.ShouldBeNull();
        operation.RemoteConfirmedAt.ShouldBeNull();
        var after = await ScratchGitRepo.GitInAsync(world.Remote, "rev-parse", "refs/heads/master");
        after.Ok.ShouldBeTrue(after.StdErr);
        after.StdOut.Trim().ShouldBe(originSha);
    }

    private static async Task<string> LandPathAsync(RepairSourceWorld world)
    {
        await using var db = world.CreateContext();
        var operation = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.TaskId == world.Owner.Id);
        operation.Phase.ShouldBe(LandPhase.Prepared);
        operation.LandWorktreePath.ShouldNotBeNullOrWhiteSpace();
        return operation.LandWorktreePath;
    }

    private static async Task<string> SourcePathAsync(RepairSourceWorld world)
    {
        await using var db = world.CreateContext();
        var operation = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.TaskId == world.Owner.Id);
        operation.Phase.ShouldBe(LandPhase.Prepared);
        operation.WorktreePath.ShouldNotBeNullOrWhiteSpace();
        return operation.WorktreePath;
    }

    private static bool SameDirectory(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsLandHeadVerify(IReadOnlyList<string> arguments) =>
        arguments.Count == 3 && arguments[0] == "rev-parse" && arguments[1] == "--verify"
        && arguments[2] == "HEAD^{commit}";
}
