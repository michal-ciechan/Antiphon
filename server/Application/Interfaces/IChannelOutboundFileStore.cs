using Antiphon.Messaging;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>CARD-0418: durable frozen inputs and sealed outputs, outside removable task worktrees.</summary>
public interface IChannelOutboundFileStore
{
    string Root { get; }

    Task StageInputAsync(Guid deliveryId, ChannelOutboundStagedInput input, CancellationToken ct);

    Task<ChannelOutboundStagedInput> ReadInputAsync(Guid deliveryId, CancellationToken ct);

    Task SealOutputAsync(Guid deliveryId, ChannelReply payload, CancellationToken ct);

    Task<ChannelReply?> ReadSealedPayloadAsync(Guid deliveryId, CancellationToken ct);

    Task WriteWorkerRequestAsync(Guid deliveryId, string json, CancellationToken ct);

    Task<string> WorkerDirectoryAsync(Guid deliveryId, CancellationToken ct);

    string InputDirectory(Guid deliveryId);

    string OutputDirectory(Guid deliveryId);
}

public sealed class ChannelOutboundStagedInput
{
    public required string FrozenReplyJson { get; init; }
    public required string InputHash { get; init; }
    public required string RequestJson { get; init; }
    public IReadOnlyList<ChannelOutboundStagedFile> Files { get; init; } = [];
}

public sealed class ChannelOutboundStagedFile
{
    public required string SafeName { get; init; }
    public required string Mime { get; init; }
    public required long Length { get; init; }
    public required string Sha256 { get; init; }
    public required byte[] Bytes { get; init; }
}