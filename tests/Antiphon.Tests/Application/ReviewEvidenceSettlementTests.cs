using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
public sealed class ReviewEvidenceSettlementTests
{
    private const string Warning = "review_evidence_not_standalone";
    private const string HeaderWarning = "review-evidence-warning=review_evidence_not_standalone";

    private static string Presented(string report, string kind)
    {
        var start = report.IndexOf(ReviewEvidence.Heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = report.IndexOf("\n\n", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        var block = report[start..end];
        var presentation = kind switch
        {
            "fence" => "```\n" + block + "\n```",
            "quote" => string.Join("\n", block.Split('\n').Select(line => "> " + line)),
            "indent" => string.Join("\n", block.Split('\n').Select(line => " " + line)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return report[..start] + presentation + report[end..];
    }

    private static async Task<(Guid Id, Guid Session, StageOutcome Outcome, AgentTask Task, AgentTaskLandNotification? Note)> SettleAsync(
        C544World world, string? presentation = null, bool found = false, bool follow = false,
        CreateAgentTaskRequest? request = null, Guid? subject = null, string? sha = null, string scope = "Full",
        string next = "land", Func<Guid, string, string>? reportEdit = null, bool? reviewedSourceClean = true)
    {
        var spec = request ?? world.FinalReview();
        if (follow)
            spec = spec with { FollowUpOnTask = world.Owner.Id.ToString("D"), Stage = OrchestrationStage.Review };
        var created = await world.CreateTaskAsync(spec);
        var session = await world.DispatchAsync(created.Id);
        var report = C544World.ReviewReport(created.Id, subject ?? world.Owner.Id, sha ?? world.OwnerSha,
            scope, found, next, reviewedSourceClean: reviewedSourceClean);
        if (presentation is not null) report = Presented(report, presentation);
        if (reportEdit is not null) report = reportEdit(created.Id, report);
        await world.SeedTurnAsync(session, created.Id, report);
        await world.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(session, CancellationToken.None);
        await using var db = world.CreateContext();
        return (created.Id, session,
            await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.StageTaskId == created.Id),
            await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id),
            await db.AgentTaskLandNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.TaskId == created.Id && n.Kind == LandNotificationKind.TaskCompletion));
    }

    [Test]
    public async Task C807_IgnoredEvidenceSettlesWithoutApproval()
    {
        foreach (var presentation in new[] { "fence", "quote", "indent" })
        foreach (var found in new[] { false, true })
        foreach (var follow in new[] { false, true })
        {
            var row = $"{presentation} found={found} follow={follow}";
            await using var world = await C544World.CreateAsync();
            var result = await SettleAsync(world, presentation, found, follow, next: found ? "code" : "land");
            result.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, row);
            result.Outcome.Outcome.ShouldBe(found ? StageOutcomeKind.Found : StageOutcomeKind.Clean, row);
            result.Outcome.SubjectTaskId.ShouldBe(follow ? world.Owner.Id : null, row);
            result.Outcome.ReviewedSourceSha.ShouldBeNull(row);
            result.Outcome.ReviewedSourceRef.ShouldBeNull(row);
            result.Outcome.ReviewedRepositoryPath.ShouldBeNull(row);
            result.Outcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Unknown, row);
            result.Task.NextStage.ShouldBe(found ? PipelineHandoffKind.Code : PipelineHandoffKind.Land, row);
            var note = result.Note.ShouldNotBeNull(row);
            var snapshot = TaskCompletionNotification.TryReadSnapshot(note.CompletionSnapshotJson).ShouldNotBeNull(row);
            snapshot.NoteHeader.ShouldContain(HeaderWarning, Case.Sensitive, row);
            snapshot.NoteHeader.ShouldContain("fenced, quoted or indented", Case.Sensitive, row);
            snapshot.NoteHeader.ShouldContain("bare lines before the next-stage block", Case.Sensitive, row);
            snapshot.NoteHeader.ShouldContain("scope=Unknown", Case.Sensitive, row);
            snapshot.NoteHeader.ShouldNotContain("review-evidence=", Case.Sensitive, row);
            snapshot.NoteHeader.ShouldNotContain("subject=", Case.Sensitive, row);
            snapshot.NoteHeader.ShouldNotContain("reviewed-sha=", Case.Sensitive, row);
            await using var db = world.CreateContext();
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == result.Id && e.Type == AgentTaskEventType.Warning
                && e.Detail == Warning)).ShouldBe(1, row);
        }
    }

    [Test]
    public async Task C807_StandaloneEvidenceAndScope()
    {
        await using var world = await C544World.CreateAsync();
        var baseline = await world.SettleReviewAsync(found: true, next: "code");
        foreach (var found in new[] { false, true })
        foreach (var interim in new[] { false, true })
        {
            var row = $"found={found} interim={interim}";
            var request = interim ? world.InterimReview(baseline.Id) : world.FinalReview();
            var result = await SettleAsync(world, found: found, request: request, next: found ? "code" : "land");
            result.Outcome.SubjectTaskId.ShouldBe(world.Owner.Id, row);
            result.Outcome.ReviewedSourceSha.ShouldBe(world.OwnerSha, row);
            result.Outcome.ReviewedSourceRef.ShouldBe("refs/heads/" + world.Owner.WorktreeBranch, row);
            result.Outcome.ReviewedRepositoryPath.ShouldBe(world.RepositoryPath, row);
            result.Outcome.OrdinaryScopeCompleted.ShouldBe(interim ? VerificationScope.Interim : VerificationScope.Full, row);
            var snapshot = TaskCompletionNotification.TryReadSnapshot(result.Note!.CompletionSnapshotJson)!;
            snapshot.NoteHeader.ShouldNotContain(HeaderWarning, Case.Sensitive, row);
        }
    }

    [Test]
    public async Task C807_RepeatedSettlementIsIdempotent()
    {
        await using var world = await C544World.CreateAsync();
        var result = await SettleAsync(world, "fence");
        var originalNote = result.Note.ShouldNotBeNull();
        await world.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(result.Session, CancellationToken.None);
        await using var db = world.CreateContext();
        (await db.StageOutcomes.Where(o => o.StageTaskId == result.Id).Select(o => o.Id).ToListAsync())
            .ShouldBe(new[] { result.Outcome.Id });
        (await db.AgentTaskLandNotifications.Where(n => n.TaskId == result.Id && n.Kind == LandNotificationKind.TaskCompletion)
            .Select(n => n.Id).ToListAsync()).ShouldBe(new[] { originalNote.Id });
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == result.Id && e.Type == AgentTaskEventType.Warning
            && e.Detail == Warning)).ShouldBe(1);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == result.Id && e.Type == AgentTaskEventType.Completed)).ShouldBe(1);
    }

    [Test]
    public async Task C807_UnsuccessfulReviewCannotBind()
    {
        foreach (var token in new[] { "failed", "blocked" })
        {
            await using var world = await C544World.CreateAsync();
            var result = await SettleAsync(world, reportEdit: (id, report) => report + "\n" +
                DelegationReportFormatter.ReportToken(id, token));
            result.Task.Status.ShouldBe(token == "failed" ? AgentTaskStatus.Failed : AgentTaskStatus.Blocked);
            result.Outcome.ReviewedSourceSha.ShouldBeNull(token);
            result.Outcome.ReviewedSourceRef.ShouldBeNull(token);
            result.Outcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Unknown, token);
        }
    }

    [Test]
    public async Task C807_AbsentAndOtherInvalidEvidenceKeepBehavior()
    {
        foreach (var row in new[] { "absent", "subject", "sha" })
        {
            await using var world = await C544World.CreateAsync();
            var result = await SettleAsync(world, reportEdit: (_, report) => row switch
            {
                "absent" => report.Replace(ReviewEvidence.Heading, "evidence omitted"),
                "subject" => report.Replace(world.Owner.Id.ToString("D"), Guid.Empty.ToString("D")),
                _ => report.Replace(world.OwnerSha, "bad"),
            });
            result.Outcome.ReviewedSourceSha.ShouldBeNull(row);
            var snapshot = TaskCompletionNotification.TryReadSnapshot(result.Note!.CompletionSnapshotJson)!;
            snapshot.NoteHeader.ShouldNotContain(HeaderWarning, Case.Sensitive, row);
            await using var db = world.CreateContext();
            var expected = row switch { "absent" => "Review settled without usable review evidence.",
                "subject" => "review_evidence_subject_invalid", _ => "review_evidence_sha_invalid" };
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == result.Id && e.Type == AgentTaskEventType.Warning
                && e.Detail == expected)).ShouldBeTrue(row);
        }
    }

    [Test]
    public async Task C807_FollowUpSubjectMustMatch()
    {
        await using var world = await C544World.CreateAsync();
        var own = await SettleAsync(world, follow: true);
        own.Outcome.SubjectTaskId.ShouldBe(world.Owner.Id);
        var other = await SeedSubjectAsync(world, world.Card.Id, WorkspaceMode.Worktree);
        var mismatch = await SettleAsync(world, follow: true, subject: other.Id);
        mismatch.Outcome.SubjectTaskId.ShouldBe(world.Owner.Id);
        mismatch.Outcome.ReviewedSourceSha.ShouldBeNull();
        mismatch.Outcome.ReviewedSourceRef.ShouldBeNull();
        mismatch.Outcome.ReviewedRepositoryPath.ShouldBeNull();
        await using var db = world.CreateContext();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == mismatch.Id && e.Type == AgentTaskEventType.Warning
            && e.Detail!.Contains("follow-up subject"))).ShouldBeTrue();
    }

    [Test]
    public async Task C807_SubjectAuthorizationRequired()
    {
        await using var world = await C544World.CreateAsync();
        var foreign = await SeedSubjectAsync(world, Guid.NewGuid(), WorkspaceMode.Worktree);
        var result = await SettleAsync(world, subject: foreign.Id);
        result.Outcome.SubjectTaskId.ShouldBeNull();
        result.Outcome.ReviewedSourceSha.ShouldBeNull();
        result.Outcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Unknown);
        await using var db = world.CreateContext();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == result.Id && e.Type == AgentTaskEventType.Warning
            && e.Detail!.Contains("not an authorized Worktree"))).ShouldBeTrue();
        var unbound = await SeedSubjectAsync(world, null, WorkspaceMode.Worktree);
        var unboundResult = await SettleAsync(world, subject: unbound.Id);
        unboundResult.Outcome.ReviewedSourceSha.ShouldBeNull();
        unboundResult.Outcome.SubjectTaskId.ShouldBeNull();
        var missing = await SettleAsync(world, subject: Guid.NewGuid());
        missing.Outcome.SubjectTaskId.ShouldBeNull();
        missing.Outcome.ReviewedSourceSha.ShouldBeNull();
        missing.Outcome.ReviewedSourceRef.ShouldBeNull();
        missing.Outcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Unknown);
    }

    [Test]
    public async Task C807_WorktreeSubjectRequired()
    {
        await using var world = await C544World.CreateAsync();
        foreach (var mode in new[] { WorkspaceMode.Shared, WorkspaceMode.ReadOnly })
        {
            var subject = await SeedSubjectAsync(world, world.Card.Id, mode);
            var result = await SettleAsync(world, subject: subject.Id);
            result.Outcome.SubjectTaskId.ShouldBeNull(mode.ToString());
            result.Outcome.ReviewedSourceSha.ShouldBeNull(mode.ToString());
            result.Outcome.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Unknown, mode.ToString());
        }
    }

    [Test]
    public async Task C807_WarningPreservesOtherWarnings()
    {
        await using var world = await C544World.CreateAsync(delegation: d => d.ReplyInlineMaxChars = 200);
        var tracked = Path.Combine(world.RepositoryPath, "README.md");
        await File.AppendAllTextAsync(tracked, "dirty review\n");
        var result = await SettleAsync(world, "fence", reportEdit: (_, report) => report.Replace("Reviewed the owner.",
            "Reviewed `README.md` in the owner checkout."));
        var snapshot = TaskCompletionNotification.TryReadSnapshot(result.Note!.CompletionSnapshotJson)!;
        snapshot.NoteHeader.ShouldContain(HeaderWarning);
        snapshot.NoteHeader.ShouldContain("uncommitted", Case.Insensitive);
        result.Task.Result.ShouldContain("README.md");
    }

    [Test]
    public async Task C807_SourceReviewFeedsAdoption()
    {
        await using var fixture = await AdoptionFixture.CreateAsync();
        var review = await SettleAsync(fixture.World, subject: fixture.SourceId, sha: fixture.SourceSha);
        review.Outcome.SubjectTaskId.ShouldBe(fixture.SourceId);
        review.Outcome.ReviewedSourceSha.ShouldBe(fixture.SourceSha);
        review.Outcome.ReviewedSourceRef.ShouldBe(fixture.SourceRef);
        review.Outcome.ReviewedRepositoryPath.ShouldBe(fixture.Harness.Fixture.Repository);
        review.Outcome.SubjectTaskId.ShouldNotBe(review.Id);
        review.Outcome.SubjectTaskId.ShouldNotBe(fixture.Harness.Fixture.TaskId);
        var admitted = await fixture.Harness.RequestAsync(expectedSourceSha: fixture.SourceSha,
            reviewEvidenceId: review.Outcome.Id, adoptFromTaskId: fixture.SourceId);
        await using var db = fixture.Harness.CreateContext();
        var request = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == admitted.RequestId);
        request.TaskId.ShouldBe(fixture.Harness.Fixture.TaskId);
        request.RecoverySourceTaskId.ShouldBe(fixture.SourceId);
        request.ReviewEvidenceId.ShouldBe(review.Outcome.Id);
        request.ExpectedSourceSha.ShouldBe(fixture.SourceSha);
        request.RecoverySourceFullRef.ShouldBe(fixture.SourceRef);
    }

    [Test]
    public async Task C807_WrongOwnerEvidenceRefusesAdoption()
    {
        await using var fixture = await AdoptionFixture.CreateAsync();
        var ownerReview = await SettleAsync(fixture.World, subject: fixture.Harness.Fixture.TaskId,
            sha: fixture.SourceSha);
        ownerReview.Outcome.SubjectTaskId.ShouldBe(fixture.Harness.Fixture.TaskId);
        var refusal = await Should.ThrowAsync<ConflictException>(() => fixture.Harness.RequestAsync(
            expectedSourceSha: fixture.SourceSha, reviewEvidenceId: ownerReview.Outcome.Id,
            adoptFromTaskId: fixture.SourceId));
        refusal.Code.ShouldBe("review_evidence_subject_mismatch");
        await using var db = fixture.Harness.CreateContext();
        (await db.AgentTaskLandRequests.CountAsync()).ShouldBe(0);
    }

    private sealed class AdoptionFixture : IAsyncDisposable
    {
        public LandingSafetyHarness Harness { get; private set; } = null!;
        public C544World World { get; private set; } = null!;
        public Guid SourceId { get; private set; }
        public string SourceRef { get; private set; } = "";
        public string SourceSha { get; private set; } = "";

        public static async Task<AdoptionFixture> CreateAsync()
        {
            var fixture = new AdoptionFixture { Harness = new LandingSafetyHarness() };
            try
            {
                var h = fixture.Harness;
                await h.InitializeAsync();
                var ownerSha = await h.AddSourceAsync();
                await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
                fixture.SourceId = Guid.NewGuid();
                fixture.SourceRef = "refs/heads/feat/card-task-" + fixture.SourceId.ToString("N");
                var sourcePath = Path.Combine(h.Fixture.Root, "trees", "c807-source");
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "-b",
                    fixture.SourceRef[11..], sourcePath, ownerSha);
                await File.WriteAllTextAsync(Path.Combine(sourcePath, "c807.txt"), "reviewed source\n");
                await h.Fixture.RequiredAsync(sourcePath, "add", ".");
                await h.Fixture.RequiredAsync(sourcePath, "commit", "-m", "source review");
                fixture.SourceSha = (await h.Fixture.RequiredAsync(sourcePath, "rev-parse", "HEAD")).Trim();
                await h.Fixture.RequiredAsync(sourcePath, "push", "origin", fixture.SourceRef);
                fixture.World = await C544World.AttachAsync(h.Schema, h.Fixture.Repository,
                    Path.Combine(h.Fixture.Root, "trees"), h.Fixture.TaskId, ownerSha);
                await using (var db = h.CreateContext())
                {
                    var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
                    owner.Status = AgentTaskStatus.Failed;
                    owner.FailureReason = "Source needs reviewed recovery.";
                    db.AgentTasks.Add(new AgentTask
                    {
                        Id = fixture.SourceId, RootTaskId = fixture.SourceId, Title = "C807 source", Goal = "source",
                        Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                        WorkingDirectory = h.Fixture.Repository, RepoPath = h.Fixture.Repository,
                        WorktreePath = sourcePath, WorktreeBranch = fixture.SourceRef[11..],
                        WorktreeBaseSha = ownerSha, Status = AgentTaskStatus.Succeeded,
                        CardId = fixture.World.Card.Id, ProjectId = fixture.World.Project.Id,
                        ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync();
                }
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (World is not null) await World.DisposeAsync();
            if (Harness is not null) await Harness.DisposeAsync();
        }
    }

    private static async Task<AgentTask> SeedSubjectAsync(C544World world, Guid? cardId, WorkspaceMode workspace)
    {
        await using var db = world.CreateContext();
        if (cardId is { } foreign && foreign != world.Card.Id)
        {
            var column = await db.BoardColumns.FirstAsync(c => c.BoardId == world.Board.Id);
            db.Cards.Add(new Card
            {
                Id = foreign, BoardId = world.Board.Id, BoardColumnId = column.Id,
                Identifier = "CARD-0807-F", Title = "foreign", CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "C807 source", Goal = "source", Role = AgentTaskRole.Code,
            Kind = AgentTaskKind.Worker, Workspace = workspace, Status = AgentTaskStatus.Succeeded,
            WorkingDirectory = world.RepositoryPath, RepoPath = world.RepositoryPath,
            WorktreePath = world.RepositoryPath, WorktreeBranch = "feat/card-task-" + id.ToString("N"),
            CardId = cardId, ProjectId = world.Project.Id, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }
}
