using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

public sealed class StandingQueueSwitchPolicy
{
    public static bool NeverAttempted(SessionQueuedMessage message) =>
        message.DeliveryAttempts == 0 && message.LastDeliveryStartedAt is null
        && message.LastDeliveryBaselineSequence is null && message.DeliveryVerdict is null
        && message.DeliveryVerdictAt is null && message.SentAt is null && message.CanceledAt is null
        && message.ChannelReplySettledAt is null;
}
