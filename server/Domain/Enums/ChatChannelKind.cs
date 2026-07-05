namespace Antiphon.Server.Domain.Enums;

/// <summary>Shape of an external conversation. Mirrors <c>Antiphon.Messaging.ConversationKind</c>.</summary>
public enum ChatChannelKind
{
    Direct = 0,
    Group = 1,
    Broadcast = 2,
}
