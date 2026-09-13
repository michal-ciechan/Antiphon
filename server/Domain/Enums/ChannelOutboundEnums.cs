namespace Antiphon.Server.Domain.Enums;

/// <summary>CARD-0418: when an opted-in channel invokes its conversion agent.</summary>
public enum ChannelOutboundTrigger
{
    MarkdownSources = 0,
    EveryAgentReply = 1,
}

/// <summary>Which dispatcher/control path produced the frozen reply.</summary>
public enum ChannelOutboundSendKind
{
    Main = 0,
    Trailing = 1,
    Machine = 2,
    Control = 3,
}

/// <summary>Agent turn versus server-composed control traffic.</summary>
public enum ChannelOutboundOrigin
{
    AgentReply = 0,
    Control = 1,
}

/// <summary>
/// Durable preparation states. Never treat Held, Failed or PublishUncertain as Published.
/// Numeric values are append-only.
/// </summary>
public enum ChannelOutboundDeliveryState
{
    Pending = 0,
    Converting = 1,
    Ready = 2,
    Publishing = 3,
    Published = 4,
    Held = 5,
    PublishUncertain = 6,
    Failed = 7,
}