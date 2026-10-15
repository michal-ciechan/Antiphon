using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

public sealed record ImportedCheckpointEvidence(string Identity, string ValidationJson, string[] MissingRows);

public interface ICheckpointReceiptValidator
{
    Task<ImportedCheckpointEvidence> ImportAsync(ReviewEvidenceCorrectionRequest request, CancellationToken ct);
}
