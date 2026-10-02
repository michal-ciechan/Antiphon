using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.HostCleanupServerFixture;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class HostCleanupReportTests
{
    private static bool Backlog(IReadOnlyList<AttentionItemDto> items) =>
        items.Any(item => item.Kind == AttentionKind.WorktreeCleanupBacklog);

    [Test]
    public async Task Backlog_opens_on_third_complete_local_day()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        for (var day = 1; day <= 3; day++)
        {
            f.Clock.UtcNow = new DateTimeOffset(2026, 10, day, 12, 0, 0, TimeSpan.Zero);
            await f.IngestAsync(f.Receipt(day));
            Backlog(await f.AttentionAsync()).ShouldBe(day == 3, "C826.third-complete-day");
        }
        var item = (await f.AttentionAsync()).Single(item => item.Kind == AttentionKind.WorktreeCleanupBacklog);
        item.HostCleanupOwner.ShouldBe("CARD-0692", "C826.backlog-owner");
        item.HostCleanupRefusal.ShouldBe("owner_unavailable", "C826.backlog-refusal");
        item.Headline.ShouldContain("20", customMessage: "C826.backlog-bytes");
    }

    [Test]
    public async Task Backlog_counts_one_observation_per_local_day()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        await f.IngestAsync(f.Receipt());
        await f.IngestAsync(f.Receipt(daily: false));
        await f.IngestAsync(f.Receipt(daily: false));
        Backlog(await f.AttentionAsync()).ShouldBeFalse("C826.one-backlog-day");
        await f.IngestAsync(f.Receipt(2));
        await f.IngestAsync(f.Receipt(3));
        f.Clock.UtcNow = f.Clock.UtcNow.AddDays(2);
        Backlog(await f.AttentionAsync()).ShouldBeTrue("C826.three-distinct-days");
    }

    [Test]
    public async Task Missing_day_breaks_backlog_streak()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        foreach (var day in new[] { 1, 3, 4 }) await f.IngestAsync(f.Receipt(day));
        f.Clock.UtcNow = f.Clock.UtcNow.AddDays(3);
        Backlog(await f.AttentionAsync()).ShouldBeFalse("C826.missing-day-breaks-streak");
        await f.IngestAsync(f.Receipt(5));
        f.Clock.UtcNow = f.Clock.UtcNow.AddDays(1);
        Backlog(await f.AttentionAsync()).ShouldBeTrue("C826.streak-restarts-after-gap");
    }

    [Test]
    public async Task Incomplete_inventory_cannot_open_or_clear_backlog()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        await f.IngestAsync(f.Receipt(1)); await f.IngestAsync(f.Receipt(2));
        await f.IngestAsync(f.Receipt(3, complete: false));
        Backlog(await f.AttentionAsync()).ShouldBeFalse("C826.incomplete-cannot-open");
        foreach (var day in new[] { 4, 5, 6 }) await f.IngestAsync(f.Receipt(day));
        await f.IngestAsync(f.Receipt(7, worktreeBytes: 0, complete: false));
        f.Clock.UtcNow = f.Clock.UtcNow.AddDays(6);
        Backlog(await f.AttentionAsync()).ShouldBeTrue("C826.incomplete-cannot-clear");
    }

    [Test]
    public async Task Fresh_below_threshold_inventory_clears_backlog()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        foreach (var day in new[] { 1, 2, 3 }) await f.IngestAsync(f.Receipt(day));
        f.Clock.UtcNow = f.Clock.UtcNow.AddDays(2);
        Backlog(await f.AttentionAsync()).ShouldBeTrue("C826.threshold-opens");
        await f.IngestAsync(f.Receipt(4, worktreeBytes: 19 * GiB));
        f.Clock.UtcNow = f.Clock.UtcNow.AddDays(1);
        Backlog(await f.AttentionAsync()).ShouldBeFalse("C826.fresh-below-threshold-clears");
    }

    [Test]
    public async Task Backlog_window_and_threshold_are_configurable()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        f.Settings.BacklogBytes = 7 * GiB; f.Settings.BacklogDays = 2;
        await f.IngestAsync(f.Receipt(worktreeBytes: 7 * GiB));
        Backlog(await f.AttentionAsync()).ShouldBeFalse("C826.configured-first-day");
        await f.IngestAsync(f.Receipt(2, worktreeBytes: 7 * GiB));
        f.Clock.UtcNow = f.Clock.UtcNow.AddDays(1);
        Backlog(await f.AttentionAsync()).ShouldBeTrue("C826.configured-window-threshold");
    }

    [Test]
    public async Task Reclaimed_bytes_exclude_worktree_backlog()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        var receipt = f.Receipt();
        var scratch = f.Candidate(4096, "/virtual/scratch") with
        { Family = "task-scratch", Worktree = false, Disposition = "eligible", ReservedBytes = 4096,
            Outcome = "removed", ReclaimedBytes = 2048 };
        receipt = receipt with { Candidates = [.. receipt.Candidates, scratch], Attempts = 1, ReservedBytes = 4096 };
        await f.IngestAsync(receipt);
        await using var db = f.Db();
        var page = await new HostCleanupService(db, f.Events, f.Clock).ReadAsync(f.BoardId, receipt.RunId, 0, 50, default);
        page.ShouldNotBeNull("C826.persisted-reclamation");
        page.ReclaimedBytes.ShouldBe(2048, "C826.scratch-only-reclamation");
        page.EligibleWorktreeBytes.ShouldBe(20 * GiB, "C826.separate-worktree-bytes");
    }

    [Test]
    public async Task Receipt_replay_is_idempotent()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        var receipt = f.Receipt();
        (await f.IngestAsync(receipt)).ShouldBe(receipt.RunId, "C826.receipt-persisted");
        (await f.IngestAsync(receipt)).ShouldBe(receipt.RunId, "C826.receipt-replay-id");
        await using var db = f.Db();
        (await db.HostCleanupRuns.CountAsync(r => r.Id == receipt.RunId)).ShouldBe(1, "C826.one-run");
        (await db.HostCleanupCandidates.CountAsync(c => c.RunId == receipt.RunId)).ShouldBe(1, "C826.one-candidate");
        f.Events.Names.Count.ShouldBe(1, "C826.one-invalidation");
    }

    [Test]
    public async Task Changed_receipt_digest_is_refused()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        var receipt = f.Receipt(); await f.IngestAsync(receipt);
        var error = await Should.ThrowAsync<ConflictException>(() =>
            f.IngestAsync(receipt with { FreeBytesAfter = 1 }));
        error.Code.ShouldBe("host_cleanup_receipt_conflict", "C826.receipt-integrity");
        await using var db = f.Db();
        (await db.HostCleanupRuns.SingleAsync(r => r.Id == receipt.RunId)).FreeBytesAfter
            .ShouldBe(receipt.FreeBytesAfter, "C826.original-receipt-retained");
    }

    [Test]
    public async Task Crash_before_invalidation_recovers_attention_delivery()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        var receipt = f.Receipt(); f.Events.Fail = true;
        await f.IngestAsync(receipt);
        await using (var db = f.Db())
        {
            var saved = await db.HostCleanupRuns.SingleOrDefaultAsync(r => r.Id == receipt.RunId);
            saved.ShouldNotBeNull("C826.receipt-survives-publish-failure");
            saved.InvalidationPending.ShouldBeTrue("C826.pending-invalidation-durable");
        }
        f.Events.Fail = false;
        await using (var db = f.Db())
            (await new HostCleanupService(db, f.Events, f.Clock).PublishPendingAsync(default))
                .ShouldBe(1, "C826.invalidation-recovered");
        f.Events.Names.ShouldContain("ScheduleChanged", "C826.mapped-invalidation-event");
        (await f.AttentionAsync()).ShouldContain(item => item.HostCleanupRunId == receipt.RunId,
            "C826.attention-recipient-run");
    }

    [Test]
    public async Task Attention_is_board_scoped_paginated_and_redacted()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        var receipt = f.Receipt() with { Candidates =
            [f.Candidate(path: "/virtual/SECRET-SENTINEL"), f.Candidate(path: "/virtual/second")] };
        await f.IngestAsync(receipt);
        var attention = await f.AttentionAsync();
        attention.Count.ShouldBeGreaterThan(0, "C826.attention-produced");
        attention.ShouldAllBe(item => item.BoardId == f.BoardId);
        attention.ShouldAllBe(item => !item.Evidence.Contains("SECRET-SENTINEL"));
        (await f.AttentionAsync(Guid.NewGuid())).ShouldBeEmpty("C826.foreign-board-hidden");
        await using var db = f.Db(); var service = new HostCleanupService(db, f.Events, f.Clock);
        var first = await service.ReadAsync(f.BoardId, receipt.RunId, 0, 1, default);
        first.ShouldNotBeNull("C826.first-report-page");
        first.Candidates.Count.ShouldBe(1); first.NextOffset.ShouldBe(1);
        var second = await service.ReadAsync(f.BoardId, receipt.RunId, 1, 1, default);
        second.ShouldNotBeNull(); second.NextOffset.ShouldBeNull();
        (await service.ReadAsync(Guid.NewGuid(), receipt.RunId, 0, 1, default)).ShouldBeNull();
    }

    [Test]
    public async Task Partial_receipt_is_not_complete_success()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        var receipt = f.Receipt() with { Candidates = [f.Candidate(4096) with
        { Family = "task-scratch", Worktree = false, Disposition = "eligible", ReservedBytes = 4096,
            Outcome = "partial", ReclaimedBytes = 100 }], Attempts = 1, ReservedBytes = 4096 };
        await f.IngestAsync(receipt);
        await using var db = f.Db();
        var page = await new HostCleanupService(db, f.Events, f.Clock).ReadAsync(f.BoardId, receipt.RunId, 0, 50, default);
        page.ShouldNotBeNull("C826.partial-receipt-persisted");
        page.Complete.ShouldBeFalse("C826.partial-not-complete");
        page.Candidates.Single().Outcome.ShouldBe("partial");
        (await db.HostCleanupRuns.SingleAsync(r => r.Id == receipt.RunId)).ReservedBytes.ShouldBe(4096);
    }

    [Test]
    public async Task Null_sample_never_becomes_zero_usage()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        var receipt = f.Receipt() with { SampleComplete = false, NamespaceAllocatedBytes = null,
            DiskCapacityBytes = null, FreeBytesBefore = null, FreeBytesAfter = null };
        await f.IngestAsync(receipt);
        await using var db = f.Db();
        var page = await new HostCleanupService(db, f.Events, f.Clock).ReadAsync(f.BoardId, receipt.RunId, 0, 50, default);
        page.ShouldNotBeNull("C826.unknown-sample-persisted");
        page.Sample.NamespaceAllocatedBytes.ShouldBeNull("C826.unknown-not-zero");
        page.Sample.FreeBytesAfter.ShouldBeNull();
        page.Sample.SampledAt.ShouldBe(receipt.SampledAt); page.Sample.Complete.ShouldBeFalse();
    }

    [Test]
    public async Task Budget_pressure_has_one_episode_and_fresh_recovery()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        HostCleanupReceiptDto Sample(long bytes, DateTime sampled) => f.Receipt(daily: false) with
        { PlannedAt = f.Clock.UtcNow.UtcDateTime.AddMinutes(-1), FinishedAt = f.Clock.UtcNow.UtcDateTime,
            SampledAt = sampled, NamespaceAllocatedBytes = bytes };
        await f.IngestAsync(Sample(60 * GiB, f.Clock.UtcNow.UtcDateTime));
        var first = (await f.AttentionAsync()).SingleOrDefault(i => i.Kind == AttentionKind.HostCleanupDiskPressure);
        first.ShouldNotBeNull("C826.warning-pressure-episode");
        first.Severity.ShouldBe(AlertSeverity.Warning);
        f.Clock.UtcNow = f.Clock.UtcNow.AddMinutes(1);
        await f.IngestAsync(Sample(90 * GiB, f.Clock.UtcNow.UtcDateTime));
        var critical = (await f.AttentionAsync()).Single(i => i.Kind == AttentionKind.HostCleanupDiskPressure);
        critical.ConditionKey.ShouldBe(first.ConditionKey, "C826.one-pressure-identity");
        critical.Severity.ShouldBe(AlertSeverity.Critical);
        f.Clock.UtcNow = f.Clock.UtcNow.AddMinutes(1);
        await f.IngestAsync(Sample(1 * GiB, f.Clock.UtcNow.UtcDateTime.AddHours(-2)));
        (await f.AttentionAsync()).Single(i => i.Kind == AttentionKind.HostCleanupDiskPressure)
            .Severity.ShouldBe(AlertSeverity.Critical, "C826.stale-good-does-not-clear");
        f.Clock.UtcNow = f.Clock.UtcNow.AddMinutes(1);
        await f.IngestAsync(Sample(1 * GiB, f.Clock.UtcNow.UtcDateTime));
        (await f.AttentionAsync()).ShouldNotContain(i => i.Kind == AttentionKind.HostCleanupDiskPressure,
            "C826.fresh-good-clears-pressure");
    }

    [Test]
    public async Task Missed_host_and_expired_hold_stay_visible()
    {
        await using var f = await HostCleanupServerFixture.CreateAsync();
        var receipt = f.Receipt(); await f.IngestAsync(receipt);
        var holdId = Guid.NewGuid();
        await using (var db = f.Db())
        {
            db.HostCleanupHolds.Add(new HostCleanupHold
            { Id = holdId, BoardId = f.BoardId, HostId = "host-a", StorageId = "storage-a",
                UnresolvedTaskPrefix = "19e7f181", Reason = "SECRET-SENTINEL", Creator = "operator",
                CreatedAt = f.Clock.UtcNow.UtcDateTime.AddDays(-2), ExpiresAt = f.Clock.UtcNow.UtcDateTime.AddDays(-1),
                Revision = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        f.Clock.UtcNow = f.Clock.UtcNow.AddDays(2);
        var items = await f.AttentionAsync();
        items.ShouldContain(i => i.Kind == AttentionKind.HostCleanupHoldExpired &&
            i.HostCleanupHoldExpiryUtc == f.Clock.UtcNow.UtcDateTime.AddDays(-3), "C826.expired-hold-visible");
        items.ShouldContain(i => i.Kind == AttentionKind.HostCleanupSummary &&
            i.Evidence.Contains("report_stale"), "C826.missed-report-visible");
        items.ShouldAllBe(i => !i.Evidence.Contains("SECRET-SENTINEL"));
        await using var verify = f.Db();
        (await verify.HostCleanupHolds.SingleAsync(h => h.Id == holdId)).DisposedAt.ShouldBeNull();
    }
}
