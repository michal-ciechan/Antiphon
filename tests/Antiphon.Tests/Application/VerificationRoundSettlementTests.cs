using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.InterimVerificationPolicyTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-5/V-13, R-4/R-9. Parser-to-durable settlement: a real marked AssistantText + TurnEnd
/// settled by <see cref="AgentTaskReplyService.OnTurnEndAsync"/> on an owned database, observed
/// through a fresh context. Covers scope capping, subject binding, atomic outcome and Completion
/// obligation commit, exact settlement-event identity, and obligation applicability.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class VerificationRoundSettlementTests
{
    [Test]
    public async Task C544_ProfileCapsScope()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var rows = new (string Row, CreateAgentTaskRequest Request, string? Declared, VerificationScope Expected)[]
        {
            ("interim-claims-full", world.InterimReview(baseline.Id), "Full", VerificationScope.Interim),
            ("interim-declares-interim", world.InterimReview(baseline.Id), "Interim", VerificationScope.Interim),
            ("final-claims-full", world.FinalReview(), "Full", VerificationScope.Full),
            ("final-declares-interim", world.FinalReview(), "Interim", VerificationScope.Interim),
            ("final-declares-none", world.FinalReview(), "None", VerificationScope.None),
            ("final-missing-scope", world.FinalReview(), null, VerificationScope.Unknown),
        };
        foreach (var (row, request, declared, expected) in rows)
        {
            var outcome = await world.SettleReviewAsync(request, scope: declared!);
            outcome.OrdinaryScopeCompleted.ShouldBe(expected, row);
            outcome.CommissionedRound.ShouldBe(request.VerificationRound ?? VerificationRound.Final, row);
            outcome.VerificationProfileVersion.ShouldBe(1, row);
            if (request.VerificationRound == VerificationRound.Interim)
                outcome.OrdinaryScopeCompleted.ShouldNotBe(VerificationScope.Full, row + ": Interim never mints Full");
        }

        // A failed (unreported-verdict) turn does not certify Full.
        var failed = await world.CreateTaskAsync(world.FinalReview());
        var session = await world.DispatchAsync(failed.Id);
        var failedReport = C544World.ReviewReport(failed.Id, world.Owner.Id, world.OwnerSha, "Full", found: false)
            + "\n" + DelegationReportFormatter.ReportToken(failed.Id, "failed");
        await world.SeedTurnAsync(session, failed.Id, failedReport);
        await world.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
        await using var db = world.CreateContext();
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == failed.Id)).Status.ShouldBe(AgentTaskStatus.Failed, "failed-turn");
        var failedOutcome = await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.StageTaskId == failed.Id);
        failedOutcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Unknown, "failed-turn: no Full");
        failedOutcome.ReviewedSourceSha.ShouldBeNull("failed-turn: no bound evidence");
    }

    [Test]
    public async Task C544_FoundFullSubject()
    {
        await using var world = await C544World.CreateAsync();
        var found = await world.SettleReviewAsync(found: true, next: "code");
        found.Outcome.ShouldBe(StageOutcomeKind.Found, "found-full");
        found.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full, "found-full");
        found.SubjectTaskId.ShouldBe(world.Owner.Id, "found-full: original owner");
        found.ReviewedSourceSha.ShouldBe(world.OwnerSha, "found-full: exact SHA");
        found.ReviewedSourceRef.ShouldBe("refs/heads/" + world.Owner.WorktreeBranch, "found-full: ref");
        found.ReviewedRepositoryPath.ShouldBe(world.Repo.Path, "found-full: repository");

        // Eligible as a baseline...
        var admitted = await world.CreateTaskAsync(world.InterimReview(found.Id));
        (await world.TaskAsync(admitted.Id)).VerificationBaselineOutcomeId.ShouldBe(found.Id, "found-full baseline admits");

        // ...but never approval.
        await using var db = world.CreateContext();
        var land = C544Land.Create(db, world.Clock);
        var error = await Should.ThrowAsync<ConflictException>(() => land.RequestAsync(world.Owner.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: world.OwnerSha, ReviewEvidenceId: found.Id), CancellationToken.None));
        error.Code.ShouldBe("review_evidence_ineligible", "found-full is not approval");
        (await db.AgentTaskLandRequests.CountAsync()).ShouldBe(0, "found-full: no request");
    }

    [Test]
    public async Task C544_SettlementSubject()
    {
        await using var world = await C544World.CreateAsync();
        var foreignCard = Guid.NewGuid();
        await using (var db = world.CreateContext())
        {
            var column = await db.BoardColumns.FirstAsync(c => c.BoardId == world.Board.Id);
            db.Cards.Add(new Card
            {
                Id = foreignCard, BoardId = world.Board.Id, BoardColumnId = column.Id, Identifier = "CARD-0998",
                Title = "foreign", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var foreignOwner = await SeedOwnerAsync(world, foreignCard, world.Project.Id);
        var sharedCode = await SeedOwnerAsync(world, world.Card.Id, world.Project.Id);
        await UpdateTaskAsync(world, sharedCode.Id, t => t.Workspace = WorkspaceMode.Shared);
        foreach (var (row, subject) in new[] { ("foreign-card-owner", foreignOwner.Id), ("shared-not-worktree", sharedCode.Id), ("missing-task", Guid.NewGuid()) })
        {
            var outcome = await world.SettleReviewAsync(subject: subject);
            outcome.ReviewedSourceSha.ShouldBeNull(row + ": no bound evidence");
            outcome.ReviewedSourceRef.ShouldBeNull(row);
            outcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Unknown, row + ": unusable scope");
            await ExpectRefusedAsync(world, world.InterimReview(outcome.Id), InterimVerificationPolicy.BaselineInvalidCode, row + ": not a baseline");
        }
    }

    [Test]
    public async Task C544_SettlementAtomic()
    {
        foreach (var cut in new[] { "before-save", "before-commit", "after-commit" })
        {
            var fault = new SettlementFault(cut);
            await using var world = await C544World.CreateAsync(fault);
            var created = await world.CreateTaskAsync(world.FinalReview());
            var session = await world.DispatchAsync(created.Id);
            fault.TaskId = created.Id;
            await world.SeedTurnAsync(session, created.Id,
                C544World.ReviewReport(created.Id, world.Owner.Id, world.OwnerSha, "Full", found: false));
            var replies = world.Services.GetRequiredService<AgentTaskReplyService>();
            await replies.OnTurnEndAsync(session, CancellationToken.None);
            fault.Throws.ShouldBe(1, cut);

            await using (var fresh = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString)))
            {
                var outcomes = await fresh.StageOutcomes.AsNoTracking().CountAsync(o => o.StageTaskId == created.Id);
                var status = (await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).Status;
                if (cut == "after-commit")
                {
                    outcomes.ShouldBe(1, cut + ": committed");
                    status.ShouldBe(AgentTaskStatus.Succeeded, cut);
                }
                else
                {
                    outcomes.ShouldBe(0, cut + ": pre-commit fault leaves zero outcomes");
                    status.ShouldBe(AgentTaskStatus.Dispatched, cut + ": no terminal task escapes");
                    (await fresh.AgentTaskLandNotifications.CountAsync(n => n.TaskId == created.Id)).ShouldBe(0, cut);
                }
            }

            // Re-enter once: exactly one committed outcome with the settled scope.
            await replies.OnTurnEndAsync(session, CancellationToken.None);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
            var settled = await verify.StageOutcomes.AsNoTracking().Where(o => o.StageTaskId == created.Id).ToListAsync();
            settled.Count.ShouldBe(1, cut + ": exactly one outcome after recovery");
            settled[0].OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full, cut);
            (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded, cut);
            (await verify.AgentTaskLandNotifications.CountAsync(n => n.TaskId == created.Id)).ShouldBe(1, cut + ": one obligation");
        }
    }

    [Test]
    public async Task C544_ManualCannotCertify()
    {
        await using var world = await C544World.CreateAsync();
        var full = await world.SettleReviewAsync();
        full.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full, "delegate-full");
        await using var db = world.CreateContext();
        var service = new StageOutcomeService(db);
        foreach (var (row, request) in new[]
                 {
                     ("manual-clean-with-sha", new RecordStageFindingRequest("Review", Found: false, Detail: "manual clean", ReviewedSourceSha: world.OwnerSha)),
                     ("manual-found", new RecordStageFindingRequest("Review", Found: true, Detail: "manual found")),
                     ("manual-clean", new RecordStageFindingRequest("Review", Found: false, Detail: "manual clean again")),
                 })
        {
            var written = await service.RecordFindingAsync(full.StageTaskId!.Value, request, CancellationToken.None);
            written.Source.ShouldBe(StageOutcomeSource.Orchestrator, row);
            written.OrdinaryScopeCompleted.ShouldBeNull(row + ": manual rows never copy Full");
            written.CommissionedRound.ShouldBeNull(row);
            written.VerificationProfileVersion.ShouldBeNull(row);
            var stored = await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == written.Id);
            stored.OrdinaryScopeCompleted.ShouldBeNull(row + " stored");
        }
        (await db.StageOutcomes.AsNoTracking().CountAsync(o => o.OrdinaryScopeCompleted == VerificationScope.Full))
            .ShouldBe(1, "only the delegate settlement carries Full");
    }

    [Test]
    public async Task C544_InterimRouting()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync();
        var rows = new (string Row, CreateAgentTaskRequest Request, bool Found, string Declared, string ReportNext,
            PipelineHandoffKind ExpectedStage, string HeaderNext, string ObligationBit)[]
        {
            ("interim-clean", world.InterimReview(baseline.Id), false, "Interim", "land", PipelineHandoffKind.Review, "next=review", "final-review=pending"),
            ("interim-found", world.InterimReview(baseline.Id), true, "Interim", "code", PipelineHandoffKind.Code, "next=code", "final-review=pending"),
            ("final-clean", world.FinalReview(), false, "Full", "land", PipelineHandoffKind.Land, "next=land", "final-review=none"),
        };
        foreach (var (row, request, found, declared, reportNext, expectedStage, headerNext, obligation) in rows)
        {
            var outcome = await world.SettleReviewAsync(request, scope: declared, found: found, next: reportNext);
            var task = await world.TaskAsync(outcome.StageTaskId!.Value);
            task.NextStage.ShouldBe(expectedStage, row);
            await using var db = world.CreateContext();
            var note = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == task.Id);
            var snapshot = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson).ShouldNotBeNull(row);
            snapshot.NoteHeader.ShouldContain(headerNext, Case.Sensitive, row);
            snapshot.NoteHeader.ShouldContain(obligation, Case.Sensitive, row);
            snapshot.NoteHeader.ShouldContain($"verification={task.VerificationRound}", Case.Sensitive, row);
            note.Body.ShouldStartWith(snapshot.NoteHeader, Case.Sensitive, row);
            if (row == "interim-clean")
                snapshot.NoteHeader.ShouldNotContain("next=land", Case.Sensitive, row + ": never land-ready");
        }
    }

    [Test]
    public async Task C544_CompletionObligationAtomic()
    {
        var fault = new ObligationFault();
        await using var world = await C544World.CreateAsync(fault);
        var created = await world.CreateTaskAsync(world.FinalReview());
        var session = await world.DispatchAsync(created.Id);
        await world.SeedTurnAsync(session, created.Id, C544World.ReviewReport(created.Id, world.Owner.Id, world.OwnerSha, "Full", found: false));
        fault.TaskId = created.Id;
        var replies = world.Services.GetRequiredService<AgentTaskReplyService>();
        await replies.OnTurnEndAsync(session, CancellationToken.None);
        fault.Throws.ShouldBe(1, "obligation-insert");
        fault.PairObserved.ShouldBeTrue("obligation-insert: task, event, outcome and obligation in one save");
        await using (var fresh = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString)))
        {
            (await fresh.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "obligation-insert: task not terminal");
            (await fresh.StageOutcomes.CountAsync(o => o.StageTaskId == created.Id)).ShouldBe(0, "obligation-insert: StageOutcome count == 0");
            (await fresh.AgentTaskLandNotifications.CountAsync(n => n.TaskId == created.Id)).ShouldBe(0, "obligation-insert: notification count == 0");
            (await fresh.AgentTaskEvents.CountAsync(e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Completed)).ShouldBe(0, "obligation-insert: no event");
        }
        await replies.OnTurnEndAsync(session, CancellationToken.None);
        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString));
        (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded, "recovered");
        (await verify.StageOutcomes.CountAsync(o => o.StageTaskId == created.Id)).ShouldBe(1, "recovered");
        (await verify.AgentTaskLandNotifications.CountAsync(n => n.TaskId == created.Id && n.Kind == LandNotificationKind.Completion)).ShouldBe(1, "recovered");
    }

    [Test]
    public async Task C544_CompletionEventIdentity()
    {
        await using var world = await C544World.CreateAsync();

        // Row 1: merge-back appends its own Completed event in the same settlement.
        var created = await world.CreateTaskAsync(world.FinalReview() with { Workspace = WorkspaceMode.Worktree });
        await using (var scope = world.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
            task.MergeTargetRef = null;
            await scope.ServiceProvider.GetRequiredService<DelegationWorktreeService>().CreateForTaskAsync(task, CancellationToken.None);
            await db.SaveChangesAsync();
        }
        var session = await world.DispatchAsync(created.Id);
        await world.SeedTurnAsync(session, created.Id, C544World.ReviewReport(created.Id, world.Owner.Id, world.OwnerSha, "Full", found: false));
        await world.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
        await AssertRetainedEventAsync(world, created.Id, "merge-back-appended");

        // Row 2: a prior attempt's Completed event carries a LATER timestamp than this settlement.
        var second = await world.CreateTaskAsync(world.FinalReview());
        await using (var db = world.CreateContext())
        {
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = second.Id, Type = AgentTaskEventType.Completed,
                Detail = "prior attempt completed (clock skew)", At = DateTime.UtcNow.AddHours(1),
            });
            await db.SaveChangesAsync();
        }
        var secondSession = await world.DispatchAsync(second.Id);
        await world.SeedTurnAsync(secondSession, second.Id, C544World.ReviewReport(second.Id, world.Owner.Id, world.OwnerSha, "Full", found: false));
        await world.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(secondSession, CancellationToken.None);
        await AssertRetainedEventAsync(world, second.Id, "later-prior-event");
    }

    private static async Task AssertRetainedEventAsync(C544World world, Guid taskId, string row)
    {
        await using var db = world.CreateContext();
        var note = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.TaskId == taskId);
        var completed = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Completed).ToListAsync();
        completed.Count.ShouldBeGreaterThanOrEqualTo(2, row + ": fixture has a competing Completed event");
        var retained = completed.Single(e => e.Detail.StartsWith("Delegate reported ", StringComparison.Ordinal));
        note.SourceEventId.ShouldBe(retained.Id, row);
        var snapshot = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson).ShouldNotBeNull(row);
        snapshot.SourceEventId.ShouldBe(retained.Id, row + ": snapshot agrees");
    }

    [Test]
    public async Task C544_CompletionIdentityAcrossContinue()
    {
        await using var world = await C544World.CreateAsync();
        var created = await world.CreateTaskAsync(world.FinalReview());
        var report = C544World.ReviewReport(created.Id, world.Owner.Id, world.OwnerSha, "Full", found: false);
        var session = await world.DispatchAsync(created.Id);
        await world.SeedTurnAsync(session, created.Id, report);
        var replies = world.Services.GetRequiredService<AgentTaskReplyService>();
        await replies.OnTurnEndAsync(session, CancellationToken.None);
        await replies.OnTurnEndAsync(session, CancellationToken.None); // re-entry of the same settlement

        await using (var db = world.CreateContext())
            (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == created.Id)).ShouldBe(1, "re-entered same settlement");

        await using (var scope = world.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(created.Id, CancellationToken.None);
        var nextSession = await world.DispatchAsync(created.Id);
        await world.SeedTurnAsync(nextSession, created.Id, report);
        await replies.OnTurnEndAsync(nextSession, CancellationToken.None);

        await using var verify = world.CreateContext();
        var notes = await verify.AgentTaskLandNotifications.AsNoTracking().Where(n => n.TaskId == created.Id).ToListAsync();
        notes.Count.ShouldBe(2, "continued settlement with identical text mints a second obligation");
        notes.Select(n => n.SourceEventId).Distinct().Count().ShouldBe(2, "distinct settlement events");
        notes.Select(n => n.ContentDigest).Distinct().Count().ShouldBe(1, "identical raw digest is not deduplication authority");
        foreach (var note in notes)
            (await verify.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == note.SourceEventId)).Type
                .ShouldBe(AgentTaskEventType.Completed, "each keyed to its own settlement");
    }

    [Test]
    public async Task C544_ObligationApplicability()
    {
        await using var world = await C544World.CreateAsync();

        var profiled = await world.SettleReviewAsync();
        await using (var db = world.CreateContext())
            (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == profiled.StageTaskId && n.Kind == LandNotificationKind.Completion))
                .ShouldBe(1, "session-profile-v1");

        var noSession = await world.CreateTaskAsync(world.FinalReview(), new AgentTaskService.Caller(null, null, world.Repo.Path, ProjectId: world.Project.Id));
        await SettleExistingAsync(world, noSession.Id, parentSession: null);
        await using (var db = world.CreateContext())
        {
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == noSession.Id)).ReplyTo.ShouldBe(AgentTaskReplyTo.None, "reply-none fixture");
            (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == noSession.Id)).ShouldBe(0, "reply-none");
        }

        var legacy = await world.CreateTaskAsync(world.FinalReview());
        await UpdateTaskAsync(world, legacy.Id, t => { t.VerificationProfileVersion = null; t.VerificationRound = null; });
        await SettleExistingAsync(world, legacy.Id, world.CallerSessionId);
        await using (var db = world.CreateContext())
        {
            (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == legacy.Id)).ShouldBe(0, "legacy");
            var direct = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskId == legacy.Id);
            direct.SourceLandNotificationId.ShouldBeNull("legacy: today's direct note");
            direct.AgentSessionId.ShouldBe(world.CallerSessionId, "legacy: direct note to caller");
            direct.NoteHeader.ShouldNotBeNull("legacy").ShouldNotContain("verification=", Case.Sensitive, "legacy header unchanged");
        }
    }

    private static async Task SettleExistingAsync(C544World world, Guid taskId, Guid? parentSession)
    {
        var sessionId = await world.DispatchAsync(taskId);
        await UpdateTaskAsync(world, taskId, t => { t.ParentSessionId = parentSession; t.ReplyTo = parentSession is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session; });
        await world.SeedTurnAsync(sessionId, taskId, C544World.ReviewReport(taskId, world.Owner.Id, world.OwnerSha, "Full", found: false));
        await world.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(sessionId, CancellationToken.None);
        (await world.TaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Succeeded, "settled " + taskId);
    }

    /// <summary>Cuts the settlement save: before SaveChanges, at the implicit commit, or after the commit acknowledged.</summary>
    private sealed class SettlementFault(string cut) : SaveChangesInterceptor, IDbTransactionInterceptor
    {
        public Guid TaskId { get; set; }
        public int Throws { get; private set; }
        private bool _armedCommit;

        private bool Matches(DbContext context) => Throws == 0 && TaskId != Guid.Empty
            && context.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Id == TaskId && e.State == EntityState.Modified
                && e.Entity.Status == AgentTaskStatus.Succeeded)
            && context.ChangeTracker.Entries<StageOutcome>().Any(e => e.State == EntityState.Added && e.Entity.StageTaskId == TaskId);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Matches(data.Context!))
            {
                if (cut == "before-save") { Throws++; throw new IOException("c544 settlement cut before save"); }
                if (cut == "before-commit") _armedCommit = true;
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (cut == "after-commit" && Throws == 0 && TaskId != Guid.Empty
                && data.Context!.ChangeTracker.Entries<StageOutcome>().Any(e => e.Entity.StageTaskId == TaskId))
            {
                Throws++;
                throw new IOException("c544 settlement cut after commit");
            }
            return ValueTask.FromResult(result);
        }

        public ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (_armedCommit)
            {
                _armedCommit = false;
                Throws++;
                throw new IOException("c544 settlement cut before commit");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ObligationFault : SaveChangesInterceptor
    {
        public Guid TaskId { get; set; }
        public int Throws { get; private set; }
        public bool PairObserved { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            var tracker = data.Context!.ChangeTracker;
            if (Throws == 0 && TaskId != Guid.Empty && tracker.Entries<AgentTaskLandNotification>().Any(e =>
                    e.State == EntityState.Added && e.Entity.TaskId == TaskId && e.Entity.Kind == LandNotificationKind.Completion))
            {
                PairObserved = tracker.Entries<AgentTask>().Any(e => e.Entity.Id == TaskId && e.State == EntityState.Modified
                        && e.Entity.Status == AgentTaskStatus.Succeeded)
                    && tracker.Entries<StageOutcome>().Any(e => e.State == EntityState.Added && e.Entity.StageTaskId == TaskId)
                    && tracker.Entries<AgentTaskEvent>().Any(e => e.State == EntityState.Added && e.Entity.AgentTaskId == TaskId
                        && e.Entity.Type == AgentTaskEventType.Completed);
                Throws++;
                throw new IOException("c544 obligation-insert cut");
            }
            return ValueTask.FromResult(result);
        }
    }
}
