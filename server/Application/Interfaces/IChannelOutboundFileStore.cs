using Antiphon.Messaging;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>Server-owned durable byte snapshots for outbound preparation.</summary>
public interface IChannelOutboundFileStore
{
    Task<ChannelOutboundSnapshot> StageAsync(Guid deliveryId, ChannelReply reply,
        CancellationToken ct, string? sourceManifestJson = null);
    Task<ChannelReply> ReadReplyAsync(string path, string expectedSha256, CancellationToken ct);
    Task<ChannelOutboundSealed> ValidateAndSealAsync(Guid deliveryId, string replyPath,
        string replySha256, int maxMessageBytes, CancellationToken ct);
    Task<ChannelOutboundMaterialized?> TryAdoptAsync(Guid deliveryId, string captureJson, CancellationToken ct) =>
        throw new NotSupportedException("Capture-aware staging is required.");
    Task<ChannelOutboundMaterialized> StageCapturedAsync(Guid deliveryId, string captureJson,
        ChannelReplyPrepared prepared, CancellationToken ct) => throw new NotSupportedException("Capture-aware staging is required.");
}

public sealed record ChannelOutboundMaterialized(ChannelOutboundSnapshot Snapshot,
    string PromptText, string PromptRevision, string? SourceManifestJson)
{
    public IReadOnlyList<ChannelReplyBundleSnapshot> Bundles { get; init; } = [];
}

public sealed record ChannelOutboundSnapshot(string ReplyPath, string ReplySha256,
    string RequestPath, string OutputDirectory);

public sealed record ChannelOutboundSealed(string ReplyPath, string ReplySha256, string Outcome);
