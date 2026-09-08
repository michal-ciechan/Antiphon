namespace Antiphon.Server.Application.Interfaces;

/// <summary>Durable process-start intent and identity for a verifier's owned children.</summary>
public interface ILandingChildObserver
{
    Task BeforeStartAsync(CancellationToken ct);
    Task StartedAsync(int processId, long startTicks, CancellationToken ct);
    Task ExitedAsync(CancellationToken ct);
}
