using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class StartRefRepairAdoptionTests
{
    [Test]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Succeeded)]
    public async Task C675_DivergedOwnerMirrorAdoptsRepairCutAtRemoteTip(AgentTaskStatus status)
    {
        await using var c = await Case.CreateAsync("remote", status);
        var request = await c.H.RequestAsync(expectedSourceSha: c.S, reviewEvidenceId: c.Evidence,
            adoptFromTaskId: c.SourceId);
        await c.H.RunQueuedAsync();
        await using var db = c.H.CreateContext();
        var row = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == request.RequestId);
        var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == c.H.Fixture.TaskId && o.Active);
        row.RecoveryLocalBeforeSha.ShouldBe(c.L);
        row.RecoveryOwnerRemoteBeforeSha.ShouldBe(c.R);
        op.RecoveryOwnerRemoteAfterSha.ShouldBe(c.S);
        op.RecoveryPatchesContained.ShouldBe(true);
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        (await db.AgentTasks.SingleAsync(t => t.Id == c.H.Fixture.TaskId)).Status.ShouldBe(status);
        c.H.Fixture.Git.Trace.ShouldContain(a => a.SequenceEqual(new[] {
            "update-ref", "--no-deref", c.H.Fixture.SourceRef, c.S, c.L }));
        c.H.Fixture.Git.Trace.ShouldContain(a => a.Any(x => x == $"--force-with-lease={c.H.Fixture.SourceRef}:{c.R}"));
        Directory.Exists(c.SourcePath).ShouldBeTrue();
        (await c.H.Fixture.RequiredAsync(c.H.Fixture.Remote, "rev-parse", c.SourceRef)).Trim().ShouldBe(c.S);
    }

    [Test]
    public async Task C675_PlainLandOnDivergedMirrorStillRefuses()
    {
        await using var c = await Case.CreateAsync("remote", AgentTaskStatus.Succeeded);
        await c.H.RequestAsync(expectedSourceSha: c.S);
        c.H.Fixture.Git.Trace.Clear();
        await c.H.RunQueuedAsync();
        await using var db = c.H.CreateContext();
        (await db.AgentTaskLandRequests.SingleAsync()).SourceRefusalReason.ShouldBe("source_remote_diverged");
        (await db.AgentTaskLandings.CountAsync()).ShouldBe(0);
        c.H.Fixture.Git.Trace.ShouldNotContain(a => a.Length > 0 && a[0] == "push");
        (await c.H.Fixture.RequiredAsync(c.H.Fixture.Repository, "rev-parse", c.H.Fixture.SourceRef)).Trim().ShouldBe(c.L);
        (await c.H.Fixture.RequiredAsync(c.H.Fixture.Remote, "rev-parse", c.H.Fixture.SourceRef)).Trim().ShouldBe(c.R);
    }

    [Test]
    public async Task C675_RepairCutAtStaleMirrorRecordsUncontainedRemotePatches()
    {
        await using var c = await Case.CreateAsync("local");
        var request = await c.H.RequestAsync(expectedSourceSha: c.S, reviewEvidenceId: c.Evidence,
            adoptFromTaskId: c.SourceId);
        await c.H.RunQueuedAsync();
        await using var db = c.H.CreateContext();
        var row = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == request.RequestId);
        var op = await db.AgentTaskLandings.SingleAsync();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        op.RecoveryPatchesContained.ShouldBe(false);
        op.RecoveryUncontainedPatches.ShouldContain(c.R);
        row.RecoveryUncontainedPatches.ShouldContain(c.R);
        c.H.Fixture.Git.Trace.ShouldContain(a => a.Any(x => x == $"--force-with-lease={c.H.Fixture.SourceRef}:{c.R}"));
    }

    [Test]
    public async Task C675_RepairCutBelowBothOwnerTipsRefusesLineage()
    {
        await using var c = await Case.CreateAsync("seed");
        await c.H.RequestAsync(expectedSourceSha: c.S, reviewEvidenceId: c.Evidence,
            adoptFromTaskId: c.SourceId);
        c.H.Fixture.Git.Trace.Clear();
        await c.H.RunQueuedAsync();
        await using var db = c.H.CreateContext();
        (await db.AgentTaskLandRequests.SingleAsync()).SourceRefusalReason.ShouldBe("adopt_source_lineage");
        (await db.AgentTaskLandings.CountAsync()).ShouldBe(0);
        c.H.Fixture.Git.Trace.ShouldNotContain(a => a.Length > 0 && (a[0] == "push" || a[0] == "update-ref"));
        var pins = await c.H.Fixture.RequiredAsync(c.H.Fixture.Repository, "for-each-ref", "refs/antiphon/land");
        pins.ShouldNotContain("/adopt/");
        (await c.H.Fixture.RequiredAsync(c.H.Fixture.Repository, "rev-parse", c.H.Fixture.SourceRef)).Trim().ShouldBe(c.L);
        (await c.H.Fixture.RequiredAsync(c.H.Fixture.Remote, "rev-parse", c.H.Fixture.SourceRef)).Trim().ShouldBe(c.R);
    }

    [Test]
    public async Task C675_DetachedOwnerMirrorRefusesBeforeMutation()
    {
        await using var c = await Case.CreateAsync("remote");
        await c.H.Fixture.RequiredAsync(c.H.Fixture.Source, "checkout", "--detach", c.L);
        await c.H.RequestAsync(expectedSourceSha: c.S, reviewEvidenceId: c.Evidence,
            adoptFromTaskId: c.SourceId);
        c.H.Fixture.Git.Trace.Clear();
        await c.H.RunQueuedAsync();
        await using var db = c.H.CreateContext();
        (await db.AgentTaskLandRequests.SingleAsync()).SourceRefusalReason.ShouldBe("detached_head");
        (await db.AgentTaskLandings.CountAsync()).ShouldBe(0);
        c.H.Fixture.Git.Trace.ShouldNotContain(a => a.Length > 0 && (a[0] == "push" || a[0] == "update-ref"));
        (await c.H.Fixture.RequiredAsync(c.H.Fixture.Repository, "for-each-ref", "refs/antiphon/land"))
            .ShouldNotContain("/adopt/");
    }

    [Test]
    [Arguments("owner-subject")]
    [Arguments("interim")]
    public async Task C675_OwnerBoundOrInterimReviewCannotAdoptRepair(string kind)
    {
        await using var c = await Case.CreateAsync("remote", reviewKind: kind);
        var ex = await Should.ThrowAsync<ConflictException>(() => c.H.RequestAsync(
            expectedSourceSha: c.S, reviewEvidenceId: c.Evidence, adoptFromTaskId: c.SourceId));
        ex.Code.ShouldBe(kind == "owner-subject" ? "review_evidence_subject_mismatch" : "review_verification_scope_ineligible");
        await using var db = c.H.CreateContext();
        (await db.AgentTaskLandRequests.CountAsync()).ShouldBe(0);
    }

    private sealed class Case : IAsyncDisposable
    {
        public LandingSafetyHarness H { get; } = new();
        public string L { get; private set; } = "";
        public string R { get; private set; } = "";
        public string S { get; private set; } = "";
        public Guid SourceId { get; } = Guid.NewGuid();
        public Guid Evidence { get; private set; }
        public string SourceRef => $"refs/heads/feat/card-task-{SourceId:N}";
        public string SourcePath => Path.Combine(H.Fixture.Root, "trees", "reviewed-repair");

        public static async Task<Case> CreateAsync(string baseKind, AgentTaskStatus status = AgentTaskStatus.Failed,
            string? reviewKind = null)
        {
            var c = new Case();
            await c.H.InitializeAsync();
            var f = c.H.Fixture;
            c.L = await c.H.AddSourceAsync();
            await f.RequiredAsync(f.Source, "push", "origin", f.SourceRef);
            await File.WriteAllTextAsync(Path.Combine(f.Observer, "remote-only.txt"), "remote patch\n");
            await f.RequiredAsync(f.Observer, "add", "remote-only.txt");
            await f.RequiredAsync(f.Observer, "commit", "-m", "rewritten owner remote");
            c.R = (await f.RequiredAsync(f.Observer, "rev-parse", "HEAD")).Trim();
            await f.RequiredAsync(f.Observer, "push", $"--force-with-lease={f.SourceRef}:{c.L}",
                "origin", $"HEAD:{f.SourceRef}");
            var fetchedRef = $"refs/antiphon/fixture/{c.SourceId:N}";
            await f.RequiredAsync(f.Repository, "fetch", "origin", $"{f.SourceRef}:{fetchedRef}");
            var baseSha = baseKind switch { "remote" => c.R, "local" => c.L, _ => f.SeedSha };
            await f.RequiredAsync(f.Repository, "worktree", "add", "-b", c.SourceRef[11..], c.SourcePath, baseSha);
            await File.WriteAllTextAsync(Path.Combine(c.SourcePath, "repair.txt"), "reviewed repair\n");
            await f.RequiredAsync(c.SourcePath, "add", "repair.txt");
            await f.RequiredAsync(c.SourcePath, "commit", "-m", "repair owner source");
            c.S = (await f.RequiredAsync(c.SourcePath, "rev-parse", "HEAD")).Trim();
            await f.RequiredAsync(c.SourcePath, "push", "origin", c.SourceRef);
            await using var db = c.H.CreateContext();
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == f.TaskId);
            owner.Status = status;
            var now = DateTime.UtcNow;
            var project = new Project { Id = Guid.NewGuid(), Name = "C675", LocalRepositoryPath = f.Repository,
                CreatedAt = now, UpdatedAt = now };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "C675", CreatedAt = now, UpdatedAt = now };
            var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Ready", StateKey = "ready",
                CreatedAt = now, UpdatedAt = now };
            var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
                Identifier = "CARD-0675", Title = "StartRef repair", CreatedAt = now, UpdatedAt = now };
            db.Projects.Add(project); db.Boards.Add(board); db.BoardColumns.Add(column); db.Cards.Add(card);
            owner.ProjectId = project.Id; owner.CardId = card.Id;
            var source = new AgentTask { Id = c.SourceId, RootTaskId = c.SourceId, Title = "reviewed repair",
                Goal = "repair", Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = f.Repository, RepoPath = f.Repository, WorktreePath = c.SourcePath,
                WorktreeBranch = c.SourceRef[11..], WorktreeBaseSha = baseSha, Status = AgentTaskStatus.Failed,
                CardId = card.Id, ProjectId = project.Id, ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = now, CompletedAt = now };
            db.AgentTasks.Add(source);
            await db.SaveChangesAsync();
            var subject = reviewKind == "owner-subject" ? owner : source;
            var evidence = new StageOutcome { Id = Guid.NewGuid(), Stage = OrchestrationStage.Review,
                Outcome = StageOutcomeKind.Clean, Source = StageOutcomeSource.Delegate,
                SubjectTaskId = subject.Id, StageTaskId = Guid.NewGuid(), ReviewedSourceSha = c.S,
                ReviewedSourceRef = "refs/heads/" + subject.WorktreeBranch,
                ReviewedRepositoryPath = subject.RepoPath,
                CommissionedRound = reviewKind == "interim" ? VerificationRound.Interim : VerificationRound.Final,
                OrdinaryScopeCompleted = VerificationScope.Full, RecordedAt = now };
            db.StageOutcomes.Add(evidence);
            await db.SaveChangesAsync();
            c.Evidence = evidence.Id;
            f.Git.Trace.Clear();
            return c;
        }

        public ValueTask DisposeAsync() => H.DisposeAsync();
    }
}
