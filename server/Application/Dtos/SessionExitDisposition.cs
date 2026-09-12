namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-0502: how <c>ObserveExitAsync</c> treated an exit envelope.</summary>
public enum SessionExitDisposition
{
    Applied = 0,
    Stale = 1,
    Unknown = 2,
    Missing = 3,
    PersistenceFailed = 4,
}
