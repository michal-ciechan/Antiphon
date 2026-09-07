using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class OutputDistillationAdmissionTests
{
    [Test]
    [Arguments("other-alias")]
    [Arguments("other-kind")]
    [Arguments("expired")]
    [Arguments("removed")]
    public async Task Only_a_current_hold_on_the_pinned_alias_blocks_work(string scenario)
    {
        using var h = new OutputDistillationHarness();
        var seat = await h.EnsureSpecialistAsync();
        var hold = new ModelAvailabilityHold { Id = Guid.NewGuid(),
            Kind = scenario == "other-kind" ? AgentKind.Grok : AgentKind.ClaudeCode,
            ModelAlias = scenario == "other-alias" ? "opus" : "haiku",
            Source = ModelAvailabilitySource.Manual, HitAt = h.Clock.GetUtcNow().UtcDateTime,
            DisabledUntil = scenario == "expired" ? h.Clock.GetUtcNow().AddSeconds(-1).UtcDateTime : null };
        await using var db = OutputDistillationHarness.CreateContext();
        db.ModelAvailabilityHolds.Add(hold);
        await db.SaveChangesAsync();
        using var stop = new CancellationTokenSource();
        Task? pending = null;
        try
        {
            if (scenario == "removed") await db.ModelAvailabilityHolds.Where(x => x.Id == hold.Id).ExecuteDeleteAsync();
            var seed = await h.SeedSourceAsync();
            pending = h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, stop.Token);
            var run = await h.WaitForDistillAsync(seat.Id);
            await h.SettleDistillAsync(run.Id, h.PassingDistillation());
            await h.PumpClockAsync(pending);
            var ledger = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
            ledger.Outcome.ShouldBe(DistillationOutcome.Shadowed);
            ledger.AvailabilityAlias.ShouldBe("haiku");
        }
        finally
        {
            stop.Cancel();
            if (pending is not null) { try { await pending; } catch (OperationCanceledException) { } }
            await db.ModelAvailabilityHolds.Where(x => x.Id == hold.Id).ExecuteDeleteAsync();
        }
    }

    [Test]
    [Arguments(false, "haiku", false)]
    [Arguments(false, "*", true)]
    [Arguments(true, "haiku", false)]
    [Arguments(true, "claude-haiku-4-5-20251001", true)]
    public async Task Held_preflight_never_creates_work(bool existing, string model, bool timed)
    {
        using var h = new OutputDistillationHarness();
        if (existing)
        {
            var seat = await h.EnsureSpecialistAsync();
            await using var edit = OutputDistillationHarness.CreateContext();
            await edit.Agents.Where(a => a.Id == seat.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.ModelId, model));
        }
        var hold = new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(), Kind = AgentKind.ClaudeCode,
            ModelAlias = model == "*" ? "*" : "haiku", Source = ModelAvailabilitySource.Manual,
            HitAt = h.Clock.GetUtcNow().UtcDateTime,
            DisabledUntil = timed ? h.Clock.GetUtcNow().AddMinutes(1).UtcDateTime : null,
        };
        await using var db = OutputDistillationHarness.CreateContext();
        db.ModelAvailabilityHolds.Add(hold);
        await db.SaveChangesAsync();
        try
        {
            var seed = await h.SeedSourceAsync(holdUntil: h.Clock.GetUtcNow().AddSeconds(45).UtcDateTime);
            await h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None);
            var ledger = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
            ledger.Outcome.ShouldBe(DistillationOutcome.DegradedHeld);
            ledger.AvailabilityAlias.ShouldBe("haiku");
            ledger.AvailabilityKind.ShouldBe(AgentKind.ClaudeCode);
            ledger.DistillTaskId.ShouldBeNull();
            (await h.ReloadQueuedAsync(seed.QueuedMessageId)).HoldUntil.ShouldBeNull();
            (await db.Agents.CountAsync(a => a.Slug == h.SpecialistSlug)).ShouldBe(existing ? 1 : 0);
            if (!existing) Directory.Exists(Path.Combine(h.Scratch, ".claude")).ShouldBeFalse();
            var ids = await db.Agents.Where(a => a.Slug == h.SpecialistSlug).Select(a => a.Id).ToListAsync();
            (await db.AgentIncidents.CountAsync(i => i.AgentId != null && ids.Contains(i.AgentId.Value))).ShouldBe(0);
        }
        finally { await db.ModelAvailabilityHolds.Where(x => x.Id == hold.Id).ExecuteDeleteAsync(); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Availability_error_is_not_a_hold(bool transportCancellation)
    {
        using var h = new OutputDistillationHarness(servicesOverride: s =>
            s.AddScoped<IModelAvailability>(_ => new ThrowingAvailability(transportCancellation)));
        var seed = await h.SeedSourceAsync();
        await h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None);
        var row = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
        row.Outcome.ShouldBe(DistillationOutcome.DegradedUnavailable);
        row.Reason.ShouldBe("availability-check");
        row.DistillTaskId.ShouldBeNull();
    }

    [Test]
    [Arguments("queue-full")]
    [Arguments("worker-unavailable")]
    public async Task Rejected_admission_releases_raw_hold(string reason)
    {
        using var h = new OutputDistillationHarness();
        var now = h.Clock.GetUtcNow();
        var seed = await h.SeedSourceAsync(holdUntil: now.AddSeconds(45).UtcDateTime);
        await h.Distiller.RejectAdmissionAsync(new(seed.Task.Id, seed.QueuedMessageId, now,
            now.AddSeconds(45), OutputDistillerMode.Apply), reason, CancellationToken.None);
        var row = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
        row.Outcome.ShouldBe(DistillationOutcome.DegradedBusy);
        row.Reason.ShouldBe(reason);
        row.DistillTaskId.ShouldBeNull();
        var note = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        note.HoldUntil.ShouldBeNull();
        note.Body.ShouldBe(seed.RawBody);
    }

    private sealed class ThrowingAvailability(bool transportCancellation = false) : IModelAvailability
    {
        public Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct) =>
            throw (transportCancellation ? new OperationCanceledException("synthetic transport timeout")
                : new IOException("synthetic availability failure"));
    }
}
