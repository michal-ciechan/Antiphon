using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RepairSourceSettlementTests
{
    [Test]
    [Timeout(90_000)]
    public async Task C499_V17_TwoWorktreeIncidentSucceedsWithRepairSourceEvidence()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        var assignedBranch = repair.WorktreeBranch;
        var assignedPath = repair.WorktreePath;
        var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
        await world.SettleAsync("Fixed it.\n" + world.ClaimLine(c) + "\n--- next stage ---\nnext: review\nhandoff: x\n");
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == repair.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.FailureCode.ShouldBeNull();
        settled.Result.ShouldContain(world.ClaimLine(c));
        var evidence = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson).ShouldNotBeNull();
        evidence!.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        var source = evidence.Sources!.First(s => s.Origin is ProgressOrigin.RepairSource or ProgressOrigin.RepairSourceRemote);
        source.OwnerTaskId.ShouldBe(world.Owner.Id);
        source.ClaimedSha.ShouldBe(c);
        source.VerifiedSha.ShouldBe(c);
        source.RegisteredPath.ShouldBe(world.Owner.WorktreePath);
        var warning = await db.AgentTaskEvents.SingleAsync(e =>
            e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("progress=repair-source"));
        warning.Detail.ShouldContain("owner=" + DelegationReportFormatter.Short(world.Owner.Id));
        warning.Detail.ShouldContain("commit=" + c);
        var note = (await world.Note()).ShouldNotBeNull();
        note!.Body.ShouldContain("progress=repair-source");
        note.NoteHeader.ShouldContain("progress=repair-source");
        var owner = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == world.Owner.Id);
        owner.Status.ShouldBe(AgentTaskStatus.Succeeded);
        owner.WorktreePath.ShouldBe(world.Owner.WorktreePath);
        owner.WorktreeBranch.ShouldBe(world.Owner.WorktreeBranch);
        Directory.Exists(world.Owner.WorktreePath!).ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(world.Owner.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(c);
        Directory.Exists(assignedPath!).ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(assignedPath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(world.OwnerSha);
        settled.WorktreePath.ShouldBe(assignedPath);
        settled.WorktreeBranch.ShouldBe(assignedBranch);
        settled.MergeTargetRef.ShouldBeNull();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Merged)).ShouldBeFalse();
        world.Git.Trace.Any(a => a.Length > 0 && a[0] is "rebase" or "push" or "checkout" or "reset" or "merge")
            .ShouldBeFalse();
        (await world.ProgressPins()).Count.ShouldBeGreaterThanOrEqualTo(1);
        (await world.ProgressPins()).Count.ShouldBeLessThanOrEqualTo(4);
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "show-ref")).StdOut.ShouldNotContain("refs/antiphon/land/");
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_V21_NoMovementFailsWithNamedSourcesAndNoInventedCounts()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        await world.SettleAsync("I read the code.\n");
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Failed);
        settled.FailureCode.ShouldBe(AgentTaskFailureCode.CompletedWithoutProgress);
        settled.FailureReason.ShouldContain("no attributable post-dispatch progress");
        settled.FailureReason.ShouldContain(repair.WorktreePath!);
        settled.FailureReason.ShouldContain(world.OwnerRef);
        settled.FailureReason.ShouldNotContain("0 commits");
        settled.FailureReason.ShouldNotContain("0 changed");
        Directory.Exists(repair.WorktreePath!).ShouldBeTrue();
        (await db.AgentIncidents.AnyAsync(i => i.Kind == AgentIncidentKind.DelegateCompletedWithoutProgress)).ShouldBeTrue();
        (await world.Note())!.Body.ShouldContain("no attributable post-dispatch progress");
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_V22_UnclaimedMovementOnTheOwnerBranchDoesNotRescue()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        var x = await world.CommitInOwnerTreeAsync("owner continued", push: true);
        await world.SettleAsync("done without a claim.\n");
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Failed);
        settled.FailureReason.ShouldContain("unclaimed_or_unmatched_commit");
        var evidence = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson);
        evidence!.Sources!.Any(s => s.LocalObserved == x).ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(world.Owner.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(x);
    }

    [Test]
    [Timeout(90_000)]
    [Arguments("ls-remote-nonzero")]
    [Arguments("missing-worktree-dir")]
    public async Task C499_V23_UnavailableEvidenceFailsOpenWithADurableWarning(string kind)
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        if (kind == "ls-remote-nonzero")
        {
            world.Git.BeforeCommand = (_, args) => args.Count > 0 && args[0] == "ls-remote"
                ? Task.FromResult<LandingGitResult?>(new(128, "", "git_exit_128"))
                : Task.FromResult<LandingGitResult?>(null);
        }
        else
        {
            Directory.Delete(repair.WorktreePath!, true);
        }

        await world.SettleAsync("Fixed it.\n");
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.FailureCode.ShouldBeNull();
        var evidence = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson);
        evidence!.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        (await db.AgentTaskEvents.AnyAsync(e =>
            e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("progress=unavailable")))
            .ShouldBeTrue();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Merged))
            .ShouldBeFalse();
    }

    [Test]
    [Timeout(90_000)]
    [Arguments("malformed-advertisement")]
    [Arguments("fetch-fails")]
    [Arguments("status-nonzero")]
    [Arguments("log-nonzero")]
    [Arguments("lease-busy")]
    public async Task C499_V23_UnavailableEvidenceFailsOpenWithADurableWarning_RemainingArms(string kind)
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        RepositoryLease? held = null;
        if (kind is "fetch-fails" or "lease-busy")
            await world.CommitFromSecondCloneAsync(world.OwnerRef, "elsewhere");
        if (kind == "malformed-advertisement")
        {
            world.Git.BeforeCommand = (_, args) => args.Count > 0 && args[0] == "ls-remote"
                ? Task.FromResult<LandingGitResult?>(new(0, "not-a-sha\trefs/heads/x\n", ""))
                : Task.FromResult<LandingGitResult?>(null);
        }
        else if (kind == "fetch-fails")
        {
            world.Git.BeforeCommand = (_, args) => args.Count > 0 && args[0] == "fetch"
                ? Task.FromResult<LandingGitResult?>(new(128, "", "git_exit_128"))
                : Task.FromResult<LandingGitResult?>(null);
        }
        else if (kind == "status-nonzero")
            world.FilesGit.FailStatus = true;
        else if (kind == "log-nonzero")
            world.FilesGit.FailLog = true;
        else
            held = await world.Leases.TryAcquireAsync(world.Repo.Path, default);

        await world.SettleAsync("Fixed it.\n");
        if (held is not null) await held.DisposeAsync();
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        var evidence = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson);
        evidence!.Assessment.ShouldBe(CompletionProgressAssessment.Indeterminate);
        evidence.Reason.ShouldBeOneOf(
            "source_remote_unreadable", "source_remote_response_invalid", "source_remote_fetch_failed",
            "primary_status_unavailable", "primary_log_unavailable", "repository_lease_busy",
            "primary_checkout_missing");
        (await db.AgentTaskEvents.AnyAsync(e =>
            e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("progress=unavailable")))
            .ShouldBeTrue();
        if (kind == "lease-busy")
            world.Git.Trace.Any(a => a.Length > 0 && a[0] == "fetch").ShouldBeFalse();
    }

    [Test]
    [Timeout(90_000)]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C499_V18_RemoteOnlyClaimedCommitFromASecondCloneIsProgress(bool laterTip)
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        var c = await world.CommitFromSecondCloneAsync(world.OwnerRef, "elsewhere");
        var claimed = c;
        if (laterTip)
            await world.CommitFromSecondCloneAsync(world.OwnerRef, "on top");
        var remoteTip = (await ScratchGitRepo.GitInAsync(world.Remote, "rev-parse", world.OwnerRef)).StdOut.Trim();
        await world.SettleAsync(world.DoneReport("Fixed it.", claimed));
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        var evidence = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson)!;
        evidence.Sources!.Any(s => s.Origin == ProgressOrigin.RepairSourceRemote).ShouldBeTrue();
        evidence.Sources!.First(s => s.Origin == ProgressOrigin.RepairSourceRemote).RemoteObserved.ShouldBe(remoteTip);
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "rev-parse", world.OwnerRef)).StdOut.Trim()
            .ShouldBe(world.OwnerSha);
        world.Git.Trace.Any(a => a.Length > 0 && a[0] == "fetch" && a.Any(x =>
        {
            var dest = x.Contains(':') ? x[(x.LastIndexOf(':') + 1)..] : "";
            return dest.StartsWith("refs/heads/", StringComparison.Ordinal)
                || dest.StartsWith("refs/remotes/", StringComparison.Ordinal);
        })).ShouldBeFalse();
    }

    [Test]
    [Timeout(90_000)]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C499_V19_PrimaryRemoteOnlyNeedsAClaim(bool withClaim)
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true);
        var (task, _) = await world.DispatchAsync();
        var c = await world.CommitFromSecondCloneAsync("refs/heads/" + task.WorktreeBranch!, "elsewhere");
        var report = withClaim ? world.DoneReport("Fixed it.", c) : world.DoneReport("Fixed it.");
        await world.SettleAsync(report);
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
        if (withClaim)
        {
            settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
            TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson)!
                .Sources!.Any(s => s.Origin == ProgressOrigin.PrimaryRemote).ShouldBeTrue();
        }
        else
        {
            settled.Status.ShouldBe(AgentTaskStatus.Failed);
            settled.FailureCode.ShouldBe(AgentTaskFailureCode.CompletedWithoutProgress);
            settled.FailureReason.ShouldContain("unclaimed_or_unmatched_commit");
        }
    }

    [Test]
    [Timeout(90_000)]
    [Arguments("commit")]
    [Arguments("dirty")]
    public async Task C499_V20_DirectPrimaryCommitAndDirtyFileStillSucceedWithoutAClaim(string kind)
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        if (kind == "commit")
            await world.CommitInPrimaryTreeAsync("own work");
        else
            await File.WriteAllTextAsync(Path.Combine(repair.WorktreePath!, "scratch.md"), "dirty\n");
        await world.SettleAsync(world.DoneReport("Fixed it."));
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        var evidence = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson)!;
        evidence.Sources!.Any(s => s.Origin == ProgressOrigin.Primary).ShouldBeTrue();
        (await db.AgentTaskEvents.AnyAsync(e =>
            e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("progress=")))
            .ShouldBeFalse();
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_V24i_SecondOnTurnEndDoesNotMutate()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, sessionId) = await world.DispatchAsync();
        var assigned = repair.WorktreeBranch;
        var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
        await world.SettleAsync(world.DoneReport("Fixed it.", c));
        var replies = world.Services.GetRequiredService<AgentTaskReplyService>();
        await replies.OnTurnEndAsync(sessionId, CancellationToken.None);
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        settled.WorktreeBranch.ShouldBe(assigned);
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Merged))
            .ShouldBeFalse();
        world.Git.Trace.Any(a => a.Length > 0 && a[0] is "rebase" or "push" or "checkout" or "reset" or "merge")
            .ShouldBeFalse();
    }

    [Test]
    [Timeout(120_000)]
    [Arguments("direct-explicit")]
    [Arguments("alternate-explicit")]
    [Arguments("indeterminate-explicit")]
    public async Task C499_V25_MergeBackOnlyForDirectWorkWithAnExplicitTarget(string kind)
    {
        await using var world = await RepairSourceWorld.CreateAsync(explicitIntegration: true);
        var (repair, _) = await world.DispatchAsync();
        if (kind == "direct-explicit")
            await world.CommitInPrimaryTreeAsync("own work");
        else if (kind == "alternate-explicit")
        {
            var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
            await world.SettleAsync(world.DoneReport("Fixed it.", c));
        }
        else
        {
            await File.WriteAllTextAsync(Path.Combine(repair.WorktreePath!, "scratch.md"), "dirty\n");
            world.FilesGit.FailStatus = true;
            await world.SettleAsync(world.DoneReport("Fixed it."));
        }

        if (kind == "direct-explicit")
            await world.SettleAsync(world.DoneReport("Fixed it."));

        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        var merged = await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Merged);
        if (kind == "direct-explicit")
        {
            merged.ShouldBeTrue();
            Directory.Exists(repair.WorktreePath!).ShouldBeFalse();
        }
        else
        {
            settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
            merged.ShouldBeFalse();
            var note = await world.Note();
            note!.NoteHeader.ShouldNotContain("merged");
            Directory.Exists(repair.WorktreePath!).ShouldBeTrue();
            if (kind == "indeterminate-explicit")
                (await ScratchGitRepo.GitInAsync(repair.WorktreePath!, "status", "--porcelain")).StdOut.Trim()
                    .ShouldNotBeEmpty();
        }
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_V26_BaselineAndEvidenceSurviveReplayAndDuplicateSettlement()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, sessionId) = await world.DispatchAsync();
        var baseline = repair.ProgressBaselineJson;
        var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
        await world.SettleAsync(world.DoneReport("Fixed it.", c));
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        settled.ProgressBaselineJson.ShouldBe(baseline);
        TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson)!.SchemaVersion.ShouldBe(1);
        var queuedAt = settled.CompletionNoteQueuedAt;
        var replies = world.Services.GetRequiredService<AgentTaskReplyService>();
        await replies.OnTurnEndAsync(sessionId, CancellationToken.None);
        db.ChangeTracker.Clear();
        var again = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        (await db.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("progress=")))
            .ShouldBe(1);
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == repair.Id && m.AgentSessionId == world.CallerSessionId))
            .ShouldBe(1);
        again.CompletionNoteQueuedAt.ShouldBe(queuedAt);
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_V27_ForeignClaimAndProsePathsConferNothing()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
        var other = Guid.NewGuid();
        var report = $"Fixed it.\n[antiphon-progress:{other:D} commit={c}]\n"
            + $"pushed refs/heads/master at {world.Repo.Path}\n{c}\n";
        await world.SettleAsync(report);
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Failed);
        var evidence = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson)!;
        evidence.ClaimedSha.ShouldBeNull();
        evidence.ClaimWarning.ShouldBe("claim_not_for_this_task");
        evidence.Sources!.Count.ShouldBe(2);
        (await db.AgentTaskEvents.AnyAsync(e =>
            e.AgentTaskId == repair.Id && e.Type == AgentTaskEventType.Warning
            && (e.Detail.Contains("claim_not_for_this_task") || e.Detail.Contains("progress="))))
            .ShouldBeTrue();
    }

    [Test]
    [Timeout(90_000)]
    [Arguments("failed")]
    [Arguments("blocked")]
    public async Task C499_V28_FailedAndBlockedReportsSkipTheProbe(string verdict)
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        world.Git.Trace.Clear();
        var token = DelegationReportFormatter.ReportToken(DelegationReportFormatter.Short(repair.Id), verdict);
        await world.SettleAsync($"it broke.\n{token}\n");
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        if (verdict == "failed")
        {
            settled.Status.ShouldBe(AgentTaskStatus.Failed);
            settled.FailureReason.ShouldContain("it broke");
        }
        else
            settled.Status.ShouldBe(AgentTaskStatus.Blocked);
        settled.CompletionProgressEvidenceJson.ShouldBeNull();
        world.Git.Trace.Any(a => a.Length > 0 && a[0] == "ls-remote").ShouldBeFalse();
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_V29_DetailProjectsProgressEvidence()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
        await world.SettleAsync(world.DoneReport("Fixed it.", c));
        await using var scope = world.Services.CreateAsyncScope();
        var detail = await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
            .GetAsync(repair.Id, CancellationToken.None);
        detail.ProgressEvidence.ShouldNotBeNull();
        detail.ProgressEvidence!.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        var origin = detail.ProgressEvidence.Sources!.First(s =>
            s.Origin is ProgressOrigin.RepairSource or ProgressOrigin.RepairSourceRemote);
        origin.Origin.ShouldBeOneOf(ProgressOrigin.RepairSource, ProgressOrigin.RepairSourceRemote);
        origin.Commit.ShouldBe(c);
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_V30_ProbePinsAreBoundedAcrossRetries()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
        var body = world.DoneReport("Fixed it.", c);
        await world.SettleAsync(body);
        var svc = world.Services.GetRequiredService<TaskCompletionProgressService>();
        await using var db = world.CreateContext();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        await svc.EvaluateAsync(row, body, CancellationToken.None);
        await svc.EvaluateAsync(row, body, CancellationToken.None);
        (await world.ProgressPins()).Count.ShouldBeLessThanOrEqualTo(4);
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "show-ref")).StdOut.ShouldNotContain("refs/antiphon/land/");
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_R23_AFutureDatedBaseCommitDoesNotRescueASnapshottedTask()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        await world.AmendOwnerCommitDateAsync(DateTimeOffset.UtcNow.AddHours(1));
        var (repair, _) = await world.DispatchAsync();
        await world.SettleAsync(world.DoneReport("I read the code."));
        await using var db = world.CreateContext();
        var settled = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Failed);
        settled.FailureCode.ShouldBe(AgentTaskFailureCode.CompletedWithoutProgress);
        var evidence = TaskProgressJson.TryReadEvidence(settled.CompletionProgressEvidenceJson)!;
        evidence.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_R24_ALegacyTaskWithoutABaselineGetsNoRemoteCredit()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true);
        var (task, _) = await world.DispatchAsync();
        await using (var db = world.CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            row.ProgressBaselineJson = null;
            await db.SaveChangesAsync();
        }
        var c = await world.CommitFromSecondCloneAsync("refs/heads/" + task.WorktreeBranch!, "elsewhere");
        await world.SettleAsync(world.DoneReport("Fixed it.", c));
        await using var verify = world.CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Failed);
        settled.FailureCode.ShouldBe(AgentTaskFailureCode.CompletedWithoutProgress);
        settled.FailureReason.ShouldContain("no attributable post-dispatch progress");
        settled.FailureReason.ShouldNotContain("0 commits");
    }

    [Test]
    [Timeout(120_000)]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C499_V38_ProgressWarningReachesTheCallerSession(bool busy)
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
        await using var bridge = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = world.Schema.ConnectionString,
        });
        await using (var db = world.CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
            row.ParentSessionId = bridge.SessionId;
            await db.SaveChangesAsync();
            world.Repair = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == repair.Id);
        }
        await world.SettleAsync(world.DoneReport("Fixed it.", c));
        var queued = (await world.CreateContext().SessionQueuedMessages
            .SingleAsync(m => m.SourceTaskId == repair.Id && m.AgentSessionId == bridge.SessionId));
        await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
            world.Schema.ConnectionString, bridge, queued, bridge.SessionId, busy);
        await using var verify = world.CreateContext();
        var prompt = await verify.TranscriptEntries.SingleAsync(e =>
            e.AgentSessionId == bridge.SessionId && e.Kind == TranscriptKinds.UserPrompt);
        prompt.Text.ShouldBe(queued.Body);
        prompt.Text.ShouldContain("progress=repair-source; owner=");
    }

    [Test]
    [Timeout(120_000)]
    [Arguments("queue-inserted", true)]
    [Arguments("queue-inserted", false)]
    [Arguments("lost-wakeup", true)]
    [Arguments("lost-wakeup", false)]
    public async Task C499_V38b_ProgressWarningSurvivesQueueCuts(string cut, bool busy)
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, _) = await world.DispatchAsync();
        var c = await world.CommitInOwnerTreeAsync("repair work", push: true);
        await using var bridge = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = world.Schema.ConnectionString,
        });
        await using (var db = world.CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
            row.ParentSessionId = bridge.SessionId;
            await db.SaveChangesAsync();
            world.Repair = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == repair.Id);
        }
        await world.SettleAsync(world.DoneReport("Fixed it.", c));
        var queued = await world.CreateContext().SessionQueuedMessages
            .SingleAsync(m => m.SourceTaskId == repair.Id && m.AgentSessionId == bridge.SessionId);
        await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
            world.Schema.ConnectionString, bridge, queued, bridge.SessionId, busy, cut);
        await using var verify = world.CreateContext();
        var prompt = await verify.TranscriptEntries.SingleAsync(e =>
            e.AgentSessionId == bridge.SessionId && e.Kind == TranscriptKinds.UserPrompt);
        prompt.Text.ShouldBe(queued.Body);
        prompt.Text.ShouldContain("progress=repair-source; owner=");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C499_V39_RepairBriefIsTypedWithTheRepairBlock()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, sessionId) = await world.DispatchAsync();
        await using var bridge = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = world.Schema.ConnectionString,
        });
        bridge.Runtime.Register(sessionId, bridge.Adapter);
        await using var db = world.CreateContext();
        var brief = await db.SessionQueuedMessages.SingleAsync(m =>
            m.AgentSessionId == sessionId && m.Origin == QueuedMessageOrigin.Delegation);
        await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
            world.Schema.ConnectionString, bridge, brief, sessionId, busy: false);
        await using var verify = world.CreateContext();
        var prompt = await verify.TranscriptEntries.SingleAsync(e =>
            e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt);
        prompt.Text.ShouldBe(brief.Body);
        prompt.Text.ShouldContain(DelegationReportFormatter.TaskMarker(repair.Id));
        prompt.Text.ShouldContain(DelegationReportFormatter.Short(world.Owner.Id));
        prompt.Text.ShouldContain(world.OwnerRef);
        prompt.Text.ShouldContain(world.OwnerSha);
        prompt.Text.ShouldContain($"[antiphon-progress:{repair.Id:D} commit=");
    }

    [Test]
    [Timeout(90_000)]
    public async Task C499_R17b_CancelledSettlementLeavesTheTaskDispatched()
    {
        await using var world = await RepairSourceWorld.CreateAsync();
        var (repair, sessionId) = await world.DispatchAsync();
        world.Git.BeforeCommand = (_, args) => args.Count > 0 && args[0] == "ls-remote"
            ? throw new OperationCanceledException()
            : Task.FromResult<LandingGitResult?>(null);
        await TurnSeeding.SeedTurnAsync(world.CreateContext, sessionId, DelegationReportFormatter.TaskMarker(repair.Id), "done.\n");
        var replies = world.Services.GetRequiredService<AgentTaskReplyService>();
        await Should.ThrowAsync<OperationCanceledException>(() => replies.OnTurnEndAsync(sessionId, CancellationToken.None));
        await using var db = world.CreateContext();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == repair.Id);
        row.Status.ShouldBe(AgentTaskStatus.Dispatched);
        row.CompletionProgressEvidenceJson.ShouldBeNull();
        (await world.Note()).ShouldBeNull();
    }
}
