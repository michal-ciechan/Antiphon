using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReviewEvidenceConsistencyTests
{
    private static readonly string A = new('a', 40);
    private static readonly string B = new('b', 40);
    private static readonly string C = new('c', 40);
    private static readonly string D = new('d', 40);
    private static readonly string E = new('e', 40);
    private static readonly string F = new('f', 40);

    [Test]
    [Arguments("remote")]
    [Arguments("primary")]
    public async Task C788_SubjectTipMismatchWarnsAndBinds(string source)
    {
        await using var world = await C544World.CreateAsync();
        var evidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.ProgressObserved,
            Sources: source == "primary"
                ? [new CompletionProgressSource(ProgressOrigin.Primary, CompletionProgressAssessment.ProgressObserved,
                    VerifiedSha: A)] : null,
            RemoteSync: source == "remote"
                ? new RemoteSyncEvidence(1, RemoteSettlementSyncState.Synchronized,
                    "refs/heads/" + world.Owner.WorktreeBranch, ConfirmedSha: A) : null);
        var row = await SettleAsync(world, B, B, evidence);
        row.Outcome.SubjectTaskId.ShouldBe(world.Owner.Id);
        row.Outcome.ReviewedSourceSha.ShouldBe(B);
        row.Outcome.ReviewedSourceRef.ShouldBe("refs/heads/" + world.Owner.WorktreeBranch);
        row.Outcome.ReviewedRepositoryPath.ShouldBe(world.RepositoryPath);
        row.Header.ShouldContain("review-evidence-warning=review_evidence_subject_tip_mismatch");
        row.Header.ShouldContain(A);
        row.Header.ShouldContain(B);
        row.Header.ShouldContain("as confirmed at its settlement");
        row.Header.ShouldNotContain("review_evidence_sha_not_review_base");
        row.Warnings.Count(w => w.Contains("review_evidence_subject_tip_mismatch", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Test]
    [Arguments(40)]
    [Arguments(64)]
    public async Task C788_ReviewBaseMismatchWarnsAndBinds(int length)
    {
        await using var world = await C544World.CreateAsync();
        var a = new string('a', length);
        var b = new string('b', length);
        var evidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.ProgressObserved,
            RemoteSync: new RemoteSyncEvidence(1, RemoteSettlementSyncState.Synchronized,
                "refs/heads/" + world.Owner.WorktreeBranch, ConfirmedSha: b));
        var row = await SettleAsync(world, a, b, evidence);
        row.Outcome.ReviewedSourceSha.ShouldBe(b);
        row.Header.ShouldContain("review-evidence-warning=review_evidence_sha_not_review_base");
        row.Header.ShouldContain(a);
        row.Header.ShouldContain(b);
        row.Header.ShouldNotContain("review_evidence_subject_tip_mismatch");
        row.Warnings.Count(w => w.Contains("review_evidence_sha_not_review_base", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Test]
    [Arguments("equal")]
    [Arguments("null-base")]
    [Arguments("short-base")]
    [Arguments("absent-json")]
    [Arguments("malformed-json")]
    [Arguments("missing-tips")]
    [Arguments("invalid-tips")]
    [Arguments("equal-64")]
    public async Task C788_ConsistentOrMissingFactsStaySilent(string shape)
    {
        await using var world = await C544World.CreateAsync();
        var claim = shape == "equal-64" ? new string('b', 64) : B;
        var baseSha = shape switch
        {
            "null-base" => null,
            "short-base" => "deadbee",
            _ => claim,
        };
        string? raw = shape switch
        {
            "absent-json" => null,
            "malformed-json" => "{broken",
            "missing-tips" => TaskProgressJson.SerializeEvidence(new(1, CompletionProgressAssessment.ProgressObserved)),
            "invalid-tips" => TaskProgressJson.SerializeEvidence(new(1, CompletionProgressAssessment.ProgressObserved,
                RemoteSync: new(1, RemoteSettlementSyncState.Synchronized, ConfirmedSha: "deadbee"))),
            _ => TaskProgressJson.SerializeEvidence(new(1, CompletionProgressAssessment.ProgressObserved,
                RemoteSync: new(1, RemoteSettlementSyncState.Synchronized, ConfirmedSha: claim))),
        };
        var row = await SettleAsync(world, baseSha, claim, raw: raw, seedRaw: true);
        row.Outcome.ReviewedSourceSha.ShouldBe(claim);
        row.Header.ShouldNotContain("review_evidence_sha_not_review_base");
        row.Header.ShouldNotContain("review_evidence_subject_tip_mismatch");
        row.Warnings.ShouldNotContain(w => w.Contains("review_evidence_", StringComparison.Ordinal));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C788_RemoteConfirmedTipPrecedesPrimary(bool remoteMatches)
    {
        await using var world = await C544World.CreateAsync();
        var evidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.ProgressObserved,
            ClaimedSha: D,
            Sources: [new CompletionProgressSource(ProgressOrigin.Primary, CompletionProgressAssessment.ProgressObserved,
                    VerifiedSha: remoteMatches ? A : B),
                new CompletionProgressSource(ProgressOrigin.PrimaryAlternate,
                    CompletionProgressAssessment.ProgressObserved, VerifiedSha: F)],
            RemoteSync: new RemoteSyncEvidence(1, RemoteSettlementSyncState.Synchronized,
                "refs/heads/" + world.Owner.WorktreeBranch,
                ObservedSha: E, ConfirmedSha: remoteMatches ? B : A));
        var row = await SettleAsync(world, B, B, evidence);
        row.Outcome.ReviewedSourceSha.ShouldBe(B);
        if (remoteMatches)
        {
            row.Header.ShouldNotContain("review_evidence_subject_tip_mismatch");
            row.Warnings.ShouldNotContain(w => w.Contains("review_evidence_subject_tip_mismatch", StringComparison.Ordinal));
            row.Header.ShouldNotContain(A);
        }
        else
        {
            row.Header.ShouldContain("review_evidence_subject_tip_mismatch");
            row.Header.ShouldContain(A);
        }
        row.Header.ShouldNotContain(D);
        row.Header.ShouldNotContain(E);
        row.Header.ShouldNotContain(F);

        foreach (var remote in new[] { "absent", "non-full" })
        {
            await using var fallbackWorld = await C544World.CreateAsync();
            var fallbackEvidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.ProgressObserved,
                Sources: [new CompletionProgressSource(ProgressOrigin.Primary,
                    CompletionProgressAssessment.ProgressObserved, VerifiedSha: remoteMatches ? A : B)],
                RemoteSync: remote == "absent" ? null : new RemoteSyncEvidence(1,
                    RemoteSettlementSyncState.Synchronized, ConfirmedSha: "deadbee"));
            var fallback = await SettleAsync(fallbackWorld, B, B, fallbackEvidence);
            fallback.Outcome.ReviewedSourceSha.ShouldBe(B, remote);
            if (remoteMatches)
                fallback.Header.ShouldContain("review_evidence_subject_tip_mismatch", Case.Sensitive, remote);
            else
                fallback.Header.ShouldNotContain("review_evidence_subject_tip_mismatch", Case.Sensitive, remote);
        }
    }

    [Test]
    public async Task C788_RepeatedSettlementKeepsOneWarningPerCode()
    {
        await using var world = await C544World.CreateAsync();
        var evidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.ProgressObserved,
            RemoteSync: new RemoteSyncEvidence(1, RemoteSettlementSyncState.Synchronized,
                "refs/heads/" + world.Owner.WorktreeBranch, ConfirmedSha: C));
        var row = await SettleAsync(world, A, B, evidence);
        row.Header.ShouldContain("review_evidence_sha_not_review_base");
        row.Header.ShouldContain("review_evidence_subject_tip_mismatch");
        var originalOutcome = row.Outcome.ShouldNotBeNull();
        await using (var before = world.CreateContext())
        {
            var originalTask = await before.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == row.Id);
            originalTask.Status.ShouldBe(AgentTaskStatus.Succeeded);
            originalOutcome.Outcome.ShouldBe(StageOutcomeKind.Clean);
            originalOutcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full);
            originalTask.NextStage.ShouldBe(PipelineHandoffKind.Land);
            originalTask.NextHandoff.ShouldBe("C544 fixture handoff.");
        }
        await world.Services.GetRequiredService<AgentTaskReplyService>()
            .OnTurnEndAsync(row.Session, CancellationToken.None);
        await using var db = world.CreateContext();
        var settledTask = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == row.Id);
        settledTask.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settledTask.NextStage.ShouldBe(PipelineHandoffKind.Land);
        settledTask.NextHandoff.ShouldBe("C544 fixture handoff.");
        var settledOutcome = await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.StageTaskId == row.Id);
        settledOutcome.Id.ShouldBe(originalOutcome.Id);
        settledOutcome.Outcome.ShouldBe(StageOutcomeKind.Clean);
        settledOutcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full);
        (await db.StageOutcomes.CountAsync(o => o.StageTaskId == row.Id)).ShouldBe(1);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == row.Id
            && n.Kind == LandNotificationKind.TaskCompletion)).ShouldBe(1);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == row.Id && e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains("review_evidence_sha_not_review_base"))).ShouldBe(1);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == row.Id && e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains("review_evidence_subject_tip_mismatch"))).ShouldBe(1);
    }

    [Test]
    public async Task C788_BothWarningsCoexistWithOtherHeaderWarnings()
    {
        await using var world = await C544World.CreateAsync(delegation: d => d.ReplyInlineMaxChars = 200);
        await File.AppendAllTextAsync(Path.Combine(world.RepositoryPath, "README.md"), "dirty review\n");
        var evidence = new CompletionProgressEvidence(1, CompletionProgressAssessment.ProgressObserved,
            RemoteSync: new RemoteSyncEvidence(1, RemoteSettlementSyncState.Synchronized,
                "refs/heads/" + world.Owner.WorktreeBranch, ConfirmedSha: C));
        var row = await SettleAsync(world, A, B, evidence, mentionDirtyFile: true);
        row.Outcome.ReviewedSourceSha.ShouldBe(B);
        row.Header.ShouldContain("review_evidence_sha_not_review_base");
        row.Header.ShouldContain("review_evidence_subject_tip_mismatch");
        row.Header.ShouldContain("uncommitted", Case.Insensitive);
        row.Warnings.Count(w => w.Contains("review_evidence_", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C788_ConsistencyWarningReceipt(bool busy)
    {
        foreach (var distilledSpill in new[] { false, true })
        {
            var row = $"busy={busy} distilledSpill={distilledSpill}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy, spill: distilledSpill,
                distill: distilledSpill);
            var (taskId, _) = await rig.SettleReviewAsync(reviewedSha: B,
                beforeSettlement: SeedBothMismatchesAsync);
            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row);
            var header = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson)!.NoteHeader;
            header.ShouldContain("review_evidence_sha_not_review_base");
            header.ShouldContain("review_evidence_subject_tip_mismatch");
            rig.Caller.SubmittedBodies.ShouldBeEmpty();
            if (distilledSpill)
            {
                await rig.FlushAsync();
                rig.DistillQueue.TryDequeue(out var request).ShouldBeTrue(row);
                (await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest,
                    C544DeliveryRig.Summary, CancellationToken.None)).ShouldBeNull(row);
            }
            await rig.DeliverAsync(row);
            await rig.ScanAsync();
            await VerificationRoundDeliveryTests.AssertReceivedOnceAsync(rig, taskId, row,
                ["review_evidence_sha_not_review_base", "review_evidence_subject_tip_mismatch", A, B, C],
                expectKind: distilledSpill ? "distilled" : "raw", expectSpill: distilledSpill);
        }
    }

    private static async Task SeedBothMismatchesAsync(C544World world, Guid reviewId)
    {
        await using var db = world.CreateContext();
        var review = await db.AgentTasks.SingleAsync(t => t.Id == reviewId);
        review.WorktreeBaseSha = A;
        var subject = await db.AgentTasks.SingleAsync(t => t.Id == world.Owner.Id);
        subject.CompletionProgressEvidenceJson = TaskProgressJson.SerializeEvidence(new(
            1, CompletionProgressAssessment.ProgressObserved,
            RemoteSync: new RemoteSyncEvidence(1, RemoteSettlementSyncState.Synchronized,
                "refs/heads/" + world.Owner.WorktreeBranch, ConfirmedSha: C)));
        await db.SaveChangesAsync();
    }

    [Test]
    [Arguments("obligation-insert")]
    [Arguments("settled-committed")]
    [Arguments("note-insert")]
    [Arguments("note-committed")]
    [Arguments("wakeup-dropped")]
    [Arguments("render-committed")]
    [Arguments("spill-written")]
    [Arguments("attempt-committed")]
    [Arguments("prompt-accepted")]
    public async Task C788_ConsistencyWarningRecovery(string cut)
    {
        foreach (var busy in new[] { false, true })
        foreach (var distilledSpill in new[] { false, true })
        {
            var row = $"{cut} busy={busy} distilledSpill={distilledSpill}";
            await using var rig = await C544DeliveryRig.CreateAsync(busy, spill: distilledSpill,
                distill: distilledSpill);
            if (cut == "wakeup-dropped") rig.Boundary.DropCompletionWakeup = true;
            else if (cut is not ("render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted"))
                rig.Fault.Cut = cut;
            var (taskId, _) = await rig.SettleReviewAsync(reviewedSha: B,
                beforeSettlement: SeedBothMismatchesAsync);
            if (cut == "obligation-insert")
            {
                rig.Fault.Throws.ShouldBe(1, row + ": cut reached");
                (await rig.NotificationAsync(taskId)).ShouldBeNull(row);
                await rig.RestartAsync();
                var session = (await rig.World.TaskAsync(taskId)).AgentSessionId!.Value;
                await rig.World.Services.GetRequiredService<AgentTaskReplyService>()
                    .OnTurnEndAsync(session, CancellationToken.None);
            }
            var note = (await rig.NotificationAsync(taskId)).ShouldNotBeNull(row);
            if (distilledSpill && cut is not ("settled-committed" or "note-insert"))
            {
                if (rig.DistillQueue.TryDequeue(out var request))
                    (await rig.Queue.TryApplyDistillationAsync(request, note.ContentDigest,
                        C544DeliveryRig.Summary, CancellationToken.None)).ShouldBeNull(row);
            }
            if (cut is "render-committed" or "spill-written" or "attempt-committed" or "prompt-accepted")
            {
                rig.Fault.TaskId = taskId;
                rig.Fault.Cut = cut;
                try
                {
                    if (busy) await rig.EndCallerTurnAsync();
                    else await rig.FlushAsync();
                    if (cut == "prompt-accepted") await rig.ScanAsync();
                }
                catch (IOException e) when (e.Message.StartsWith("c544 completion persistence cut", StringComparison.Ordinal)) { }
                rig.Fault.Throws.ShouldBe(1, row + ": cut reached");
            }
            await rig.RestartAsync();
            rig.World.Clock.Advance(TimeSpan.FromMinutes(10));
            await rig.ScanAsync();
            if (rig.Busy) await rig.EndCallerTurnAsync();
            await rig.FlushAsync();
            await rig.ScanAsync();
            await VerificationRoundDeliveryTests.AssertReceivedOnceAsync(rig, taskId, row,
                ["review_evidence_sha_not_review_base", "review_evidence_subject_tip_mismatch", A, B, C],
                expectKind: null, expectSpill: distilledSpill ? null : false);
            (await rig.NotificationAsync(taskId))!.Id.ShouldBe(note.Id);
        }
    }

    [Test]
    [Arguments("failed")]
    [Arguments("blocked")]
    [Arguments("foreign-card")]
    [Arguments("unusable-block")]
    public async Task C788_UnauthorizedOrUnusableEvidenceHasNoConsistencyWarning(string shape)
    {
        await using var world = await C544World.CreateAsync();
        var progress = new CompletionProgressEvidence(1, CompletionProgressAssessment.ProgressObserved,
            RemoteSync: new RemoteSyncEvidence(1, RemoteSettlementSyncState.Synchronized,
                "refs/heads/" + world.Owner.WorktreeBranch, ConfirmedSha: C));
        var row = await SettleAsync(world, A, B, progress, shape: shape);
        row.Outcome?.ReviewedSourceSha.ShouldBeNull();
        row.Header.ShouldNotContain("review_evidence_sha_not_review_base");
        row.Header.ShouldNotContain("review_evidence_subject_tip_mismatch");
        row.Warnings.ShouldNotContain(w => w.Contains("review_evidence_sha_not_review_base", StringComparison.Ordinal)
            || w.Contains("review_evidence_subject_tip_mismatch", StringComparison.Ordinal));
    }

    private static async Task<(Guid Id, Guid Session, StageOutcome? Outcome, string Header, string[] Warnings)> SettleAsync(
        C544World world, string? reviewBase, string claim, CompletionProgressEvidence? progress = null,
        string? raw = null, bool seedRaw = false, bool mentionDirtyFile = false,
        string? shape = null)
    {
        var created = await world.CreateTaskAsync(world.FinalReview());
        var session = await world.DispatchAsync(created.Id);
        await using (var db = world.CreateContext())
        {
            var review = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
            review.WorktreeBaseSha = reviewBase;
            var subject = await db.AgentTasks.SingleAsync(t => t.Id == world.Owner.Id);
            if (shape == "foreign-card") subject.CardId = null;
            if (progress is not null) subject.CompletionProgressEvidenceJson = TaskProgressJson.SerializeEvidence(progress);
            else if (seedRaw) subject.CompletionProgressEvidenceJson = raw;
            await db.SaveChangesAsync();
        }
        var report = C544World.ReviewReport(created.Id, world.Owner.Id, claim, "Full", false, "land",
            reviewedSourceClean: true);
        if (mentionDirtyFile)
            report = report.Replace("Reviewed the owner.", "Reviewed `README.md` in the owner checkout.",
                StringComparison.Ordinal);
        if (shape == "unusable-block")
            report = report.Replace(ReviewEvidence.Heading, "Invalid evidence", StringComparison.Ordinal);
        if (shape is "failed" or "blocked")
            report += "\n" + DelegationReportFormatter.ReportToken(created.Id, shape);
        await world.SeedTurnAsync(session, created.Id, report);
        await world.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
        await using var settled = world.CreateContext();
        var outcome = await settled.StageOutcomes.AsNoTracking().SingleOrDefaultAsync(o => o.StageTaskId == created.Id);
        var note = await settled.AgentTaskLandNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.TaskId == created.Id
            && n.Kind == LandNotificationKind.TaskCompletion);
        var header = note is null ? "" : TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson)!.NoteHeader;
        var warnings = await settled.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == created.Id
            && e.Type == AgentTaskEventType.Warning).Select(e => e.Detail).ToArrayAsync();
        return (created.Id, session, outcome, header, warnings);
    }
}
