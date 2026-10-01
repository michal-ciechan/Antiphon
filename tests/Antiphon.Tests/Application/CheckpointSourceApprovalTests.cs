using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointSourceApprovalTests
{
    [Test]
    public async Task settlement_persists_source_assertion()
    {
        foreach (bool? assertion in new bool?[] { true, false, null })
        {
            await using var world = await C544World.CreateAsync();
            var settled = await world.SettleReviewAsync(reviewedSourceClean: assertion);
            settled.Outcome.ShouldBe(StageOutcomeKind.Clean);
            settled.OrdinaryScopeCompleted.ShouldBe(VerificationScope.Full);
            settled.ReviewedSourceClean.ShouldBe(assertion, $"settlement-{assertion}");
            await using (var fresh = world.CreateContext())
            {
                var saved = await fresh.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == settled.Id);
                saved.ReviewedSourceClean.ShouldBe(assertion, $"fresh-context-{assertion}");
            }
            var task = await world.TaskAsync(settled.StageTaskId!.Value);
            task.AgentSessionId.ShouldNotBeNull();
            await world.Services.GetRequiredService<AgentTaskReplyService>()
                .OnTurnEndAsync(task.AgentSessionId.Value, CancellationToken.None);
            await using var replay = world.CreateContext();
            (await replay.StageOutcomes.CountAsync(o => o.StageTaskId == settled.StageTaskId)).ShouldBe(1);
            (await replay.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == settled.Id))
                .ReviewedSourceClean.ShouldBe(assertion, $"replay-{assertion}");
        }
    }

    [Test]
    public async Task land_admission_requires_clean_review_source()
    {
        foreach (bool? assertion in new bool?[] { false, null, true })
        {
            await using var world = await C544World.CreateAsync();
            var evidence = await world.SettleReviewAsync(reviewedSourceClean: assertion);
            await using var db = world.CreateContext();
            var queue = new AgentTaskLandQueue();
            var land = C544Land.Create(db, world.Clock, queue);
            var request = new LandAgentTaskRequest(ExpectedSourceSha: world.OwnerSha, ReviewEvidenceId: evidence.Id);
            if (assertion == true)
            {
                var accepted = await land.RequestAsync(world.Owner.Id, request, CancellationToken.None);
                accepted.Status.ShouldBe("queued");
                (await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId))
                    .ReviewEvidenceId.ShouldBe(evidence.Id);
            }
            else
            {
                var error = await Should.ThrowAsync<ConflictException>(() =>
                    land.RequestAsync(world.Owner.Id, request, CancellationToken.None));
                error.Code.ShouldBe("review_evidence_source_not_clean", $"assertion-{assertion}");
                (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == world.Owner.Id)).ShouldBe(0);
                queue.TryDequeue(out _).ShouldBeFalse();
            }
        }

        await using var callerWorld = await C544World.CreateAsync();
        await using var callerDb = callerWorld.CreateContext();
        var callerLand = C544Land.Create(callerDb, callerWorld.Clock);
        var caller = await callerLand.RequestAsync(callerWorld.Owner.Id,
            new LandAgentTaskRequest(ExpectedSourceSha: callerWorld.OwnerSha), CancellationToken.None);
        caller.Status.ShouldBe("queued", "explicit-caller-approval-remains-valid");
    }

    [Test]
    public async Task recovery_and_resume_recheck_source_assertion()
    {
        foreach (var latched in new[] { false, true })
        foreach (LandPhase? cut in new LandPhase?[] { null, LandPhase.Prepared, LandPhase.Verified, LandPhase.PushStarted })
        foreach (bool? assertion in new bool?[] { false, null, true })
        {
            var label = $"ordinary latched={latched} cut={cut?.ToString() ?? "queued"} clean={assertion?.ToString() ?? "null"}";
            await using var h = new LandingProtocolHarness();
            await h.InitializeAsync();
            var sha = await h.AddSourceAsync();
            await SetLatchAsync(h, latched);
            var evidence = await SeedEvidenceAsync(h, sha, true);
            var request = await h.RequestAsync(expectedSourceSha: sha, reviewEvidenceId: evidence.Id);
            request.Status.ShouldBe("queued", label);
            if (cut is LandPhase phase)
            {
                h.Fault.Phase = phase;
                h.Fault.AfterCommit = true;
                await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync(), label);
                h.Fault.Phase = null;
                h.Fault.AfterCommit = false;
                (await h.OperationAsync()).ShouldNotBeNull(label).Phase.ShouldBe(phase, label);
            }
            await using (var db = h.CreateContext())
                await db.StageOutcomes.Where(o => o.Id == evidence.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReviewedSourceClean, assertion));

            var target = h.Git.TargetHead;
            var remote = h.Git.RemoteTarget;
            var verifierCalls = h.Verifier.Calls;
            h.Git.Trace.Clear();
            await h.RestartServicesAsync();
            await h.RunAsync();
            var op = await h.OperationAsync();
            if (assertion == true)
            {
                op.ShouldNotBeNull(label);
                new AgentTaskLandingState().HasPublication(op).ShouldBeTrue(label);
            }
            else
            {
                h.Git.TargetHead.ShouldBe(target, label);
                h.Git.RemoteTarget.ShouldBe(remote, label);
                h.Verifier.Calls.ShouldBe(verifierCalls, label);
                h.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("update-ref") ||
                    a.Contains("--ff-only") || a.Contains("rebase"), label);
                if (op is not null)
                {
                    new AgentTaskLandingState().HasPublication(op).ShouldBeFalse(label);
                    op.LastReason.ShouldBe("review_evidence_source_not_clean", label);
                }
                await using var db = h.CreateContext();
                var stored = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == request.RequestId);
                stored.ReviewEvidenceId.ShouldBe(evidence.Id, label);
                stored.ExpectedSourceSha.ShouldBe(sha, label);
                stored.IsPending.ShouldBeFalse(label);
                (await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == h.Git.TaskId &&
                    e.Type == AgentTaskEventType.LandRefused).SingleAsync()).Detail
                    .ShouldContain("review_evidence_source_not_clean", Case.Sensitive, label);
            }
        }

        foreach (var mode in new[] { "self", "adoption" })
        foreach (var timing in new[] { "admission", "resume" })
        foreach (bool? assertion in new bool?[] { false, null, true })
        {
            var label = $"{mode} {timing} clean={assertion?.ToString() ?? "null"}";
            await using var h = new LandingSafetyHarness();
            await h.InitializeAsync();
            var (reviewed, evidenceId, sourceId) = await SeedRecoveryAsync(h, mode,
                timing == "admission" ? assertion : true);
            Task<LandRequestResult> Request() => h.RequestAsync(expectedSourceSha: reviewed,
                reviewEvidenceId: evidenceId, recoverReviewedSource: mode == "self",
                adoptFromTaskId: sourceId);
            var traceStart = h.Fixture.Git.Trace.Count;
            if (timing == "admission" && assertion != true)
            {
                var error = await Should.ThrowAsync<ConflictException>(Request, label);
                error.Code.ShouldBe("review_evidence_source_not_clean", label);
                await using var db = h.CreateContext();
                (await db.AgentTaskLandRequests.CountAsync()).ShouldBe(0, label);
                h.Fixture.Git.Trace.Skip(traceStart).ShouldNotContain(a => a.Contains("update-ref") ||
                    a.Contains("reset") || a.Contains("rebase") || a.Contains("merge") || a.Contains("push"), label);
                continue;
            }

            var accepted = await Request();
            accepted.Status.ShouldBe("queued", label);
            if (timing == "resume")
            {
                await using var db = h.CreateContext();
                await db.StageOutcomes.Where(o => o.Id == evidenceId)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReviewedSourceClean, assertion));
                await h.RestartServicesAsync();
            }
            var remoteSource = (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse",
                h.Fixture.SourceRef)).Trim();
            var remoteTarget = (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse",
                h.Fixture.TargetRef)).Trim();
            h.Fixture.Git.Trace.Clear();
            if (timing == "resume") await h.RunAsync();
            else await h.RunQueuedAsync();
            if (assertion == true)
            {
                var op = (await h.OperationAsync()).ShouldNotBeNull(label);
                new AgentTaskLandingState().HasPublication(op).ShouldBeTrue(label);
            }
            else
            {
                (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse",
                    h.Fixture.SourceRef)).Trim().ShouldBe(remoteSource, label);
                (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse",
                    h.Fixture.TargetRef)).Trim().ShouldBe(remoteTarget, label);
                h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("update-ref") || a.Contains("reset") ||
                    a.Contains("rebase") || a.Contains("merge") || a.Contains("push"), label);
                await using var db = h.CreateContext();
                var stored = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == accepted.RequestId);
                stored.ReviewEvidenceId.ShouldBe(evidenceId, label);
                stored.IsPending.ShouldBeFalse(label);
                (await db.AgentTaskLandings.CountAsync()).ShouldBe(0, label);
            }
        }

        foreach (var mode in new[] { "ordinary", "self", "adoption" })
        foreach (bool? assertion in new bool?[] { false, null })
        {
            var label = $"published-cleanup {mode} clean={assertion?.ToString() ?? "null"}";
            await using var h = new LandingSafetyHarness();
            await h.InitializeAsync();
            string reviewed;
            Guid evidenceId;
            Guid? sourceId;
            if (mode == "ordinary")
            {
                reviewed = await h.AddSourceAsync();
                sourceId = null;
                await using var db = h.CreateContext();
                var evidence = new StageOutcome
                {
                    Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
                    Source = StageOutcomeSource.Delegate, SubjectTaskId = h.Fixture.TaskId,
                    StageTaskId = Guid.NewGuid(), ReviewedSourceSha = reviewed, ReviewedSourceClean = true,
                    ReviewedSourceRef = h.Fixture.SourceRef, ReviewedRepositoryPath = h.Fixture.Repository,
                    CommissionedRound = VerificationRound.Final, OrdinaryScopeCompleted = VerificationScope.Full,
                    RecordedAt = DateTime.UtcNow,
                };
                db.StageOutcomes.Add(evidence);
                await db.SaveChangesAsync();
                evidenceId = evidence.Id;
            }
            else
            {
                (reviewed, evidenceId, sourceId) = await SeedRecoveryAsync(h, mode, true);
            }
            var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            await File.WriteAllTextAsync(sentinel, "owned cleanup residue");
            await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidenceId,
                recoverReviewedSource: mode == "self", adoptFromTaskId: sourceId);
            await h.RunQueuedAsync();
            var published = (await h.OperationAsync()).ShouldNotBeNull(label);
            new AgentTaskLandingState().HasPublication(published).ShouldBeTrue(label);
            published.Cleanup.ShouldBe(LandCleanupStatus.Refused, label);
            await using (var db = h.CreateContext())
                await db.StageOutcomes.Where(o => o.Id == evidenceId)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReviewedSourceClean, assertion));
            var verifierCalls = h.Verifier.Calls;
            h.Fixture.Git.Trace.Clear();
            await h.RestartServicesAsync();
            File.Delete(sentinel);
            await h.RequestCleanupRetryAsync(published.Id);
            await h.RunQueuedAsync();
            var after = (await h.OperationAsync()).ShouldNotBeNull(label);
            after.Id.ShouldBe(published.Id, label);
            after.Cleanup.ShouldBe(LandCleanupStatus.Complete, label);
            h.Verifier.Calls.ShouldBe(verifierCalls, label);
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("merge") ||
                a.Contains("push"), label);
        }
    }

    private static async Task<(string Reviewed, Guid EvidenceId, Guid? SourceId)> SeedRecoveryAsync(
        LandingSafetyHarness h, string mode, bool? clean)
    {
        var ownerHead = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", h.Fixture.SourceRef);
        var reviewed = ownerHead;
        Guid? sourceId = null;
        string sourceRef = h.Fixture.SourceRef;
        string sourcePath = h.Fixture.Source;
        if (mode == "adoption")
        {
            sourceId = Guid.NewGuid();
            sourceRef = $"refs/heads/feat/card-task-{sourceId:N}";
            sourcePath = Path.Combine(h.Fixture.Root, "trees", "reviewed-repair");
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "-b", sourceRef[11..],
                sourcePath, ownerHead);
            await File.WriteAllTextAsync(Path.Combine(sourcePath, "repair.txt"), "reviewed repair\n");
            await h.Fixture.RequiredAsync(sourcePath, "add", ".");
            await h.Fixture.RequiredAsync(sourcePath, "commit", "-m", "reviewed repair");
            reviewed = (await h.Fixture.RequiredAsync(sourcePath, "rev-parse", "HEAD")).Trim();
            await h.Fixture.RequiredAsync(sourcePath, "push", "origin", sourceRef);
        }
        await using var db = h.CreateContext();
        var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
        owner.Status = AgentTaskStatus.Failed;
        if (sourceId is Guid sid)
        {
            var now = DateTime.UtcNow;
            var projectId = Guid.NewGuid();
            var boardId = Guid.NewGuid();
            var columnId = Guid.NewGuid();
            var cardId = Guid.NewGuid();
            db.Projects.Add(new Project
            {
                Id = projectId, Name = "c835 recovery", LocalRepositoryPath = h.Fixture.Repository,
                CreatedAt = now, UpdatedAt = now,
            });
            db.Boards.Add(new Board
            {
                Id = boardId, ProjectId = projectId, Name = "c835 recovery", CreatedAt = now, UpdatedAt = now,
            });
            db.BoardColumns.Add(new BoardColumn
            {
                Id = columnId, BoardId = boardId, Name = "Ready", StateKey = "ready",
                CreatedAt = now, UpdatedAt = now,
            });
            db.Cards.Add(new Card
            {
                Id = cardId, BoardId = boardId, BoardColumnId = columnId, Identifier = "CARD-0835",
                Title = "source approval recovery", CreatedAt = now, UpdatedAt = now,
            });
            owner.ProjectId = projectId;
            owner.CardId = cardId;
            db.AgentTasks.Add(new AgentTask
            {
                Id = sid, RootTaskId = sid, Title = "reviewed repair", Goal = "repair", Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree, WorkingDirectory = h.Fixture.Repository,
                RepoPath = h.Fixture.Repository, WorktreePath = sourcePath, WorktreeBranch = sourceRef[11..],
                WorktreeBaseSha = ownerHead, Status = AgentTaskStatus.Failed, CardId = cardId, ProjectId = projectId,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, CompletedAt = now,
            });
        }
        await db.SaveChangesAsync();
        var evidence = new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Delegate, SubjectTaskId = sourceId ?? owner.Id, StageTaskId = Guid.NewGuid(),
            ReviewedSourceSha = reviewed, ReviewedSourceClean = clean, ReviewedSourceRef = sourceRef,
            ReviewedRepositoryPath = h.Fixture.Repository, CommissionedRound = VerificationRound.Final,
            OrdinaryScopeCompleted = VerificationScope.Full, RecordedAt = DateTime.UtcNow,
        };
        db.StageOutcomes.Add(evidence);
        await db.SaveChangesAsync();
        return (reviewed, evidence.Id, sourceId);
    }

    [Test]
    public async Task migration_and_override_preserve_unknown()
    {
        var databaseName = "test_c835_upgrade_" + Guid.NewGuid().ToString("N");
        await using (var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString))
        {
            await maintenance.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", maintenance);
            await create.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(TestDbFixture.ConnectionString)
        { Database = databaseName }.ConnectionString;
        await using (var owned = new IsolatedTestSchema(databaseName, connection))
        {
            var options = TestDbFixture.CreateDbContextOptions(connection);
            await using var db = new AppDbContext(options);
            var migrations = db.Database.GetMigrations().ToArray();
            var index = Array.FindIndex(migrations, name => name.EndsWith("_AddReviewSourceClean", StringComparison.Ordinal));
            index.ShouldBeGreaterThan(0, "generated-migration-present");
            await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
            var legacyId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "StageOutcomes" ("Id", "Stage", "Outcome", "Source", "DurationSeconds",
                    "Detail", "RecordedAt", "ReviewedSourceSha", "ReviewedSourceRef", "ReviewedRepositoryPath")
                VALUES ({legacyId}, {(int)OrchestrationStage.Review}, {(int)StageOutcomeKind.Clean},
                    {(int)StageOutcomeSource.Delegate}, 0, 'legacy clean review', {DateTime.UtcNow},
                    {new string('a', 40)}, 'refs/heads/legacy', '/legacy/repo')
                """);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index]);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index]);
            await using (var fresh = new AppDbContext(options))
                (await fresh.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == legacyId))
                    .ReviewedSourceClean.ShouldBeNull("legacy-source-clean-remains-null");
            await using var sql = new NpgsqlConnection(connection);
            await sql.OpenAsync();
            await using var column = new NpgsqlCommand("""
                SELECT is_nullable, column_default FROM information_schema.columns
                WHERE table_name = 'StageOutcomes' AND column_name = 'ReviewedSourceClean'
                """, sql);
            await using var result = await column.ExecuteReaderAsync();
            (await result.ReadAsync()).ShouldBeTrue();
            result.GetString(0).ShouldBe("YES");
            result.IsDBNull(1).ShouldBeTrue("migration-must-not-default-legacy-to-true");
            await result.CloseAsync();
            await db.StageOutcomes.Where(o => o.Id == legacyId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReviewedSourceClean, true));
            await using (var fresh = new AppDbContext(options))
                (await fresh.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == legacyId))
                    .ReviewedSourceClean.ShouldBe(true, "true-roundtrip");
            await db.StageOutcomes.Where(o => o.Id == legacyId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReviewedSourceClean, false));
            await using (var fresh = new AppDbContext(options))
                (await fresh.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == legacyId))
                    .ReviewedSourceClean.ShouldBe(false, "false-roundtrip");
        }

        foreach (bool? assertion in new bool?[] { null, false, true })
        {
            await using var world = await C544World.CreateAsync();
            var old = await world.SettleReviewAsync(reviewedSourceClean: true);
            await using var db = world.CreateContext();
            var finding = await new StageOutcomeService(db).RecordFindingAsync(old.StageTaskId!.Value,
                new RecordStageFindingRequest("Review", Found: false, Detail: "new finding",
                    ReviewedSourceSha: assertion is null ? null : world.OwnerSha,
                    ReviewedSourceClean: assertion), CancellationToken.None);
            finding.SupersedesId.ShouldBe(old.Id);
            finding.ReviewedSourceClean.ShouldBe(assertion, "override-does-not-inherit-true");
            await using var fresh = world.CreateContext();
            (await fresh.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == old.Id))
                .ReviewedSourceClean.ShouldBe(true, "old-row-unchanged");
            (await fresh.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == finding.Id))
                .ReviewedSourceClean.ShouldBe(assertion, "new-row-roundtrip");
        }
    }

    private static async Task SetLatchAsync(LandingProtocolHarness h, bool latched)
    {
        await using var db = h.CreateContext();
        await db.AgentTasks.Where(t => t.Id == h.Git.TaskId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RequiresFinalVerificationReview, latched)
                .SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()));
    }

    private static async Task<StageOutcome> SeedEvidenceAsync(LandingProtocolHarness h, string sha, bool? clean)
    {
        await using var db = h.CreateContext();
        var row = new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Delegate, SubjectTaskId = h.Git.TaskId, StageTaskId = Guid.NewGuid(),
            ReviewedSourceSha = sha, ReviewedSourceClean = clean, ReviewedSourceRef = h.Git.SourceRef,
            ReviewedRepositoryPath = h.Git.Repository, VerificationProfileVersion = 1,
            CommissionedRound = VerificationRound.Final, OrdinaryScopeCompleted = VerificationScope.Full,
            Detail = "checkpoint source assertion", RecordedAt = DateTime.UtcNow,
        };
        db.StageOutcomes.Add(row);
        await db.SaveChangesAsync();
        return row;
    }
}
