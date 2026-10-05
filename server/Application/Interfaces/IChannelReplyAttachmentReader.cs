namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Fallible source reads performed only after durable outbound capture. The opened regular
/// file must report exactly one link; hard-linked artifacts must first be copied to a new file.
/// This handle-time check does not prove inode origin or prevent subsequent local writes.
/// </summary>
public interface IChannelReplyAttachmentReader
{
    Task<byte[]> ReadAttachmentAsync(string path, IReadOnlyList<string> allowedRoots, long maxBytes, CancellationToken ct);
    Task<string> ReadTextAsync(string path, IReadOnlyList<string> allowedRoots, long maxBytes, CancellationToken ct);
}

public sealed class ChannelReplyFileTooLargeException(long length) : IOException("The source file exceeds its read budget.")
{
    public long Length { get; } = length;
}
