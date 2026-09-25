using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandIndexLockTests
{
    [Test]
    public async Task Stale_lock_holds_before_admission()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var path = await CreateLockAsync(h.Fixture.Repository, TimeSpan.FromHours(1));
        var result = await h.RunAsync();
        result.ShouldBe(LandRunResult.Held);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
        request.State.ShouldBe(LandRequestState.Held);
        request.HoldReasonCode.ShouldBe(GitIndexLock.StaleCode);
        request.HoldDetail.ShouldContain(path);
        request.HoldDetail.ShouldContain("Remove-Item");
        request.Attempt.ShouldBe(0);
        (await h.OperationAsync()).ShouldBeNull();
        h.Verifier.Calls.ShouldBe(0);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Fixture.TaskId
            && n.Kind == LandNotificationKind.Held)).ShouldBe(1);
    }

    [Test]
    public async Task Fresh_lock_holds_as_held_then_ages_to_stale()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var path = await CreateLockAsync(h.Fixture.Repository, age: null);
        (await h.RunAsync()).ShouldBe(LandRunResult.Held);
        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
            request.HoldReasonCode.ShouldBe(GitIndexLock.HeldCode);
            request.HoldEpisode.ShouldBe(1);
        }
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromHours(1));
        (await h.RunAsync()).ShouldBe(LandRunResult.Held);
        await using var again = h.CreateContext();
        var aged = await again.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
        aged.HoldReasonCode.ShouldBe(GitIndexLock.StaleCode);
        aged.HoldEpisode.ShouldBe(2);
        (await again.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.Held)).ShouldBe(2);
    }

    [Test]
    public async Task Removing_the_lock_resumes_the_land()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var path = await CreateLockAsync(h.Fixture.Repository, TimeSpan.FromHours(1));
        (await h.RunAsync()).ShouldBe(LandRunResult.Held);
        File.Delete(path);
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.HeldReleased)).ShouldBe(1);
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.Landed)).ShouldBeTrue();
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task Source_worktree_lock_refuses_before_rebase()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Repository, "master-move.txt"), "moved\n");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "add", ".");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "-m", "move master");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", "HEAD:refs/heads/master");
        var lockPath = await ResolveLockAsync(h.Fixture.Source);
        var created = false;
        h.Fixture.Git.BeforeCommand = (repo, args) =>
        {
            if (!created
                && GitIndexLock.PathsEqual(repo, h.Fixture.Source)
                && args.Contains("--git-path")
                && args.Contains("index.lock")
                && h.Fixture.Git.Trace.Any(a => a.Length > 0 && a[0] == "update-ref"))
            {
                File.WriteAllBytes(lockPath, []);
                File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow - TimeSpan.FromHours(1));
                created = true;
            }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        created.ShouldBeTrue();
        await using var db = h.CreateContext();
        var refused = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.LandRefused);
        refused.Detail.ShouldStartWith("land refused: git_index_lock_stale;");
        refused.Detail.ShouldContain(lockPath);
        var op = await h.OperationAsync();
        op.ShouldNotBeNull();
        op!.Phase.ShouldBe(LandPhase.RecoveryPinned);
        op.LastReason.ShouldBe(GitIndexLock.StaleCode);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase"));
    }

    [Test]
    public async Task Target_lock_refuses_before_ff_and_same_request_resumes()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = await h.AddSourceAsync();
        var lockPath = await ResolveLockAsync(h.Fixture.Repository);
        var created = false;
        h.Fixture.Git.BeforeCommand = (repo, args) =>
        {
            if (!created
                && GitIndexLock.PathsEqual(repo, h.Fixture.Repository)
                && args.Contains("--git-path")
                && args.Contains("index.lock")
                && h.Fixture.Git.Trace.Any(a => a.Contains("rebase")))
            {
                File.WriteAllBytes(lockPath, []);
                File.SetLastWriteTimeUtc(lockPath, DateTime.UtcNow - TimeSpan.FromHours(1));
                created = true;
            }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        created.ShouldBeTrue();
        var first = await h.OperationAsync();
        first.ShouldNotBeNull();
        first!.Phase.ShouldBe(LandPhase.TargetAdvanceStarted);
        first.LastReason.ShouldBe(GitIndexLock.StaleCode);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only"));
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Length > 0 && a[0] == "push");
        var targetBefore = first.TargetBeforeSha;
        File.Delete(lockPath);
        h.Fixture.Git.BeforeCommand = null;
        h.Fixture.Git.Trace.Clear();
        await h.RequestAsync(expectedSourceSha: sha);
        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Complete);
        var second = await h.OperationAsync();
        second.ShouldNotBeNull();
        second!.Id.ShouldBe(first.Id);
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.Landed)).ShouldBeTrue();
        (await db.AgentTaskEvents.AnyAsync(e => e.Detail.Contains("land_request_identity_conflict"))).ShouldBeFalse();
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", "refs/heads/master")).Trim()
            .ShouldNotBe(targetBefore);
    }

    [Test]
    public async Task Lock_created_during_rebase_is_reported_held_and_same_sha_repost_lands()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = await h.AddSourceAsync();
        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Repository, "master-move.txt"), "moved\n");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "add", ".");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "-m", "move master");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", "HEAD:refs/heads/master");
        var lockPath = await ResolveLockAsync(h.Fixture.Source);
        var created = false;
        h.Fixture.Git.BeforeCommand = (repo, args) =>
        {
            if (!created
                && GitIndexLock.PathsEqual(repo, h.Fixture.Source)
                && args.Contains("rebase")
                && !args.Contains("--abort")
                && !File.Exists(lockPath))
            {
                File.WriteAllBytes(lockPath, []);
                created = true;
            }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        created.ShouldBeTrue();
        var first = await h.OperationAsync();
        first.ShouldNotBeNull();
        first!.LastReason.ShouldBe(GitIndexLock.HeldCode);
        first.LastReason.ShouldNotBe("interrupted_rebase_requires_inspection");
        first.Phase.ShouldBe(LandPhase.Refused);
        h.Fixture.Git.Trace.ShouldContain(a => a.Contains("rebase") && !a.Contains("--abort"));
        await using (var db = h.CreateContext())
        {
            var refused = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId
                && e.Type == AgentTaskEventType.LandRefused);
            refused.Detail.ShouldContain(lockPath);
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == h.Fixture.TaskId
                && e.Detail.Contains("interrupted_rebase_requires_inspection"))).ShouldBeFalse();
        }
        File.Delete(lockPath);
        h.Fixture.Git.BeforeCommand = null;
        h.Fixture.Git.Trace.Clear();
        await h.RequestAsync(expectedSourceSha: sha);
        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Complete);
        await using var again = h.CreateContext();
        (await again.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.Landed)).ShouldBeTrue();
        (await again.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Detail.Contains("interrupted_rebase_requires_inspection"))).ShouldBeFalse();
        var second = await h.OperationAsync();
        second.ShouldNotBeNull();
        second!.Id.ShouldNotBe(first.Id);
    }

    [Test]
    public async Task Lock_created_during_ff_merge_is_reported_held()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var lockPath = await ResolveLockAsync(h.Fixture.Repository);
        h.Fixture.Git.BeforeCommand = (repo, args) =>
        {
            if (GitIndexLock.PathsEqual(repo, h.Fixture.Repository)
                && args.Contains("merge")
                && args.Contains("--ff-only")
                && !File.Exists(lockPath))
            {
                File.WriteAllBytes(lockPath, []);
            }
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        var op = await h.OperationAsync();
        op.ShouldNotBeNull();
        op!.LastReason.ShouldBe(GitIndexLock.HeldCode);
        op.LastReason.ShouldNotBe("target_advance_failed");
        op.Phase.ShouldBe(LandPhase.TargetAdvanceStarted);
        await using var db = h.CreateContext();
        var refused = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.LandRefused);
        refused.Detail.ShouldContain(lockPath);
    }

    [Test]
    public async Task Missing_source_worktree_does_not_hold_as_index_lock()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "remove", "--force", h.Fixture.Source);
        Directory.Exists(h.Fixture.Source).ShouldBeFalse();
        var result = await h.RunAsync();
        result.ShouldNotBe(LandRunResult.Held);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
        request.State.ShouldNotBe(LandRequestState.Held);
        request.HoldReasonCode.ShouldNotBe(GitIndexLock.HeldCode);
        request.HoldReasonCode.ShouldNotBe(GitIndexLock.StaleCode);
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.Held)).ShouldBeFalse();
        var refused = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.LandRefused);
        refused.Detail.ShouldNotContain(GitIndexLock.HeldCode);
        refused.Detail.ShouldNotContain("index_lock");
    }

    [Test]
    public async Task Reland_after_removed_worktree_is_not_held_as_index_lock()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        if (Directory.Exists(h.Fixture.Source))
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "remove", "--force", h.Fixture.Source);
        Directory.Exists(h.Fixture.Source).ShouldBeFalse();
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.WorktreePath.ShouldNotBeNull();
            Directory.Exists(task.WorktreePath!).ShouldBeFalse();
        }
        await h.RequestAsync();
        var result = await h.RunAsync();
        result.ShouldNotBe(LandRunResult.Held);
        await using var again = h.CreateContext();
        var request = await again.AgentTaskLandRequests
            .OrderByDescending(r => r.RequestedAt)
            .FirstAsync(r => r.TaskId == h.Fixture.TaskId);
        request.State.ShouldNotBe(LandRequestState.Held);
        request.HoldReasonCode.ShouldNotBe(GitIndexLock.HeldCode);
        request.HoldReasonCode.ShouldNotBe(GitIndexLock.StaleCode);
        (await again.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.Held
            && e.Detail != null
            && (e.Detail.Contains(GitIndexLock.HeldCode) || e.Detail.Contains("index_lock_path_error"))))
            .ShouldBe(0);
    }

    [Test]
    public async Task Existing_source_worktree_with_broken_git_holds_as_index_lock()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = await h.AddSourceAsync();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        var gitPath = Path.Combine(h.Fixture.Source, ".git");
        File.Exists(gitPath).ShouldBeTrue();
        File.Delete(gitPath);
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await h.RequestAsync(expectedSourceSha: sha);
        var result = await h.RunQueuedAsync();
        result.ShouldBe(LandRunResult.Held);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
        request.State.ShouldBe(LandRequestState.Held);
        request.HoldReasonCode.ShouldBe(GitIndexLock.HeldCode);
        request.HoldDetail.ShouldNotBeNull();
        request.HoldDetail.ShouldContain(GitIndexLock.PathErrorReason);
        request.Attempt.ShouldBe(0);
        (await h.OperationAsync()).ShouldBeNull();
        h.Verifier.Calls.ShouldBe(0);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Fixture.TaskId
            && n.Kind == LandNotificationKind.Held)).ShouldBe(1);
    }

    [Test]
    public async Task Update_ref_advance_ignores_repository_lock()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "checkout", "--detach");
        var path = await CreateLockAsync(h.Fixture.Repository, TimeSpan.FromHours(1));
        var probes = new List<string>();
        h.Fixture.Git.BeforeCommand = (repo, args) =>
        {
            if (args.Contains("--git-path") && args.Contains("index.lock"))
                probes.Add(Path.GetFullPath(repo));
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        File.Exists(path).ShouldBeTrue();
        probes.ShouldNotContain(p => GitIndexLock.PathsEqual(p, h.Fixture.Repository));
        h.Fixture.Git.Trace.ShouldContain(a => a.Length > 0 && a[0] == "update-ref" && a.Contains("--no-deref"));
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.Landed)).ShouldBeTrue();
        (await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId))
            .HoldReasonCode.ShouldBeNull();
    }

    private static async Task<string> ResolveLockAsync(string checkout)
    {
        var resolved = await ScratchGitRepo.GitInAsync(checkout,
            "rev-parse", "--path-format=absolute", "--git-path", "index.lock");
        resolved.Ok.ShouldBeTrue();
        return resolved.StdOut.Trim();
    }

    private static async Task<string> CreateLockAsync(string checkout, TimeSpan? age)
    {
        var path = await ResolveLockAsync(checkout);
        File.WriteAllBytes(path, []);
        if (age is { } value)
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - value);
        return path;
    }

    [Test]
    [Arguments("land-worktree")]
    [Arguments("main-checkout")]
    public async Task C688_AdmissionProbesLandWorktreeAndMainCheckoutWithoutListing(string where)
    {
        // CARD-0688 V-19 (D-10): admission probes the land worktree and the main checkout found from
        // <common>/HEAD, never through a worktree listing; removing the lock resumes on the next sweep.
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var workspace = new Antiphon.Server.Infrastructure.Git.LandWorkspace(h.Fixture.Git,
            Microsoft.Extensions.Options.Options.Create(new Antiphon.Server.Application.Settings.GitSettings
            { WorktreeBasePath = Path.Combine(h.Fixture.Root, "trees") }), TimeProvider.System);
        var common = await h.Fixture.Git.CommonDirectoryAsync(h.Fixture.Repository, CancellationToken.None);
        var land = workspace.PathFor(common);
        (await workspace.EnsureAsync(h.Fixture.Repository, land, h.Fixture.SeedSha, null, CancellationToken.None)).Reason.ShouldBeNull();
        var path = await CreateLockAsync(where == "land-worktree" ? land : h.Fixture.Repository, TimeSpan.FromHours(1));
        h.Fixture.Git.Trace.Clear();

        (await h.RunAsync()).ShouldBe(LandRunResult.Held);

        h.Fixture.Git.Trace.ShouldNotContain(a => a.Length > 1 && a[0] == "worktree" && a[1] == "list");
        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
            request.State.ShouldBe(LandRequestState.Held);
            request.HoldReasonCode.ShouldBe(GitIndexLock.StaleCode);
            request.HoldDetail.ShouldContain(path);
        }
        (await h.OperationAsync()).ShouldBeNull();
        File.Delete(path);
        await h.SweepAsync();

        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Complete);

        (await h.OperationAsync()).ShouldNotBeNull().Publication.ShouldBe(LandPublicationOutcome.Landed);
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
