using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.InterimVerificationPolicyTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-6, R-5. The latched-owner final-review land guard through the production
/// <see cref="AgentTaskLandService"/> and <see cref="AgentTaskLandingProtocol"/> on the controlled
/// landing harness (isolated DB, controlled Git ordering — never actual publication; V-7 owns that).
/// Approval dimensions vary one at a time from a valid Clean Final/Full Review, and refusals are
/// asserted as zero requests and zero publication mutations, not just a code.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class InterimVerificationLandGuardTests
{
    [Test]
    public async Task C544_AdmissionRace()
    {
        // land-first: the land request holds the owner row; Interim admission must see it and refuse.
        await using (var world = await C544World.CreateAsync())
        {
            var baseline = await world.SettleReviewAsync();
            var barrier = new SaveBarrier(e => e.Entries<AgentTaskLandRequest>().Any(r => r.State == EntityState.Added));
            await using var landDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(
                TestDbFixture.CreateDbContextOptions(world.Schema.ConnectionString)).AddInterceptors(barrier).Options);
            var land = C544Land.Create(landDb, world.Clock);
            var landing = Task.Run(() => land.RequestAsync(world.Owner.Id, new LandAgentTaskRequest(ExpectedSourceSha: world.OwnerSha), CancellationToken.None));
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var admitting = Task.Run(() => world.CreateTaskAsync(world.InterimReview(baseline.Id)));
            await WaitForLockWaitAsync(world.Schema.ConnectionString);
            barrier.Release.TrySetResult();
            (await landing).Status.ShouldBe("queued", "land-first: land admitted");
            var refused = await Should.ThrowAsync<HttpException>(() => admitting, "land-first");
            refused.Code.ShouldBe(InterimVerificationPolicy.OwnerLandingCode, "land-first");
            (await world.TaskCountAsync(q => q.Where(t => t.VerificationRound == VerificationRound.Interim))).ShouldBe(0, "land-first: never admits Interim");
            (await world.TaskAsync(world.Owner.Id)).RequiresFinalVerificationReview.ShouldBeFalse("land-first: no latch");
        }

        // interim-first: the admission holds the owner row; the land then sees the committed latch.
        {
            var barrier = new SaveBarrier(e => e.Entries<AgentTask>().Any(t => t.State == EntityState.Added
                && t.Entity.VerificationRound == VerificationRound.Interim));
            barrier.Armed = false;
            await using var world = await C544World.CreateAsync(barrier);
            var baseline = await world.SettleReviewAsync();
            barrier.Armed = true;
            var admitting = Task.Run(() => world.CreateTaskAsync(world.InterimReview(baseline.Id)));
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(60));
            await using var landDb = world.CreateContext();
            var land = C544Land.Create(landDb, world.Clock);
            var landing = Task.Run(() => land.RequestAsync(world.Owner.Id, new LandAgentTaskRequest(ExpectedSourceSha: world.OwnerSha), CancellationToken.None));
            await WaitForLockWaitAsync(world.Schema.ConnectionString);
            barrier.Release.TrySetResult();
            await admitting;
            var error = await Should.ThrowAsync<ConflictException>(() => landing, "interim-first");
            error.Code.ShouldBe(LandApproval.FinalReviewRequiredCode, "interim-first: land refuses without Final");
            (await landDb.AgentTaskLandRequests.CountAsync(r => r.TaskId == world.Owner.Id)).ShouldBe(0, "interim-first: no request");
        }

        // pre-commit failure: a rolled-back admission leaves no latch, so explicit-caller land stays available.
        {
            var fault = new LatchFault { FailLatchSave = true, Armed = false };
            await using var world = await C544World.CreateAsync(fault);
            var baseline = await world.SettleReviewAsync();
            fault.Armed = true;
            await Should.ThrowAsync<IOException>(() => world.CreateTaskAsync(world.InterimReview(baseline.Id)), "pre-commit-failure");
            fault.Armed = false;
            await using var landDb = world.CreateContext();
            (await C544Land.Create(landDb, world.Clock).RequestAsync(world.Owner.Id,
                new LandAgentTaskRequest(ExpectedSourceSha: world.OwnerSha), CancellationToken.None)).Status.ShouldBe("queued", "pre-commit-failure: rollback leaves owner unlatched");
        }
    }

    [Test]
    public async Task C544_NoEvidenceRefuses()
    {
        await using var h = await HarnessAsync(latched: true);
        var sha = await h.AddSourceAsync();
        var error = await Should.ThrowAsync<ConflictException>(() => h.RequestAsync(expectedSourceSha: sha), "latched-no-evidence");
        error.Code.ShouldBe(LandApproval.FinalReviewRequiredCode, "latched-no-evidence");
        await AssertNothingAdmittedAsync(h, "latched-no-evidence");

        // Unlatched legacy/full-only owners keep CARD-0488 explicit-caller compatibility.
        await SetLatchAsync(h, false);
        (await h.RequestAsync(expectedSourceSha: sha)).Status.ShouldBe("queued", "unlatched-explicit-caller");
    }

    [Test]
    public async Task C544_InterimApprovalRefuses()
    {
        foreach (var latched in new[] { true, false })
        {
            foreach (var (row, round, scope) in new (string, VerificationRound?, VerificationScope?)[]
                     {
                         ("interim-round", VerificationRound.Interim, VerificationScope.Interim),
                         ("interim-scope", VerificationRound.Final, VerificationScope.Interim),
                     })
            {
                var label = $"{row} latched={latched}";
                await using var h = await HarnessAsync(latched);
                var sha = await h.AddSourceAsync();
                var evidence = await SeedEvidenceAsync(h, sha, o => { o.CommissionedRound = round; o.OrdinaryScopeCompleted = scope; });
                var error = await Should.ThrowAsync<ConflictException>(() => h.RequestAsync(expectedSourceSha: sha, reviewEvidenceId: evidence.Id), label);
                error.Code.ShouldBe(LandApproval.ScopeIneligibleCode, label);
                await AssertNothingAdmittedAsync(h, label);
            }
        }
    }

    [Test]
    public async Task C544_FinalFullRequired()
    {
        foreach (var (row, round, scope, admits) in new (string, VerificationRound?, VerificationScope?, bool)[]
                 {
                     ("final-unknown", VerificationRound.Final, VerificationScope.Unknown, false),
                     ("final-none", VerificationRound.Final, VerificationScope.None, false),
                     ("legacy-null-scope", null, null, false),
                     ("final-full", VerificationRound.Final, VerificationScope.Full, true),
                 })
        {
            await using var h = await HarnessAsync(latched: true);
            var sha = await h.AddSourceAsync();
            var evidence = await SeedEvidenceAsync(h, sha, o => { o.CommissionedRound = round; o.OrdinaryScopeCompleted = scope; });
            if (admits)
            {
                var queued = await h.RequestAsync(expectedSourceSha: sha, reviewEvidenceId: evidence.Id);
                queued.Status.ShouldBe("queued", row);
                await using var db = h.CreateContext();
                (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId)).ReviewEvidenceId.ShouldBe(evidence.Id, row);
                continue;
            }
            var error = await Should.ThrowAsync<ConflictException>(() => h.RequestAsync(expectedSourceSha: sha, reviewEvidenceId: evidence.Id), row);
            error.Code.ShouldBe(LandApproval.ScopeIneligibleCode, row);
            await AssertNothingAdmittedAsync(h, row);
        }
    }

    [Test]
    public async Task C544_CleanCompletedRequired() =>
        await IdentityRowAsync("found-full", o => o.Outcome = StageOutcomeKind.Found, "review_evidence_ineligible");

    [Test]
    public async Task C544_LandOwnerIdentity() =>
        await IdentityRowAsync("other-owner", o => o.SubjectTaskId = Guid.NewGuid(), "review_evidence_subject_mismatch");

    [Test]
    public async Task C544_LandShaIdentity() =>
        await IdentityRowAsync("other-sha", o => o.ReviewedSourceSha = new string('e', 40), "review_evidence_sha_mismatch");

    [Test]
    public async Task C544_LandRefIdentity() =>
        await IdentityRowAsync("other-ref", o => o.ReviewedSourceRef = "refs/heads/other", "review_evidence_ref_mismatch");

    [Test]
    public async Task C544_LandRepositoryIdentity() =>
        await IdentityRowAsync("other-repository", o => o.ReviewedRepositoryPath = Path.Combine(Path.GetTempPath(), "c544-other-repo"),
            "review_evidence_repository_mismatch");

    [Test]
    public async Task C544_SupersededFinal()
    {
        await using var h = await HarnessAsync(latched: true);
        var sha = await h.AddSourceAsync();
        var evidence = await SeedEvidenceAsync(h, sha);
        await SupersedeAsync(h, evidence.Id);
        var error = await Should.ThrowAsync<ConflictException>(() => h.RequestAsync(expectedSourceSha: sha, reviewEvidenceId: evidence.Id), "superseded");
        error.Code.ShouldBe("review_evidence_superseded", "superseded");
        await AssertNothingAdmittedAsync(h, "superseded");
    }

    private static async Task IdentityRowAsync(string row, Action<StageOutcome> change, string code)
    {
        await using var h = await HarnessAsync(latched: true);
        var sha = await h.AddSourceAsync();
        var valid = await SeedEvidenceAsync(h, sha);
        var invalid = await SeedEvidenceAsync(h, sha, change);
        var error = await Should.ThrowAsync<ConflictException>(() => h.RequestAsync(expectedSourceSha: sha, reviewEvidenceId: invalid.Id), row);
        error.Code.ShouldBe(code, row);
        await AssertNothingAdmittedAsync(h, row);
        (await h.RequestAsync(expectedSourceSha: sha, reviewEvidenceId: valid.Id)).Status.ShouldBe("queued", row + ": the valid Final/Full control admits");
    }

    [Test]
    public async Task C544_RecoveryRevalidates()
    {
        var checkpoints = new (string Name, LandPhase? Phase)[]
        {
            ("accepted-no-operation", null),
            ("prepared", LandPhase.Prepared),
            ("verified", LandPhase.Verified),
            ("before-target-advance", LandPhase.TargetAdvanceStarted),
            ("before-push", LandPhase.LocalTargetAdvanced),
        };
        var mutations = new (string Name, string Code)[]
        {
            ("evidence-removed", "review_evidence_missing"),
            ("superseded", "review_evidence_superseded"),
            ("scope-invalidated", LandApproval.ScopeIneligibleCode),
            ("owner-newly-latched-no-evidence", LandApproval.FinalReviewRequiredCode),
        };
        foreach (var (checkpoint, phase) in checkpoints)
        foreach (var (mutation, code) in mutations)
        {
            var row = $"{checkpoint} x {mutation}";
            var newlyLatched = mutation == "owner-newly-latched-no-evidence";
            await using var h = await HarnessAsync(latched: !newlyLatched);
            var sha = await h.AddSourceAsync();
            StageOutcome? evidence = newlyLatched ? null : await SeedEvidenceAsync(h, sha);
            (await h.RequestAsync(expectedSourceSha: sha, reviewEvidenceId: evidence?.Id)).Status.ShouldBe("queued", row);
            if (phase is { } cut)
            {
                h.Fault.Phase = cut;
                h.Fault.AfterCommit = true;
                await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync(), row + ": checkpoint reached");
                h.Fault.Phase = null;
                h.Fault.AfterCommit = false;
                (await h.OperationAsync()).ShouldNotBeNull(row).Phase.ShouldBe(cut, row);
            }

            await using (var db = h.CreateContext())
            {
                switch (mutation)
                {
                    case "evidence-removed":
                        await db.StageOutcomes.Where(o => o.Id == evidence!.Id).ExecuteDeleteAsync();
                        break;
                    case "superseded":
                        await SupersedeAsync(h, evidence!.Id);
                        break;
                    case "scope-invalidated":
                        await db.StageOutcomes.Where(o => o.Id == evidence!.Id)
                            .ExecuteUpdateAsync(s => s.SetProperty(o => o.OrdinaryScopeCompleted, VerificationScope.Interim));
                        break;
                    default:
                        await SetLatchAsync(h, true);
                        break;
                }
            }

            var targetBefore = h.Git.TargetHead;
            var remoteBefore = h.Git.RemoteTarget;
            h.Git.Trace.Clear();
            await h.RestartServicesAsync();
            await h.RunAsync();

            h.Git.Trace.ShouldNotContain(a => a[0] == "push", row + ": zero push children");
            h.Git.Trace.ShouldNotContain(a => a.Contains("update-ref") || a.Contains("--ff-only"), row + ": zero target advancement");
            h.Git.TargetHead.ShouldBe(targetBefore, row + ": target unchanged since the checkpoint");
            h.Git.RemoteTarget.ShouldBe(remoteBefore, row + ": remote unchanged");
            var op = await h.OperationAsync();
            if (op is not null)
            {
                new AgentTaskLandingState().HasPublication(op).ShouldBeFalse(row + ": never published");
                op.LastReason.ShouldBe(code, row);
            }
            await using var verify = h.CreateContext();
            var refusal = await verify.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == h.Git.TaskId && e.Type == AgentTaskEventType.LandRefused).SingleAsync();
            refusal.Detail.ShouldContain(code, Case.Sensitive, row);
            (await verify.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.TaskId == h.Git.TaskId)).IsPending.ShouldBeFalse(row + ": terminal");
        }
    }

    [Test]
    public async Task C544_ReplayImmutable()
    {
        await using var h = await HarnessAsync(latched: true);
        var sha = await h.AddSourceAsync();
        var first = await SeedEvidenceAsync(h, sha);
        var second = await SeedEvidenceAsync(h, sha);
        var queued = await h.RequestAsync(filter: "/*/*/Pinned/*", expectedSourceSha: sha, reviewEvidenceId: first.Id);
        // The in-process claim is gone (as after a restart); the durable pending request remains.
        h.Queue.TryDequeue(out _);
        h.Queue.Release(h.Git.TaskId);
        var error = await Should.ThrowAsync<ConflictException>(
            () => h.RequestAsync(filter: "/*/*/Pinned/*", expectedSourceSha: sha, reviewEvidenceId: second.Id), "replay-other-evidence");
        error.Code.ShouldBe("land_request_identity_conflict", "replay-other-evidence");
        await using (var db = h.CreateContext())
        {
            var stored = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == queued.RequestId);
            stored.ReviewEvidenceId.ShouldBe(first.Id, "persisted evidence unchanged");
            stored.ExpectedSourceSha.ShouldBe(sha, "persisted SHA unchanged");
            stored.VerifyFilter.ShouldBe("/*/*/Pinned/*", "persisted filter unchanged");
            (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == h.Git.TaskId)).ShouldBe(1, "no second request");
        }
        (await h.RequestAsync(filter: "/*/*/Pinned/*")).Status.ShouldBe("requeued", "identity-free repost keeps the original approval");
    }

    [Test]
    public async Task C544_PublishedCleanupCompatibility()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Git.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "preserve");
        await h.RunAsync(); // explicit-caller land of an unlatched owner publishes; cleanup is left pending
        var published = (await h.OperationAsync()).ShouldNotBeNull("published");
        new AgentTaskLandingState().HasPublication(published).ShouldBeTrue("published");

        await SetLatchAsync(h, true); // later Interim work latched the owner after publication
        var verifierCalls = h.Verifier.Calls;
        h.Git.Trace.Clear();
        (await h.RequestAsync()).Status.ShouldBe("queued", "cleanup-only retry needs no new Review");
        File.Delete(sentinel);
        await h.RunAsync();

        var after = (await h.OperationAsync()).ShouldNotBeNull("cleanup-retry");
        after.Id.ShouldBe(published.Id, "the original receipt/operation is retained");
        after.Cleanup.ShouldBe(LandCleanupStatus.Complete, "cleanup-retry");
        h.Verifier.Calls.ShouldBe(verifierCalls, "zero new verification");
        h.Git.Trace.ShouldNotContain(a => a[0] == "push", "zero new publication");
        h.Git.Trace.ShouldNotContain(a => a.Contains("rebase"), "zero new preparation");
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static async Task<LandingProtocolHarness> HarnessAsync(bool latched)
    {
        var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await SetLatchAsync(h, latched);
        return h;
    }

    private static async Task SetLatchAsync(LandingProtocolHarness h, bool latched)
    {
        await using var db = h.CreateContext();
        await db.AgentTasks.Where(t => t.Id == h.Git.TaskId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RequiresFinalVerificationReview, latched)
                .SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()));
    }

    private static async Task<StageOutcome> SeedEvidenceAsync(LandingProtocolHarness h, string sha, Action<StageOutcome>? change = null)
    {
        await using var db = h.CreateContext();
        var row = new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Delegate, SubjectTaskId = h.Git.TaskId, StageTaskId = Guid.NewGuid(),
            ReviewedSourceSha = sha, ReviewedSourceRef = h.Git.SourceRef, ReviewedRepositoryPath = h.Git.Repository,
            VerificationProfileVersion = 1, CommissionedRound = VerificationRound.Final,
            OrdinaryScopeCompleted = VerificationScope.Full, Detail = "c544 final full review", RecordedAt = DateTime.UtcNow,
        };
        change?.Invoke(row);
        db.StageOutcomes.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private static async Task SupersedeAsync(LandingProtocolHarness h, Guid evidenceId)
    {
        await using var db = h.CreateContext();
        db.StageOutcomes.Add(new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Found,
            Source = StageOutcomeSource.Orchestrator, SubjectTaskId = h.Git.TaskId, SupersedesId = evidenceId,
            Detail = "override", RecordedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task AssertNothingAdmittedAsync(LandingProtocolHarness h, string row)
    {
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == h.Git.TaskId)).ShouldBe(0, row + ": zero new request");
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0, row + ": zero landing operation");
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Git.TaskId)).LandRequestedAt.ShouldBeNull(row);
        h.Git.OwnedTrace.ShouldBeEmpty(row + ": zero publication mutations");
        h.Git.Trace.ShouldNotContain(a => a[0] == "push", row);
    }

    private static async Task WaitForLockWaitAsync(string connectionString)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", connection);
            if ((long)(await command.ExecuteScalarAsync())! > 0)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
        throw new TimeoutException("The competing admission never reached the owner row lock.");
    }

    /// <summary>Pauses a matching SaveChanges (inside its transaction, after the row lock) until released.</summary>
    private sealed class SaveBarrier(Func<Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker, bool> predicate) : SaveChangesInterceptor
    {
        public bool Armed { get; set; } = true;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && predicate(data.Context!.ChangeTracker) && Interlocked.CompareExchange(ref _fired, 1, 0) == 0)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(120), ct);
            }
            return result;
        }
    }
}
