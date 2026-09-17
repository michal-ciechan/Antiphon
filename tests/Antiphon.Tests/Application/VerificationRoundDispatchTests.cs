using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.InterimVerificationPolicyTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-3/V-6, R-2/R-5. The queued Interim lifecycle through the production dispatcher
/// (<see cref="AgentTaskDispatcher.TickAsync"/>), task lifecycle services, restart and land admission.
/// A loss of eligibility while queued holds the task and launches nothing; a running task finishes.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class VerificationRoundDispatchTests
{
    [Test]
    public async Task C544_AncestryAtPreparation()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();

        // Related history: the baseline SHA is the owner head itself -> launches.
        var related = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        await TickAsync(world);
        (await world.TaskAsync(related.Id)).Status.ShouldNotBe(AgentTaskStatus.Queued, "related-history launched");
        (await world.TaskAsync(related.Id)).Status.ShouldNotBe(AgentTaskStatus.Blocked, "related-history launched");

        // Unrelated history: an orphan commit that is not an ancestor of the owner branch.
        await world.Repo.GitAsync("checkout", "--orphan", "c544-orphan");
        await world.Repo.CommitFileAsync("orphan.md", "unrelated\n");
        var orphan = (await world.Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await world.Repo.GitAsync("checkout", "-f", "master");
        await UpdateTaskAsync(world, related.Id, t => { t.Status = AgentTaskStatus.Succeeded; t.CompletedAt = DateTime.UtcNow; });
        var unrelated = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        await using (var db = world.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == unrelated.Id);
            var admission = VerificationAdmission.TryRead(task.VerificationAdmissionJson)!;
            task.VerificationAdmissionJson = (admission with { BaselineReviewedSha = orphan }).Serialize();
            await db.SaveChangesAsync();
        }
        await TickAsync(world);
        var held = await world.TaskAsync(unrelated.Id);
        held.Status.ShouldBe(AgentTaskStatus.Blocked, "unrelated-history");
        held.FailureReason.ShouldNotBeNull().ShouldStartWith(InterimVerificationPolicy.BaselineInvalidCode, Case.Sensitive, "unrelated-history");
        held.FailureReason.ShouldContain("commission a new Final review", Case.Sensitive, "unrelated-history");
        held.AgentSessionId.ShouldBeNull("unrelated-history: no launch");
        held.DispatchedAt.ShouldBeNull("unrelated-history: no launch");
        (await world.TaskAsync(held.Id)).VerificationBaselineOutcomeId.ShouldBe(baseline.Id, "no substituted baseline");
    }

    [Test]
    public async Task C544_QueuePolicyRevocation()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var queued = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        await world.SetCardPolicyAsync(CardVerificationPolicy.AllowInterim, CardVerificationPolicy.FullOnly);
        await TickAsync(world);
        await AssertHeldAsync(world, queued.Id, InterimVerificationPolicy.InterimDisallowedCode, "review-policy-revoked");
    }

    [Test]
    public async Task C544_QueueReadinessLoss()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        foreach (var (row, lose) in new (string, Action)[]
                 {
                     ("stale", () => world.Readiness.Verdict = InterimReadiness.Unready("monitor_stale")),
                     ("read-failed", () => world.Readiness.Throw = new IOException("readiness file locked")),
                 })
        {
            world.Readiness.Throw = null;
            world.Readiness.Verdict = new InterimReadiness(true, "ready", ControlledInterimReadiness.ReadySnapshot);
            var queued = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
            var readsAtAdmission = world.Readiness.Reads;
            lose();
            await TickAsync(world);
            world.Readiness.Reads.ShouldBeGreaterThan(readsAtAdmission, row + ": queued recheck rereads readiness");
            await AssertHeldAsync(world, queued.Id, InterimVerificationPolicy.BackstopUnreadyCode, row);
        }
    }

    [Test]
    public async Task C544_QueueBaselineLoss()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var queued = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        await using (var db = world.CreateContext())
            await new StageOutcomeService(db).RecordFindingAsync(baseline.StageTaskId!.Value,
                new RecordStageFindingRequest("Review", Found: true, Detail: "override supersedes the baseline"), CancellationToken.None);
        await TickAsync(world);
        await AssertHeldAsync(world, queued.Id, InterimVerificationPolicy.BaselineInvalidCode, "baseline-superseded");
    }

    [Test]
    public async Task C544_ProfileContinuity()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var created = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        var original = await world.TaskAsync(created.Id);

        async Task AssertKeptAsync(string row)
        {
            var now = await world.TaskAsync(created.Id);
            now.VerificationRound.ShouldBe(VerificationRound.Interim, row);
            now.VerificationProfileVersion.ShouldBe(1, row);
            now.VerificationSubjectTaskId.ShouldBe(original.VerificationSubjectTaskId, row);
            now.VerificationBaselineOutcomeId.ShouldBe(original.VerificationBaselineOutcomeId, row);
            now.VerificationAdmissionJson.ShouldBe(original.VerificationAdmissionJson, row);
        }

        await UpdateTaskAsync(world, created.Id, t => t.ModelLevel = AgentModelLevel.Medium);
        await using (var scope = world.Services.CreateAsyncScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
            await tasks.EscalateAsync(created.Id, AgentModelLevel.High, CancellationToken.None);
        }
        await AssertKeptAsync("escalate-queued");
        await using (var scope = world.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .RerouteAsync(created.Id, AgentKind.ClaudeCode, AgentModelLevel.High, CancellationToken.None);
        await AssertKeptAsync("reroute-queued");
        await UpdateTaskAsync(world, created.Id, t => { t.Status = AgentTaskStatus.Failed; t.CompletedAt = DateTime.UtcNow; t.FailureReason = "c544 transient"; });
        await using (var scope = world.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(created.Id, CancellationToken.None);
        await AssertKeptAsync("retry-failed");

        // The continued round settles a report claiming Full; the commissioned round caps it.
        var sessionId = await world.DispatchAsync(created.Id);
        await world.SeedTurnAsync(sessionId, created.Id,
            C544World.ReviewReport(created.Id, world.Owner.Id, world.OwnerSha, "Full", found: false, next: "land"));
        await world.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertKeptAsync("settled");
        await using var verify = world.CreateContext();
        var outcome = await verify.StageOutcomes.AsNoTracking().SingleAsync(o => o.StageTaskId == created.Id);
        outcome.CommissionedRound.ShouldBe(VerificationRound.Interim, "settled");
        outcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Interim, "settled: cannot mint Full");
        (await world.TaskAsync(created.Id)).NextStage.ShouldBe(PipelineHandoffKind.Review, "settled: cannot relabel itself land-ready");
    }

    [Test]
    public async Task C544_ProfileRoundTrip()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var created = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        int cardRevision;
        await using (var db = world.CreateContext())
            cardRevision = (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == world.Card.Id)).RevisionCount;

        for (var pass = 0; pass < 2; pass++)
        {
            var row = pass == 0 ? "fresh-context" : "after-restart";
            if (pass == 1) await world.RestartAsync();
            await using var db = world.CreateContext();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
            var admission = VerificationAdmission.TryRead(task.VerificationAdmissionJson).ShouldNotBeNull(row);
            admission.Selection.ArtifactPath.ShouldBe(C544World.SelectionPath, row);
            admission.Selection.ArtifactCommitSha.ShouldBe(world.SelectionSha, row);
            admission.Selection.Section.ShouldBe(C544World.Section, row);
            admission.BaselineOutcomeId.ShouldBe(baseline.Id, row);
            admission.BaselineReviewedSha.ShouldBe(baseline.ReviewedSourceSha, row);
            admission.SubjectTaskId.ShouldBe(world.Owner.Id, row);
            admission.CardId.ShouldBe(world.Card.Id, row);
            admission.CardRevision.ShouldBe(cardRevision, row);
            admission.CardPolicy.ShouldBe(CardVerificationPolicy.AllowInterim, row);
            admission.Readiness.QualificationArtifactCommitSha.ShouldBe(ControlledInterimReadiness.ReadySnapshot.QualificationArtifactCommitSha, row);
            admission.Readiness.ScheduledRunId.ShouldBe(ControlledInterimReadiness.ReadySnapshot.ScheduledRunId, row);
            admission.Readiness.WindmillJobId.ShouldBe(ControlledInterimReadiness.ReadySnapshot.WindmillJobId, row);
            admission.Readiness.RecipientEvidenceIds.ShouldBe(ControlledInterimReadiness.ReadySnapshot.RecipientEvidenceIds, row);
            admission.Readiness.MonitorRecordedAt.ShouldBe(ControlledInterimReadiness.ReadySnapshot.MonitorRecordedAt, row);

            await using var scope = world.Services.CreateAsyncScope();
            var detail = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetAsync(created.Id, CancellationToken.None);
            var profile = detail.Verification.ShouldNotBeNull(row);
            profile.Round.ShouldBe(VerificationRound.Interim, row);
            profile.BaselineOutcomeId.ShouldBe(baseline.Id, row);
            profile.Selection!.ArtifactCommitSha.ShouldBe(world.SelectionSha, row);
            profile.FinalReviewPending.ShouldBeTrue(row);
            profile.OwnerRequiresFinalReview.ShouldBeFalse(row + ": the latch lives on the owner, not the round");
        }
    }

    [Test]
    public async Task C544_LatchSurvives()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var review = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        var code = await world.CreateTaskAsync(world.InterimCode(baseline.Id));
        (await world.TaskAsync(world.Owner.Id)).RequiresFinalVerificationReview.ShouldBeTrue("admitted");

        await using (var scope = world.Services.CreateAsyncScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
            await tasks.CancelAsync(review.Id, CancellationToken.None);
            await tasks.CancelAsync(code.Id, CancellationToken.None);
        }
        (await world.TaskAsync(world.Owner.Id)).RequiresFinalVerificationReview.ShouldBeTrue("after-cancel");
        await world.SetCardPolicyAsync(CardVerificationPolicy.FullOnly, CardVerificationPolicy.FullOnly);
        (await world.TaskAsync(world.Owner.Id)).RequiresFinalVerificationReview.ShouldBeTrue("after-policy-disable");
        await world.RestartAsync();
        (await world.TaskAsync(world.Owner.Id)).RequiresFinalVerificationReview.ShouldBeTrue("after-restart");

        // A settled Final review of the owner does not clear the latch either; only its evidence can approve.
        await world.SetCardPolicyAsync(CardVerificationPolicy.FullOnly, CardVerificationPolicy.FullOnly);
        await world.SettleReviewAsync();
        (await world.TaskAsync(world.Owner.Id)).RequiresFinalVerificationReview.ShouldBeTrue("after-final-settlement");

        await using var db = world.CreateContext();
        var land = C544Land.Create(db, world.Clock);
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(world.Owner.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: world.OwnerSha), CancellationToken.None), "no-evidence-land");
        error.Code.ShouldBe(LandApproval.FinalReviewRequiredCode, "no-evidence-land");
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == world.Owner.Id)).ShouldBe(0, "no-evidence-land: no request");
    }

    [Test]
    public async Task C544_RunningHealthLoss()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var running = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        var sessionId = await world.DispatchAsync(running.Id);
        var taskCount = await world.TaskCountAsync();

        world.Readiness.Verdict = InterimReadiness.Unready("monitor_stale");
        await TickAsync(world);
        (await world.TaskAsync(running.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "running: not held or killed");
        await using (var db = world.CreateContext())
            (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).Status.ShouldBe(SessionStatus.Running, "running: session untouched");

        await world.SeedTurnAsync(sessionId, running.Id,
            C544World.ReviewReport(running.Id, world.Owner.Id, world.OwnerSha, "Interim", found: false, next: "review"));
        await world.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(sessionId, CancellationToken.None);
        (await world.TaskAsync(running.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded, "running command finished normally");
        (await world.TaskCountAsync()).ShouldBe(taskCount, "replacement count == 0");
        await using (var db = world.CreateContext())
            (await db.AgentTasks.AsNoTracking().CountAsync(t => t.VerificationRound == VerificationRound.Final && t.CreatedAt > DateTime.UtcNow.AddMinutes(-1) && t.Id != baseline.StageTaskId))
                .ShouldBe(0, "no automatic Final replacement");

        await ExpectRefusedAsync(world, world.InterimReview(baseline.Id), InterimVerificationPolicy.BackstopUnreadyCode, "next-interim-refused");
    }

    [Test]
    public async Task C544_FreshFollowUp()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var predecessor = await world.CreateTaskAsync(world.InterimReview(baseline.Id));
        await UpdateTaskAsync(world, predecessor.Id, t => { t.Status = AgentTaskStatus.Succeeded; t.CompletedAt = DateTime.UtcNow; });

        var followUp = await world.CreateTaskAsync(new CreateAgentTaskRequest("Follow up the interim review.",
            Title: "follow-up", Role: AgentTaskRole.Review, Workspace: WorkspaceMode.ReadOnly, WorkingDirectory: world.Repo.Path,
            FollowUpOnTask: predecessor.Id.ToString("D")));
        var fresh = await world.TaskAsync(followUp.Id);
        fresh.FollowUpOfTaskId.ShouldBe(predecessor.Id, "fresh-follow-up");
        fresh.VerificationRound.ShouldBe(VerificationRound.Final, "fresh-follow-up: omitted round is Final");
        fresh.VerificationSubjectTaskId.ShouldBeNull("fresh-follow-up");
        fresh.VerificationBaselineOutcomeId.ShouldBeNull("fresh-follow-up");
        fresh.VerificationAdmissionJson.ShouldBeNull("fresh-follow-up");
        (await world.TaskAsync(predecessor.Id)).VerificationRound.ShouldBe(VerificationRound.Interim, "predecessor stays Interim");
    }

    private static async Task TickAsync(C544World world)
    {
        await using var scope = world.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
    }

    private static async Task AssertHeldAsync(C544World world, Guid taskId, string code, string row)
    {
        var held = await world.TaskAsync(taskId);
        held.Status.ShouldBe(AgentTaskStatus.Blocked, row);
        held.FailureReason.ShouldNotBeNull(row).ShouldStartWith(code, Case.Sensitive, row);
        held.AgentSessionId.ShouldBeNull(row + ": launch count == 0");
        held.DispatchedAt.ShouldBeNull(row + ": launch count == 0");
        held.VerificationRound.ShouldBe(VerificationRound.Interim, row + ": never relaunched as another round");
        await using var db = world.CreateContext();
        (await db.AgentTaskEvents.AsNoTracking().CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked))
            .ShouldBe(1, row + ": one visible hold event");
        await using var scope = world.Services.CreateAsyncScope();
        var detail = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetAsync(taskId, CancellationToken.None);
        detail.Verification!.HoldReason.ShouldNotBeNull(row).ShouldStartWith(code, Case.Sensitive, row + ": status shows the refusal");
    }
}
