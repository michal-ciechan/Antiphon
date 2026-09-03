namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Adopt an existing runner session instead of spawning one (CARD-0340). Runner adapters
/// implement this; in-process Pty adapters do not, and that is the "not resumable" signal.
/// </summary>
public interface IAttachableProtocolAdapter
{
    Task AttachAsync(Guid sessionId, CancellationToken ct);
}
