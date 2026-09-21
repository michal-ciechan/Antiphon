namespace Antiphon.Server.Application.Services;

/// <summary>
/// Strict same-conversation resume for one compaction episode. Callers pass the
/// episode id; the implementation refuses Fresh and a second launch.
/// </summary>
public interface ICompactionContinuationResume
{
    Task<CompactionResumeResult> ResumeAsync(Guid episodeId, CancellationToken ct);
}

public sealed record CompactionResumeResult(
    bool Accepted,
    Guid? SessionId,
    DateTime? AcceptedStartedAt,
    string Outcome);
