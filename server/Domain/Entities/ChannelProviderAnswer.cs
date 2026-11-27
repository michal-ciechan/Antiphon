namespace Antiphon.Server.Domain.Entities;

/// <summary>Gateway provider acceptance can arrive before this server accepts the inbound.</summary>
public sealed class ChannelProviderAnswer
{
    public string Provider { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string NativeMessageId { get; set; } = "";
    public DateTime AnsweredAt { get; set; }
}
