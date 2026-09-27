using Antiphon.Messaging;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>Server-owned durable byte snapshots for outbound preparation.</summary>
public interface IChannelOutboundFileStore
{
    Task<ChannelOutboundSnapshot> StageAsync(Guid deliveryId, ChannelReply reply,
        CancellationToken ct, string? sourceManifestJson = null);
    Task<ChannelReply> ReadReplyAsync(string path, string expectedSha256, CancellationToken ct);
    Task<ChannelOutboundSealed> ValidateAndSealAsync(Guid deliveryId, string replyPath,
        string replySha256, int maxMessageBytes, CancellationToken ct);
}

public sealed record ChannelOutboundSnapshot(string ReplyPath, string ReplySha256,
    string RequestPath, string OutputDirectory);

public sealed record ChannelOutboundSealed(string ReplyPath, string ReplySha256, string Outcome);
