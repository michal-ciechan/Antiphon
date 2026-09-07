using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0330 S3 — Shadow/Apply pipeline, gates, skip reasons, no recursion.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class OutputDistillationTests
{
    [Test]
    public async Task shadow_mode_records_without_replacing_the_note()
    {
        using var h = new OutputDistillationHarness(s => s.OutputDistillerMode = OutputDistillerMode.Shadow);
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync();

        var run = Task.Run(() => h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None));
        var distill = await h.WaitForDistillAsync(specialist.Id);
        await h.SettleDistillAsync(distill.Id, h.PassingDistillation());
        await h.PumpClockAsync(run);
        await run;

        var queued = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        queued.Body.ShouldBe(seed.RawBody);
        queued.HoldUntil.ShouldBeNull();
        queued.ContentDigest.ShouldBe(seed.Digest);
        queued.NoteHeader.ShouldBe(seed.Header);

        var source = await h.ReloadTaskAsync(seed.Task.Id);
        source.DistilledResult.ShouldNotBeNullOrWhiteSpace();
        source.Result.ShouldBe(seed.Report);

        var ledger = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DistillationOutcome.Shadowed);
        ledger.Mode.ShouldBe(OutputDistillerMode.Shadow);
    }

    [Test]
    public async Task apply_mode_replaces_a_held_pending_body_and_keeps_header_and_digest()
    {
        using var h = new OutputDistillationHarness(s => s.OutputDistillerMode = OutputDistillerMode.Apply);
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync(holdUntil: DateTime.UtcNow.AddMinutes(5));

        var run = Task.Run(() => h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None));
        var distill = await h.WaitForDistillAsync(specialist.Id);
        var distilled = h.PassingDistillation();
        await h.SettleDistillAsync(distill.Id, distilled);
        await h.PumpClockAsync(run);
        await run;

        var queued = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        queued.HoldUntil.ShouldBeNull();
        queued.ContentDigest.ShouldBe(seed.Digest);
        queued.NoteHeader.ShouldBe(seed.Header);
        queued.Body.ShouldStartWith(seed.Header);
        queued.Body.ShouldContain(distilled.Trim());
        queued.Body.ShouldContain("Full report:");
        queued.Body.ShouldNotBe(seed.RawBody);

        var ledger = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DistillationOutcome.Applied);
    }

    [Test]
    public async Task a_row_already_delivered_records_applied_late()
    {
        using var h = new OutputDistillationHarness(s => s.OutputDistillerMode = OutputDistillerMode.Apply);
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync();
        await h.MarkQueuedSentAsync(seed.QueuedMessageId);

        var run = Task.Run(() => h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None));
        var distill = await h.WaitForDistillAsync(specialist.Id);
        await h.SettleDistillAsync(distill.Id, h.PassingDistillation());
        await h.PumpClockAsync(run);
        await run;

        var queued = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        queued.Body.ShouldBe(seed.RawBody);
        (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem()
            .Outcome.ShouldBe(DistillationOutcome.AppliedLate);
    }

    [Test]
    public async Task a_rejected_distillation_delivers_the_raw_body_and_records_missing_anchors()
    {
        using var h = new OutputDistillationHarness(s => s.OutputDistillerMode = OutputDistillerMode.Apply);
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync(holdUntil: DateTime.UtcNow.AddMinutes(5));

        var run = Task.Run(() => h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None));
        var distill = await h.WaitForDistillAsync(specialist.Id);
        await h.SettleDistillAsync(distill.Id, Pad("omitted every identifier on purpose"));
        await h.PumpClockAsync(run);
        await run;

        var queued = await h.ReloadQueuedAsync(seed.QueuedMessageId);
        queued.Body.ShouldBe(seed.RawBody);
        queued.HoldUntil.ShouldBeNull();
        var ledger = (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem();
        ledger.Outcome.ShouldBe(DistillationOutcome.RejectedOverCompressed);
        ledger.MissingAnchors.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task timeout_cancels_a_still_queued_distill_task_and_clears_the_hold()
    {
        using var h = new OutputDistillationHarness(s =>
        {
            s.OutputDistillerMode = OutputDistillerMode.Apply;
            s.OutputDistillerWaitSeconds = 1;
        });
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedSourceAsync(holdUntil: DateTime.UtcNow.AddMinutes(5));

        var run = Task.Run(() => h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None));
        var distill = await h.WaitForDistillAsync(specialist.Id);
        await h.PumpClockAsync(run);
        await run;

        var distillRow = await h.ReloadTaskAsync(distill.Id);
        distillRow.Status.ShouldBe(AgentTaskStatus.Canceled);
        (await h.ReloadQueuedAsync(seed.QueuedMessageId)).HoldUntil.ShouldBeNull();
        (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem()
            .Outcome.ShouldBe(DistillationOutcome.DegradedExpired);
        var incidents = await h.IncidentsAsync(specialist.Id);
        incidents.ShouldBeEmpty("pre-execution expiry does not raise a generic provider incident");
    }

    [Test]
    public async Task backlog_at_the_cap_degrades_without_creating()
    {
        using var h = new OutputDistillationHarness(s => s.OutputDistillerMaxBacklog = 1);
        var specialist = await h.EnsureSpecialistAsync();
        await h.SeedQueuedDistillAsync(specialist.Id);
        var seed = await h.SeedSourceAsync();

        await h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None);

        (await h.DistillCountAsync(specialist.Id)).ShouldBe(1, "the cap forbids a second Distill row");
        (await h.LedgerAsync(seed.Task.Id)).ShouldHaveSingleItem()
            .Outcome.ShouldBe(DistillationOutcome.DegradedBusy);
    }

    [Test]
    public async Task disabled_writes_no_ledger_row()
    {
        using var h = new OutputDistillationHarness(s => s.OutputDistillerEnabled = false);
        var seed = await h.SeedSourceAsync();

        await h.Distiller.RequestAsync(seed.Task.Id, seed.QueuedMessageId, CancellationToken.None);

        (await h.LedgerAsync(seed.Task.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task short_and_long_reports_are_skipped_with_reasons()
    {
        using var h = new OutputDistillationHarness();
        var shortSeed = await h.SeedSourceAsync(report: new string('a', 200));
        await h.Distiller.RequestAsync(shortSeed.Task.Id, shortSeed.QueuedMessageId, CancellationToken.None);
        (await h.LedgerAsync(shortSeed.Task.Id)).ShouldHaveSingleItem()
            .Outcome.ShouldBe(DistillationOutcome.SkippedShort);

        var longSeed = await h.SeedSourceAsync(report: new string('b', 21_000));
        await h.Distiller.RequestAsync(longSeed.Task.Id, longSeed.QueuedMessageId, CancellationToken.None);
        (await h.LedgerAsync(longSeed.Task.Id)).ShouldHaveSingleItem()
            .Outcome.ShouldBe(DistillationOutcome.SkippedLong);
    }

    [Test]
    public async Task a_blocked_task_is_never_requested()
    {
        var task = new AgentTask
        {
            Status = AgentTaskStatus.Blocked,
            ReplyTo = AgentTaskReplyTo.Session,
            Role = AgentTaskRole.Code,
        };
        OutputDistillationService.ShouldRequest(task, new DelegationSettings()).ShouldBeFalse();
    }

    [Test]
    public async Task a_specialist_row_is_never_requested()
    {
        var task = new AgentTask
        {
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.Session,
            Role = AgentTaskRole.Distill,
        };
        OutputDistillationService.ShouldRequest(task, new DelegationSettings()).ShouldBeFalse();
    }

    [Test]
    public async Task the_unavailable_incident_dedups_per_minute()
    {
        using var h = new OutputDistillationHarness();
        var specialist = await h.EnsureSpecialistAsync();
        using var db = OutputDistillationHarness.CreateContext();
        var runner = new SpecialistTaskRunner(db, h.Clock, NullLogger.Instance);
        var spec = OutputDistillerProvisioner.Spec(h.Settings);
        await runner.RaiseUnavailableAsync(spec, specialist, "synthetic unavailable", CancellationToken.None);
        await runner.RaiseUnavailableAsync(spec, specialist, "synthetic unavailable", CancellationToken.None);
        (await h.IncidentsAsync(specialist.Id)).Count.ShouldBe(1);
    }

    private static string Pad(string text)
    {
        if (text.Length >= 120)
            return text;
        return text + "\n- " + new string('x', 120 - text.Length);
    }

}
