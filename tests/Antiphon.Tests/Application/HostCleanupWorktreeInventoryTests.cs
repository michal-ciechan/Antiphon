using System.Collections.Immutable;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class HostCleanupWorktreeInventoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-01T12:00:00Z");

    private static HostCleanupWorktreeInventory Inventory() => new(
        new WorktreeIgnoredContentClassifier(Options.Create(new WorktreeCleanupSettings())),
        new HostCleanupClock { UtcNow = Now });

    private static HostCleanupWorktreeFacts Facts(string path = "/work/worktrees/task-1") =>
        new(path, "runner-work", "generation-1", Guid.Parse("11111111-1111-1111-1111-111111111111"),
            HostCleanupWorktreeKind.Registered, 1024, Now.AddHours(-25),
            false, false, false, false, false, true, true, false, true, true,
            ImmutableArray<string>.Empty, true, "CARD-0692");

    [Test]
    public void Unpushed_commit_is_never_eligible()
    {
        var row = Inventory().Classify(Facts() with { IsPushed = false });
        row.Disposition.ShouldBe(HostCleanupWorktreeDisposition.Keep,
            "C826.Unpushed_commit_is_never_eligible:keep");
        row.Reason.ShouldBe("unpushed", "C826.Unpushed_commit_is_never_eligible:reason");
    }

    [Test]
    public void Dirty_or_active_worktree_is_kept()
    {
        var cases = new (HostCleanupWorktreeFacts Facts, string Reason)[]
        {
            (Facts() with { IsDirty = true }, "dirty_source"),
            (Facts() with { HasUntrackedSource = true }, "dirty_source"),
            (Facts() with { IsTaskActive = true }, "task_active"),
            (Facts() with { HasLiveSession = true }, "session_live"),
            (Facts() with { HasPendingLandOrRecovery = true }, "land_or_recovery_pending")
        };
        foreach (var (facts, reason) in cases)
            Inventory().Classify(facts).Reason.ShouldBe(reason,
                "C826.Dirty_or_active_worktree_is_kept");
    }

    [Test]
    public void Classifier_preserves_guarded_classes_and_release_requirements()
    {
        var inventory = Inventory();
        var protectedFacts = Facts() with
        { IgnoredPaths = ["server/bin-c826/.claude/session.jsonl", "server/bin-c826/output.dll"] };
        var protectedRow = inventory.Classify(protectedFacts);
        protectedRow.Reason.ShouldBe("protected_content",
            "C826.Classifier_preserves_guarded_classes_and_release_requirements:protected");
        protectedRow.Content.Protected.ShouldContain("server/bin-c826/.claude/session.jsonl");
        inventory.Classify(Facts() with { HasRelease = false }).Reason.ShouldBe("release_required",
            "C826.Classifier_preserves_guarded_classes_and_release_requirements:release");
        inventory.Classify(Facts() with { EvidencePreserved = false }).Reason.ShouldBe("evidence_required",
            "C826.Classifier_preserves_guarded_classes_and_release_requirements:evidence");
        inventory.Classify(Facts() with { IsContainedInPushedBranch = null }).Disposition.ShouldBe(
            HostCleanupWorktreeDisposition.Unknown,
            "C826.Classifier_preserves_guarded_classes_and_release_requirements:reachability");
    }

    [Test]
    public async Task Registered_and_unregistered_candidates_are_fully_accounted()
    {
        var probe = new PagedProbe(
            new HostCleanupWorktreePage([Facts()], "next", true),
            new HostCleanupWorktreePage([Facts("/work/worktrees/orphan") with
                { Kind = HostCleanupWorktreeKind.Unregistered }], null, true));
        var result = await Inventory().ReadAsync("host-a", probe, new OwnerProbe());
        result.Complete.ShouldBeTrue("C826.Registered_and_unregistered_candidates_are_fully_accounted:complete");
        result.Rows.Count.ShouldBe(2,
            "C826.Registered_and_unregistered_candidates_are_fully_accounted:count");
        result.Rows[1].Disposition.ShouldBe(HostCleanupWorktreeDisposition.Unknown,
            "C826.Registered_and_unregistered_candidates_are_fully_accounted:orphan");
        probe.Calls.ShouldBe(2);
    }

    [Test]
    public async Task Unavailable_owner_is_reported_without_retry_or_delete()
    {
        var owner = new OwnerProbe { Fail = true };
        var probe = new PagedProbe(new HostCleanupWorktreePage([Facts(), Facts("/work/worktrees/task-2")], null, true));
        var result = await Inventory().ReadAsync("host-a", probe, owner);
        owner.Calls.ShouldBe(1,
            "C826.Unavailable_owner_is_reported_without_retry_or_delete:one-read");
        result.Rows.ShouldAllBe(row => row.OwnerAvailabilityReason == "owner_unavailable");
    }

    [Test]
    public async Task Refusing_owner_is_reported_without_retry_or_delete()
    {
        var owner = new OwnerProbe { Reason = "owner_busy" };
        var probe = new PagedProbe(new HostCleanupWorktreePage([Facts(), Facts("/work/worktrees/task-2")], null, true));
        var result = await Inventory().ReadAsync("host-a", probe, owner);
        owner.Calls.ShouldBe(1,
            "C826.Refusing_owner_is_reported_without_retry_or_delete:one-read");
        result.Rows.ShouldAllBe(row => row.OwnerAvailabilityReason == "owner_busy");
    }

    private sealed class PagedProbe(params HostCleanupWorktreePage[] pages) : IHostCleanupWorktreeProbe
    {
        public int Calls { get; private set; }
        public Task<HostCleanupWorktreePage> ReadPageAsync(string hostId, string? cursor,
            int take, CancellationToken cancellationToken)
        {
            var index = Calls++;
            return Task.FromResult(pages[index]);
        }
    }

    private sealed class OwnerProbe : IHostCleanupExistingOwnerStatusProbe
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public string? Reason { get; set; }
        public Task<string?> ReadAvailabilityReasonAsync(string owner, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail) throw new IOException("virtual owner unavailable");
            return Task.FromResult(Reason);
        }
    }
}
