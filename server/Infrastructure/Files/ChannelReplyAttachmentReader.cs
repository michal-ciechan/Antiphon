using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Files;

public sealed class ChannelReplyAttachmentReader : IChannelReplyAttachmentReader
{
    public Task<byte[]> ReadAttachmentAsync(string path, CancellationToken ct) =>
        File.ReadAllBytesAsync(path, ct);

    public Task<string> ReadTextAsync(string path, CancellationToken ct) =>
        File.ReadAllTextAsync(path, ct);
}
