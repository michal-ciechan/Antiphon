using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1150 S2 repair 7 (Review 52d6dd7f): the structural guard for F11. The resumed launch
/// treats its bookkeeping as best-effort only after a completed delivery. Each table lists every
/// value of one delivery result type with its classification, so a value added later fails here
/// until someone decides whether it is a completed delivery.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    public void C1150_Resume_input_rule_classifies_every_delivery_result()
    {
        var flush = new Dictionary<SessionMessageQueueService.FlushResult, bool>
        {
            [SessionMessageQueueService.FlushResult.Nothing] = false,
            [SessionMessageQueueService.FlushResult.Delivered] = true,
            [SessionMessageQueueService.FlushResult.Failed] = false,
            // Transcript evidence of an earlier attempt, not a delivery completed by this resume.
            [SessionMessageQueueService.FlushResult.LateConfirmed] = false,
        };
        var verdicts = new Dictionary<DeliveryVerdict, bool>
        {
            [DeliveryVerdict.Delivered] = true,
            [DeliveryVerdict.NoComposerEvidence] = false,
            [DeliveryVerdict.NoSubmitOutput] = false,
            [DeliveryVerdict.NoTranscriptRecord] = false,
            [DeliveryVerdict.Truncated] = false,
            [DeliveryVerdict.ForbiddenBody] = false,
            [DeliveryVerdict.LocalCommandNotAccepted] = false,
            [DeliveryVerdict.BackendUnreachable] = false,
            [DeliveryVerdict.LateConfirmed] = false,
            [DeliveryVerdict.ModalBlocked] = false,
            [DeliveryVerdict.SpillBodyMissing] = false,
        };
        var local = new Dictionary<SessionMessageQueueService.LocalCommandTypeResult, bool>
        {
            [SessionMessageQueueService.LocalCommandTypeResult.NotAccepted] = false,
            [SessionMessageQueueService.LocalCommandTypeResult.NotAdvanced] = false,
            [SessionMessageQueueService.LocalCommandTypeResult.Sent] = true,
        };

        Enum.GetValues<SessionMessageQueueService.FlushResult>().ShouldBe(flush.Keys, ignoreOrder: true,
            "F11: classify every flush result as a completed delivery or not");
        Enum.GetValues<DeliveryVerdict>().ShouldBe(verdicts.Keys, ignoreOrder: true,
            "F11: classify every delivery verdict as a completed delivery or not");
        Enum.GetValues<SessionMessageQueueService.LocalCommandTypeResult>().ShouldBe(local.Keys, ignoreOrder: true,
            "F11: classify every local-command result as a completed delivery or not");
        foreach (var (result, completed) in flush)
            SessionMessageQueueService.CompletedInput(result).ShouldBe(completed, $"F11 flush result {result}");
        foreach (var (verdict, completed) in verdicts)
            SessionMessageQueueService.CompletedDelivery(verdict).ShouldBe(completed, $"F11 verdict {verdict}");
        foreach (var (typed, completed) in local)
            SessionMessageQueueService.CompletedLocalCommand(typed).ShouldBe(completed, $"F11 local command {typed}");
    }
}
