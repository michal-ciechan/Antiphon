using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1475: the screen-only fallback's DeliveryUnverified copy. Every harness session starts with
/// zero transcript rows (the unobservable first-prompt baseline). When no matching complete
/// UserPrompt arrives inside the verification window, the incident and degraded receipt describe
/// that bounded observation; they must not claim verification is impossible or that a redraw
/// proves delivery. The guards pin what the copy change must not move: a complete first prompt
/// still confirms by transcript with no warning, and a still-unproved channel delivery keeps its
/// Critical severity, 10-minute dedup and no-kill/no-retype behaviour.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
[Category("Slow")]
public class DeliveryUnverifiedDiagnosticTests
{
    private static Task<BridgeQueueHarness> CreateHarnessAsync() =>
        ClaudeDialogFixture.CreateBridgeAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            ClockSpeed = 5,
        });

    [Test]
    public async Task Missing_receipt_copy_is_temporal_and_allows_later_confirmation()
    {
        await using var h = await CreateHarnessAsync();
        h.Adapter.OnSubmitted = _ => Task.CompletedTask; // no UserPrompt row: screen fallback
        const string body = "CARD1475 first brief body long enough for identity";

        var dto = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.Now, CancellationToken.None);

        var receipt = dto.LastDelivery.ShouldNotBeNull();
        receipt.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Screen);
        receipt.Degraded.ShouldBeTrue();
        var reason = receipt.Reason.ShouldNotBeNull();
        reason.ShouldNotContain("no transcript bound");
        reason.ShouldContain("verification window");
        reason.ShouldContain("later matching UserPrompt");

        await using var db = BridgeQueueHarness.CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified);
        incident.Message.ShouldNotContain("nothing can verify");
        incident.Message.ShouldNotContain("probably landed");
        incident.Message.ShouldNotContain("no transcript bound");
        incident.Message.ShouldContain("verification window");
        incident.Message.ShouldContain("Enter(s)");
        incident.Message.ShouldContain("later matching UserPrompt");
        incident.Message.ShouldContain("redraw alone does not prove");
    }

    [Test]
    public async Task Complete_first_prompt_uses_transcript_and_emits_no_warning()
    {
        // The harness default stamps a complete matching UserPrompt on submit.
        await using var h = await CreateHarnessAsync();

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "CARD1475 complete first prompt body long enough", MessageSendMode.Now,
            CancellationToken.None);

        var receipt = dto.LastDelivery.ShouldNotBeNull();
        receipt.Verdict.ShouldBe("Delivered");
        receipt.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Transcript);
        receipt.Degraded.ShouldBeFalse();
        receipt.Reason.ShouldBeNull();
        await using var db = BridgeQueueHarness.CreateContext();
        (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified)
                .Select(i => i.Message)
                .ToListAsync())
            .ShouldBeEmpty();
    }

    [Test]
    public async Task Still_unproved_channel_delivery_keeps_critical_dedup_and_no_retype()
    {
        await using var h = await CreateHarnessAsync();
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;
        await h.BindChannelAsync();
        const string first = "CARD1475 channel first body long enough for identity";
        const string second = "CARD1475 channel second body long enough for identity";

        var a = await h.Queue.EnqueueAsync(h.SessionId, first, MessageSendMode.Now, CancellationToken.None);
        var b = await h.Queue.EnqueueAsync(h.SessionId, second, MessageSendMode.Now, CancellationToken.None);

        a.LastDelivery.ShouldNotBeNull().ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Screen);
        b.LastDelivery.ShouldNotBeNull().ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Screen);
        h.Adapter.Inputs.Count(i => i == first).ShouldBe(1, "a screen-only verdict never re-types");
        h.Adapter.Inputs.Count(i => i == second).ShouldBe(1, "a screen-only verdict never re-types");
        h.Adapter.Killed.ShouldBeFalse();

        await using var db = BridgeQueueHarness.CreateContext();
        var incidents = await db.AgentIncidents
            .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified)
            .ToListAsync();
        incidents.Count.ShouldBe(1, "two attempts inside the 10-minute window -> one incident");
        incidents[0].Severity.ShouldBe(AlertSeverity.Critical);
        incidents[0].SessionId.ShouldBe(h.SessionId);
    }
}
