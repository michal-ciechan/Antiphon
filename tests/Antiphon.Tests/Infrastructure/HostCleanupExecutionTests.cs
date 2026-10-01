using Antiphon.HostCleanup;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupExecutionTests
{
    private static CleanupCandidateFacts Register(HostCleanupFixture fixture,
        string path = HostCleanupFixture.CheckpointPath, long bytes = 10)
    {
        var candidate = fixture.Candidate(path, entries:
            [HostCleanupFixture.File(path + "/payload", fixture.Clock.UtcNow.AddHours(-25), bytes, bytes)])
            with { Identity = new CleanupIdentity("storage-a", path, "generation-1") };
        fixture.FileSystem.Facts[path] = candidate;
        return candidate;
    }

    [Test]
    public async Task Dry_run_has_zero_mutating_calls()
    {
        var f = new HostCleanupFixture();
        var plan = f.Plan(Register(f));
        await f.Executor().ExecuteAsync(plan, dryRun: true);
        (f.FileSystem.DeleteCalls + f.PlanStore.Calls + f.ClaimStore.Claims +
            f.ClaimStore.Completions).ShouldBe(0, "C826.Dry_run_has_zero_mutating_calls");
    }

    [Test]
    public async Task Explicit_plan_file_is_the_only_preview_write()
    {
        var f = new HostCleanupFixture();
        var plan = f.Plan(Register(f));
        var output = new RecordingPreviewOutput();
        await CleanupPreview.WritePlanFileAsync(plan, "/tmp/c826-plan.json", output);
        output.Writes.ShouldBe(1, "C826.Explicit_plan_file_is_the_only_preview_write:outside");
        await Should.ThrowAsync<ArgumentException>(async () =>
            await CleanupPreview.WritePlanFileAsync(plan,
                HostCleanupFixture.CheckpointPath + "/plan.json", output),
            "C826.Explicit_plan_file_is_the_only_preview_write:inside");
        output.Writes.ShouldBe(1, "C826.Explicit_plan_file_is_the_only_preview_write:inside");
    }

    [Test]
    public async Task Plan_is_durable_before_first_delete()
    {
        var f = new HostCleanupFixture();
        f.PlanStore.OnPersist = () => f.FileSystem.Events.Add("plan-commit");
        var plan = f.Plan(Register(f));
        await f.Executor().ExecuteAsync(plan, dryRun: false);
        f.FileSystem.Events.ShouldBe(["plan-commit", "delete"],
            "C826.Plan_is_durable_before_first_delete");
    }

    [Test]
    public async Task Plan_persist_failure_prevents_delete()
    {
        var f = new HostCleanupFixture();
        f.PlanStore.Fail = true;
        var candidate = Register(f);
        var plan = f.Plan(candidate);
        await Should.ThrowAsync<IOException>(async () => await f.Executor().ExecuteAsync(plan, false),
            "C826.Plan_persist_failure_prevents_delete:failed-commit");
        f.FileSystem.DeleteCalls.ShouldBe(0, "C826.Plan_persist_failure_prevents_delete");
        f.FileSystem.Facts.ShouldContainKey(candidate.Path);
    }

    [Test]
    public async Task Failed_attempt_still_consumes_count_cap()
    {
        var f = new HostCleanupFixture();
        f.FileSystem.Partial = true;
        var candidates = Enumerable.Range(0, 11)
            .Select(index => Register(f, "/tmp/c723-" + index.ToString("x32"))).ToArray();
        var receipt = await f.Executor().ExecuteAsync(f.Plan(candidates), false);
        f.FileSystem.DeleteCalls.ShouldBe(10, "C826.Failed_attempt_still_consumes_count_cap:attempts");
        receipt.Outcomes.Single(x => x.Reason == "count_cap").Kind.ShouldBe(CleanupOutcomeKind.Kept,
            "C826.Failed_attempt_still_consumes_count_cap:eleventh");
    }

    [Test]
    public async Task Byte_cap_is_shared_across_families()
    {
        const long sixGiB = 6L * 1024 * 1024 * 1024;
        var f = new HostCleanupFixture();
        var first = Register(f, "/tmp/c723-00000000000000000000000000000001", sixGiB);
        var second = Register(f, "/tmp/c723-00000000000000000000000000000002", sixGiB);
        var receipt = await f.Executor().ExecuteAsync(f.Plan(first, second), false);
        f.FileSystem.DeleteCalls.ShouldBe(1, "C826.Byte_cap_is_shared_across_families:one-delete");
        receipt.Outcomes.Single(x => x.Decision.Path == second.Path).Reason.ShouldBe("byte_cap",
            "C826.Byte_cap_is_shared_across_families:shared-limit");
    }

    [Test]
    public async Task Oversized_root_is_not_truncated()
    {
        var f = new HostCleanupFixture();
        var candidate = Register(f, bytes: 11L * 1024 * 1024 * 1024);
        var receipt = await f.Executor().ExecuteAsync(f.Plan(candidate), false);
        receipt.Outcomes.Single().Reason.ShouldBe("byte_cap", "C826.Oversized_root_is_not_truncated");
        f.FileSystem.DeleteCalls.ShouldBe(0);
        f.FileSystem.Facts.ShouldContainKey(candidate.Path);
    }

    [Test]
    public async Task Unknown_or_overflowed_size_is_kept()
    {
        var f = new HostCleanupFixture();
        var old = f.Clock.UtcNow.AddHours(-25);
        var unknown = f.Candidate(entries:
            [new CleanupEntry(HostCleanupFixture.CheckpointPath + "/unknown", false, false, false,
                true, old, null, null)]);
        f.Planner().Decide(unknown).Disposition.ShouldBe(CleanupDisposition.Unknown,
            "C826.Unknown_or_overflowed_size_is_kept:unknown");
        var overflow = f.Candidate(entries:
            [HostCleanupFixture.File(HostCleanupFixture.CheckpointPath + "/a", old, long.MaxValue, 1),
             HostCleanupFixture.File(HostCleanupFixture.CheckpointPath + "/b", old, 1, 1)]);
        f.Planner().Decide(overflow).Reason.ShouldBe("size_overflow",
            "C826.Unknown_or_overflowed_size_is_kept:overflow");
        await f.Executor().ExecuteAsync(f.Plan(unknown, overflow), false);
        f.FileSystem.DeleteCalls.ShouldBe(0, "C826.Unknown_or_overflowed_size_is_kept:no-delete");
    }

    [Test]
    public void Hardlinks_reserve_conservatively()
    {
        var f = new HostCleanupFixture();
        var old = f.Clock.UtcNow.AddHours(-25);
        var candidate = f.Candidate(entries:
            [HostCleanupFixture.File(HostCleanupFixture.CheckpointPath + "/a", old, 4, 6),
             HostCleanupFixture.File(HostCleanupFixture.CheckpointPath + "/b", old, 4, 6)]);
        f.Planner().Decide(candidate).ReservedBytes.ShouldBe(12,
            "C826.Hardlinks_reserve_conservatively");
    }

    [Test]
    public async Task Changed_file_identity_refuses_execution()
    {
        var f = new HostCleanupFixture();
        var candidate = Register(f);
        var replacement = candidate with
        { Identity = new CleanupIdentity("storage-a", "replacement", "generation-1") };
        f.FileSystem.Facts[candidate.Path] = replacement;
        var receipt = await f.Executor().ExecuteAsync(f.Plan(candidate), false);
        receipt.Outcomes.Single().Reason.ShouldBe("identity_changed",
            "C826.Changed_file_identity_refuses_execution");
        f.FileSystem.DeleteCalls.ShouldBe(0);
    }

    [Test]
    public async Task Revalidation_catches_new_write()
    {
        var f = new HostCleanupFixture();
        var candidate = Register(f);
        f.FileSystem.BeforeObserve = () => f.FileSystem.Facts[candidate.Path] = candidate with
        { Entries = [HostCleanupFixture.File(candidate.Path + "/new", f.Clock.UtcNow, 1, 1)] };
        var receipt = await f.Executor().ExecuteAsync(f.Plan(candidate), false);
        receipt.Outcomes.Single().Reason.ShouldBe("recent_write",
            "C826.Revalidation_catches_new_write");
        f.FileSystem.DeleteCalls.ShouldBe(0);
    }

    [Test]
    public async Task Revalidation_catches_new_deny_or_hold()
    {
        var f = new HostCleanupFixture();
        var candidate = Register(f);
        f.FileSystem.BeforeObserve = () => f.Holds.Add(new CleanupHold("storage-a", candidate.Path,
            "generation-1", "operator", "operator", f.Clock.UtcNow, f.Clock.UtcNow.AddDays(1), 1));
        var receipt = await f.Executor().ExecuteAsync(f.Plan(candidate), false);
        receipt.Outcomes.Single().Reason.ShouldBe("hold_active",
            "C826.Revalidation_catches_new_deny_or_hold");
        f.FileSystem.DeleteCalls.ShouldBe(0);
    }

    [Test]
    public async Task Revalidation_catches_live_owner_or_revoked_release()
    {
        var f = new HostCleanupFixture();
        var candidate = Register(f);
        f.FileSystem.BeforeObserve = () => f.FileSystem.Facts[candidate.Path] = candidate with
        { Owner = candidate.Owner! with { Liveness = OwnerLiveness.Alive } };
        var receipt = await f.Executor().ExecuteAsync(f.Plan(candidate), false);
        receipt.Outcomes.Single().Reason.ShouldBe("owner_live",
            "C826.Revalidation_catches_live_owner_or_revoked_release");
        f.FileSystem.DeleteCalls.ShouldBe(0);
    }

    [Test]
    public async Task Active_hold_preserves_owned_scratch()
    {
        var f = new HostCleanupFixture();
        var candidate = Register(f);
        f.Holds.Add(new CleanupHold("storage-a", candidate.Path, "generation-1", "operator",
            "operator", f.Clock.UtcNow, f.Clock.UtcNow.AddDays(1), 1));
        var receipt = await f.Executor().ExecuteAsync(f.Plan(candidate), false);
        receipt.Outcomes.Single().Reason.ShouldBe("hold_active",
            "C826.Active_hold_preserves_owned_scratch");
        f.FileSystem.DeleteCalls.ShouldBe(0);
    }

    [Test]
    public async Task Expired_hold_requires_visible_disposition()
    {
        var f = new HostCleanupFixture();
        var candidate = Register(f);
        foreach (var prefix in new[] { "19e7f181", "28fc3bab", "7647cb6a", "7ebf8aa2",
            "89719e41", "c03af147", "e7c20aaa" })
            f.Holds.Add(new CleanupHold("storage-a", candidate.Path, "generation-1", prefix,
                "operator", f.Clock.UtcNow.AddDays(-10), f.Clock.UtcNow.AddDays(-1), 1));
        var receipt = await f.Executor().ExecuteAsync(f.Plan(candidate), false);
        f.Holds.Count.ShouldBe(7, "C826.Expired_hold_requires_visible_disposition:retained");
        receipt.Outcomes.Single().Reason.ShouldBe("hold_expired_review_required",
            "C826.Expired_hold_requires_visible_disposition:reason");
        f.FileSystem.DeleteCalls.ShouldBe(0);
    }

    [Test]
    public async Task Partial_failure_keeps_custody_and_budget()
    {
        var f = new HostCleanupFixture();
        var candidate = Register(f);
        f.FileSystem.Partial = true;
        var plan = f.Plan(candidate);
        var first = await f.Executor().ExecuteAsync(plan, false);
        var second = await f.Executor().ExecuteAsync(plan, false);
        first.Outcomes.Single().Kind.ShouldBe(CleanupOutcomeKind.Partial,
            "C826.Partial_failure_keeps_custody_and_budget:partial");
        second.Outcomes.Single().ReceiptId.ShouldBe(first.Outcomes.Single().ReceiptId,
            "C826.Partial_failure_keeps_custody_and_budget:receipt");
        f.FileSystem.Facts.ShouldContainKey(candidate.Path);
        f.FileSystem.DeleteCalls.ShouldBe(1);
    }

    [Test]
    public async Task Duplicate_request_returns_same_receipt()
    {
        var f = new HostCleanupFixture();
        var candidate = Register(f);
        var plan = f.Plan(candidate);
        var first = await f.Executor().ExecuteAsync(plan, false);
        var second = await f.Executor().ExecuteAsync(plan, false);
        second.Outcomes.Single().ReceiptId.ShouldBe(first.Outcomes.Single().ReceiptId,
            "C826.Duplicate_request_returns_same_receipt");
        f.FileSystem.DeleteCalls.ShouldBe(1);
    }

    [Test]
    public async Task Concurrent_parent_removal_is_zero_reclaimed()
    {
        var f = new HostCleanupFixture();
        var candidate = Register(f);
        f.FileSystem.Facts.Remove(candidate.Path);
        var receipt = await f.Executor().ExecuteAsync(f.Plan(candidate), false);
        receipt.Outcomes.Single().Kind.ShouldBe(CleanupOutcomeKind.AlreadyAbsent,
            "C826.Concurrent_parent_removal_is_zero_reclaimed:outcome");
        receipt.Outcomes.Single().ReclaimedBytes.ShouldBe(0,
            "C826.Concurrent_parent_removal_is_zero_reclaimed:bytes");
    }

    [Test]
    public void Cursor_is_stable_without_starving_old_roots()
    {
        var f = new HostCleanupFixture { Limits = new CleanupLimits(MaxCandidates: 1) };
        var a = f.Candidate("/tmp/c723-00000000000000000000000000000001", entries: []);
        var b = f.Candidate("/tmp/c723-00000000000000000000000000000002", entries: []);
        var c = f.Candidate("/tmp/c723-00000000000000000000000000000003", entries: []);
        var first = f.Planner().Plan(Guid.NewGuid(), "storage-a", [a, b]);
        var second = f.Planner().Plan(Guid.NewGuid(), "storage-a", [a, b, c], first.NextCursor);
        second.Decisions.First().Path.ShouldBe(b.Path,
            "C826.Cursor_is_stable_without_starving_old_roots");
    }

    [Test]
    public async Task Eligible_scratch_is_removed_and_measured()
    {
        var f = new HostCleanupFixture();
        var candidate = Register(f, bytes: 10);
        f.FileSystem.ReclaimedBytes = 6;
        var receipt = await f.Executor().ExecuteAsync(f.Plan(candidate), false);
        receipt.Outcomes.Single().ReclaimedBytes.ShouldBe(6,
            "C826.Eligible_scratch_is_removed_and_measured:measured");
        f.FileSystem.Facts.ShouldNotContainKey(candidate.Path,
            "C826.Eligible_scratch_is_removed_and_measured:removed");
    }

    private sealed class RecordingPreviewOutput : ICleanupPreviewOutput
    {
        public int Writes { get; private set; }
        public ValueTask WriteAsync(string path, CleanupPlan plan, CancellationToken cancellationToken)
        {
            Writes++;
            return ValueTask.CompletedTask;
        }
    }
}
