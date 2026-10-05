namespace Antiphon.Server.Application.Interfaces;

/// <summary>Fallible source reads performed only after durable outbound capture.</summary>
public interface IChannelReplyAttachmentReader
{
    Task<byte[]> ReadAttachmentAsync(string path, CancellationToken ct);
    Task<string> ReadTextAsync(string path, CancellationToken ct);
}
