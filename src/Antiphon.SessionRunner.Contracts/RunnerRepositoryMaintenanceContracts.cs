namespace Antiphon.SessionRunner.Contracts;

/// <summary>CARD-1817. One repository in a runner maintenance pass.</summary>
public sealed record RunnerRepositoryMaintenanceReport(
    string Repository,
    string Outcome,
    string? Reason,
    int LooseBefore,
    int LooseAfter,
    int PacksBefore,
    int PacksAfter);

/// <summary>
/// CARD-1817. The last runner repository maintenance pass. Null on a runner status means
/// this process has not finished a pass yet.
/// </summary>
public sealed record RunnerRepositoryMaintenanceStatus(
    DateTimeOffset? AtUtc,
    IReadOnlyList<RunnerRepositoryMaintenanceReport> Repositories);
