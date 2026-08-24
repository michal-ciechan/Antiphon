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
    IReadOnlyDictionary<string, string> Options,
    /// <summary>Optional CARD-0106 ApiKeys name (<c>tracker.token_key</c>).</summary>
    string? TokenKeyName = null,
    /// <summary>
    /// Resolved bearer token populated by <c>TrackerTokenResolver</c> before tracker calls.
    /// Never serialized, never logged.
    /// </summary>
    string? ResolvedToken = null);

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

/// <summary>CARD-0166: write + comment-pull capabilities. Implemented by GitHub only.</summary>
public interface IBidirectionalIssueTracker : IIssueTracker
{
    Task<IReadOnlyList<TrackedIssueComment>> FetchCommentsSinceAsync(
        IssueTrackerConfig config,
        DateTime? since,
        CancellationToken ct);

    Task<TrackedIssueComment> PostCommentAsync(
        IssueTrackerConfig config,
        string externalId,
        string body,
        CancellationToken ct);

    Task AddLabelsAsync(
        IssueTrackerConfig config,
        string externalId,
        IReadOnlyList<string> labels,
        CancellationToken ct);

    Task RemoveLabelAsync(
        IssueTrackerConfig config,
        string externalId,
        string label,
        CancellationToken ct);

    Task ReplaceLabelsAsync(
        IssueTrackerConfig config,
        string externalId,
        IReadOnlyList<string> labels,
        CancellationToken ct);

    Task SetStateAsync(
        IssueTrackerConfig config,
        string externalId,
        string state,
        string? stateReason,
        CancellationToken ct);

    Task<TrackedIssue> CreateIssueAsync(
        IssueTrackerConfig config,
        string title,
        string body,
        IReadOnlyList<string> labels,
        CancellationToken ct);

    Task UpdateIssueContentAsync(
        IssueTrackerConfig config,
        string externalId,
        string title,
        string body,
        CancellationToken ct);
}

public sealed record TrackedIssueComment(
    string ExternalCommentId,
    string IssueExternalId,
    string Author,
    string Body,
    string Url,
    DateTime CreatedAt,
    DateTime UpdatedAt);
