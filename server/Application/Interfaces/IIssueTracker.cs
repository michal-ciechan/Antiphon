using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

public interface IIssueTracker
{
    TrackerKind Kind { get; }

    Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(IssueTrackerConfig config, CancellationToken ct);

    Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
        IssueTrackerConfig config,
        IReadOnlyList<string> states,
        CancellationToken ct);

    Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
        IssueTrackerConfig config,
        IReadOnlyList<string> externalIds,
        CancellationToken ct);
}

public sealed record IssueTrackerConfig(
    TrackerKind Kind,
    string BaseUrl,
    string? ProjectKey,
    string? Repository,
    IReadOnlyList<string> ActiveStates,
    string? ApiKeyEnv,
    string? Jql,
    IReadOnlyDictionary<string, string> Options);

public sealed record TrackedIssue(
    string ExternalId,
    string ExternalKey,
    string Title,
    string Description,
    string State,
    int Priority,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> BlockedByExternalIds,
    string Url,
    string RawPayloadJson);
