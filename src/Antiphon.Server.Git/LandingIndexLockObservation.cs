namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-0543. A probe failure sets <see cref="Reason"/>; the caller treats that as held.</summary>
public sealed record LandingIndexLockObservation(
    string Path,
    bool Present,
    DateTime? LastWriteUtc,
    long? Length,
    IReadOnlyList<(int Pid, DateTime? StartUtc)> CandidateHolders,
    string? Reason);
