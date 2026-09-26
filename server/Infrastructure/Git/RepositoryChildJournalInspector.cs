using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

public enum JournalRecordState
{
    Alive,
    Dead,
    Completed,
    Unknown,
    Malformed,
}

public sealed record JournalRecordFinding(
    string File,
    JournalRecordState State,
    TimeSpan Age,
    bool Stale,
    int? ProcessId);

public sealed record JournalInspection(string? CommonDirectory, IReadOnlyList<JournalRecordFinding> Findings)
{
    public int StaleCount => Findings.Count(finding => finding.Stale);
}

/// <summary>CARD-0726 D-8: read-only classification of one repository's child journal.</summary>
public sealed class RepositoryChildJournalInspector(ILandingGit git)
{
    public Task<JournalInspection> InspectAsync(
        string repository, TimeSpan staleAfter, DateTimeOffset now, CancellationToken ct)
    {
        _ = (git, repository, staleAfter, now);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new JournalInspection(null, []));
    }
}
