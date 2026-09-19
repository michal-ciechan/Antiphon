namespace Antiphon.Server.Application.Interfaces;

/// <summary>Retirement command-slot I/O. A spent slot never resets on restart.</summary>
public interface IRetirementCommandJournal
{
    Task<bool> TryCommitIntentAsync(Guid attemptId, Guid commandId, CancellationToken ct);

    Task RecordComponentAsync(Guid attemptId, bool? directory, bool? registration, bool? branch, string? residue, CancellationToken ct);
}
