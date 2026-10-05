using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>A retained Succeeded report over real pushed Review and Code refs, with its historical unbound row.</summary>
internal sealed class ReviewRecoveryWorld : IAsyncDisposable
{
    public RunnerSettlementWorld World { get; private init; } = null!;
    public Guid ReviewId => World.TaskId;
    public Guid SubjectId { get; private init; }
    public Guid OldId { get; } = Guid.NewGuid();
    public Guid EventId { get; } = Guid.NewGuid();
    public string Report { get; private set; } = "";
    public string Actor => "operator";
    public ReviewEvidenceRecoveryRequest Request => new(OldId, ReviewEvidenceBindingService.ReportDigest(Report), "Recover retained audit é");
    public AppDbContext Db(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(World.Schema.ConnectionString).AddInterceptors(interceptors).Options);
    public static string Body(Guid review, Guid subject, string sha, string finding = "Clean", string scope = "Full", string clean = "true") =>
        $"Retained final review é.\n[antiphon-finding:{review.ToString("N")[..8]} {finding}] checked\n"
        + $"--- review evidence ---\nsubjectTaskId: {subject:D}\nreviewedSourceSha: {sha}\nreviewedSourceClean: {clean}\nordinaryScopeCompleted: {scope}\n\n"
        + "--- next stage ---\nnext: land\nhandoff: reviewed\n";
    public static async Task<ReviewRecoveryWorld> CreateAsync()
    {
        var world = await RunnerSettlementWorld.CreateAsync(AgentTaskRole.Review, profiled: true);
        try
        {
            var subject = await world.AddReviewSubjectAsync(world.Git.Baseline);
            var w = new ReviewRecoveryWorld { World = world, SubjectId = subject };
            w.Report = Body(w.ReviewId, subject, world.Git.Baseline);
            await using var db = w.Db();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == w.ReviewId);
            task.Status = AgentTaskStatus.Succeeded;
            task.CompletedAt = DateTime.UtcNow;
            task.Result = w.Report;
            task.ReportEvidence = AgentTaskReportEvidence.Marked;
            task.NextStage = "land"; task.NextHandoff = "retained handoff"; task.CostUsd = 2.50m;
            task.CompletionProgressEvidenceJson = TaskProgressJson.SerializeEvidence(new(1,
                CompletionProgressAssessment.NoAttributedProgress, RemoteSync: new(1,
                    RemoteSettlementSyncState.NoPushedProgress, "refs/heads/" + world.Git.Branch,
                    world.Git.Baseline, world.Git.Baseline, MirrorDirty: false)));
            db.StageOutcomes.Add(new StageOutcome { Id = w.OldId, Stage = OrchestrationStage.Review,
                StageTaskId = w.ReviewId, SubjectTaskId = subject, Source = StageOutcomeSource.Delegate,
                Outcome = StageOutcomeKind.Clean, RecordedAt = task.CompletedAt.Value.AddMinutes(-5),
                VerificationProfileVersion = 1, CommissionedRound = VerificationRound.Final,
                OrdinaryScopeCompleted = VerificationScope.Unknown, CostUsd = 2.50m, Detail = "first blocked finding" });
            db.AgentTaskEvents.Add(new AgentTaskEvent { Id = w.EventId, AgentTaskId = w.ReviewId,
                Type = AgentTaskEventType.Completed, At = task.CompletedAt.Value,
                Detail = "Delegate reported 500 characters (verdict: done)." });
            await db.SaveChangesAsync();
            return w;
        }
        catch { await world.DisposeAsync(); throw; }
    }
    public async Task ChangeAsync(Action<AgentTask> change, Guid? id = null)
    {
        await using var db = Db();
        change(await db.AgentTasks.SingleAsync(t => t.Id == (id ?? ReviewId)));
        await db.SaveChangesAsync();
    }
    public async Task ReportAsync(string report)
    { Report = report; await ChangeAsync(t => t.Result = report); }
    public async Task SnapshotAsync()
    {
        await using var db = Db();
        var t = await db.AgentTasks.SingleAsync(t => t.Id == ReviewId);
        var source = await db.AgentTaskEvents.SingleAsync(e => e.Id == EventId);
        var snapshot = new TaskCompletionNotification.Snapshot(1, ReviewId, ReviewId, EventId, OldId,
            World.CallerSessionId, AgentTaskStatus.Succeeded, Report, TaskCompletionNotification.Sha256(Report),
            DelegationNoteDigest.Compute(Report), 1, VerificationRound.Final, VerificationScope.Unknown,
            SubjectId, null, null, false, "land", "retained handoff", "old unbound header", Report,
            null, null, null, t.RepoPath, t.WorkingDirectory, t.WorktreePath, false,
            OutputDistillerMode.Shadow, null, null);
        var note = TaskCompletionNotification.Create(t, source, snapshot, source.At);
        note.State = LandNotificationState.Confirmed;
        note.ConfirmedAt = source.At.AddSeconds(1); note.ConfirmingPromptSequence = 100;
        db.AgentTaskLandNotifications.Add(note);
        await db.SaveChangesAsync();
    }
    public async Task<ReviewEvidenceRecoveryResponse> RecoverAsync(LandDeliveryBoundary? boundary = null,
        ReviewEvidenceRecoveryRequest? request = null, ITaskProgressGit? git = null, params IInterceptor[] interceptors)
    {
        await using var db = Db(interceptors);
        return await new ReviewEvidenceRecoveryService(db, new(db, git ?? World.Git.Git), git ?? World.Git.Git,
            TimeProvider.System, boundary ?? new()).RebindAsync(ReviewId, request ?? Request, Actor, CancellationToken.None);
    }
    public async Task<List<StageOutcome>> RowsAsync()
    { await using var db = Db(); return await db.StageOutcomes.AsNoTracking().Where(o => o.StageTaskId == ReviewId || o.SupersedesId == OldId).ToListAsync(); }
    public async Task<List<AgentTaskEvent>> AuditsAsync()
    { await using var db = Db(); return await db.AgentTaskEvents.AsNoTracking().Where(e => e.Type == AgentTaskEventType.FindingRecorded).ToListAsync(); }
    public async Task FindingAsync(Guid target, LandDeliveryBoundary? boundary = null, params IInterceptor[] interceptors)
    {
        await using var db = Db(interceptors);
        await new StageOutcomeService(db, boundary).RecordFindingAsync(target, new("Review", true, "manual Found"), CancellationToken.None);
    }
    public async Task UnchangedAsync(string label)
    {
        (await RowsAsync()).ShouldHaveSingleItem(label).Id.ShouldBe(OldId, label);
        (await AuditsAsync()).ShouldBeEmpty(label);
    }
    public ValueTask DisposeAsync() => World.DisposeAsync();
}
