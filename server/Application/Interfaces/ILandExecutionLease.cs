namespace Antiphon.Server.Application.Interfaces;

/// <summary>Excludes other lands for the entire operation. Acquire before the mutation lease.</summary>
public interface ILandExecutionLease
{
    Task<IAsyncDisposable?> TryAcquireAsync(string repository, CancellationToken ct);
}
