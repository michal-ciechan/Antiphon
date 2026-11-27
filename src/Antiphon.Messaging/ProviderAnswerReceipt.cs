namespace Antiphon.Messaging;

/// <summary>Native identity of an inbound answered by an accepted provider send.</summary>
public sealed record ProviderAnswerReceipt
{
    public required string Channel { get; init; }
    public required string ConversationId { get; init; }
    public required string ChannelMessageId { get; init; }
    public required DateTimeOffset AnsweredAt { get; init; }

    public static string Topic(string outboundTopic) => outboundTopic + ".provider-acceptances";
}
