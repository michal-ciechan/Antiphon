using Antiphon.HostCleanup;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class HostCleanupOwnershipTests
{
    [Test]
    public void Unknown_owner_prevents_delete()
    {
        var f = new HostCleanupFixture();
        var candidate = f.Candidate() with { Owner = null, HasValidMarker = false, IsRegistered = false };
        f.Planner().Decide(candidate).Reason.ShouldBe("owner_unknown",
            "C826.Unknown_owner_prevents_delete");
        f.FileSystem.DeleteCalls.ShouldBe(0);
    }

    [Test]
    public void Live_owner_prevents_delete()
    {
        var f = new HostCleanupFixture();
        var candidate = f.Candidate(owner: f.DeadReleasedOwner() with { Liveness = OwnerLiveness.Alive });
        f.Planner().Decide(candidate).Reason.ShouldBe("owner_live",
            "C826.Live_owner_prevents_delete");
    }

    [Test]
    public void Reused_pid_prevents_delete()
    {
        var f = new HostCleanupFixture();
        var candidate = f.Candidate(owner: f.DeadReleasedOwner() with { Liveness = OwnerLiveness.Reused });
        f.Planner().Decide(candidate).Reason.ShouldBe("owner_reused_pid",
            "C826.Reused_pid_prevents_delete");
    }

    [Test]
    public void Unknown_liveness_prevents_delete()
    {
        var f = new HostCleanupFixture();
        var candidate = f.Candidate(owner: f.DeadReleasedOwner() with { Liveness = OwnerLiveness.Unknown });
        f.Planner().Decide(candidate).Reason.ShouldBe("owner_liveness_unknown",
            "C826.Unknown_liveness_prevents_delete");
    }

    [Test]
    public void Foreign_host_boot_or_namespace_prevents_delete()
    {
        var f = new HostCleanupFixture();
        foreach (var owner in new[]
        {
            f.DeadReleasedOwner() with { HostBootId = "foreign-boot" },
            f.DeadReleasedOwner() with { PidNamespace = "foreign-namespace" }
        })
            f.Planner().Decide(f.Candidate(owner: owner)).Reason.ShouldBe("owner_identity_foreign",
                "C826.Foreign_host_boot_or_namespace_prevents_delete");
    }

    [Test]
    public void Dead_parent_with_live_executor_is_kept()
    {
        var f = new HostCleanupFixture();
        var owner = f.DeadReleasedOwner() with
        { HasNestedExecutor = true, NestedExecutorLiveness = OwnerLiveness.Alive };
        f.Planner().Decide(f.Candidate(owner: owner)).Reason.ShouldBe("executor_live",
            "C826.Dead_parent_with_live_executor_is_kept");
        f.Planner().Decide(f.Candidate(owner: owner with { NestedExecutorLiveness = null })).Reason
            .ShouldBe("executor_liveness_unknown",
                "C826.Dead_parent_with_live_executor_is_kept:unknown");
    }

    [Test]
    public void Missing_nested_custody_is_kept()
    {
        var f = new HostCleanupFixture();
        var owner = f.DeadReleasedOwner() with { NestedCustodyComplete = false };
        f.Planner().Decide(f.Candidate(owner: owner)).Reason.ShouldBe("nested_custody_missing",
            "C826.Missing_nested_custody_is_kept");
    }

    [Test]
    public void Evidence_receipt_is_required_for_release()
    {
        var f = new HostCleanupFixture();
        var owner = f.DeadReleasedOwner() with { EvidencePreserved = false };
        f.Planner().Decide(f.Candidate(owner: owner)).Reason.ShouldBe("evidence_required",
            "C826.Evidence_receipt_is_required_for_release");
    }

    [Test]
    public async Task Owner_generation_change_refuses_claim()
    {
        var f = new HostCleanupFixture();
        var candidate = f.Candidate() with
        { Identity = new CleanupIdentity("storage-a", "file-1", "generation-1") };
        f.FileSystem.Facts[candidate.Path] = candidate with
        { Identity = new CleanupIdentity("storage-a", "file-1", "generation-2") };
        var receipt = await f.Executor().ExecuteAsync(f.Plan(candidate), false);
        receipt.Outcomes.Single().Reason.ShouldBe("identity_changed",
            "C826.Owner_generation_change_refuses_claim");
        f.FileSystem.DeleteCalls.ShouldBe(0);
    }

    [Test]
    public async Task Competing_lifecycle_and_daily_calls_share_claim()
    {
        var f = new HostCleanupFixture();
        var candidate = f.Candidate();
        f.FileSystem.Facts[candidate.Path] = candidate;
        var daily = f.Plan(candidate);
        var lifecycle = daily with { RunId = Guid.NewGuid() };
        var first = await f.Executor().ExecuteAsync(daily, false);
        var second = await f.Executor().ExecuteAsync(lifecycle, false);
        f.FileSystem.DeleteCalls.ShouldBe(1,
            "C826.Competing_lifecycle_and_daily_calls_share_claim:one-delete");
        second.Outcomes.Single().ReceiptId.ShouldBe(first.Outcomes.Single().ReceiptId,
            "C826.Competing_lifecycle_and_daily_calls_share_claim:receipt");
    }

    [Test]
    public void Slot_binding_blocks_daily_ownership()
    {
        var f = new HostCleanupFixture();
        foreach (var state in new[] { "active", "idle", "quarantined" })
        {
            var owner = f.DeadReleasedOwner() with { SlotBound = true };
            f.Planner().Decide(f.Candidate(owner: owner)).Reason.ShouldBe("slot_owned",
                $"C826.Slot_binding_blocks_daily_ownership:{state}");
        }
    }

    [Test]
    public void Released_dead_owner_allows_scratch()
    {
        var f = new HostCleanupFixture();
        f.Planner().Decide(f.Candidate()).Disposition.ShouldBe(CleanupDisposition.Eligible,
            "C826.Released_dead_owner_allows_scratch");
    }
}
