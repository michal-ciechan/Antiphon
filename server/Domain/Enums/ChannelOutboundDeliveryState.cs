namespace Antiphon.Server.Domain.Enums;

public enum ChannelOutboundDeliveryState
{
    Pending,
    Converting,
    Ready,
    Publishing,
    Published,
    Held,
    PublishUncertain,
    Failed,
}
