namespace Antiphon.Server.Application.Services;

/// <summary>Process-local scheduling hints shared by successive pump scopes, never ownership.</summary>
public sealed class ChannelOutboundWorkCursor
{
    internal sealed record Position(DateTime CreatedAt, Guid Id);
    internal Position? Send { get; set; }
    internal Position? Repair { get; set; }
}
