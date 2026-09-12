using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
