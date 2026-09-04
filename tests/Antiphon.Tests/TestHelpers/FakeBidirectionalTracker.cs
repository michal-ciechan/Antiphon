using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Shared write-recording fake for bidirectional tracker tests. Extracted from
/// <c>TrackerBidirectionalSyncTests</c> so CARD-0347's per-card push suite can reuse it.
/// </summary>
internal sealed class FakeBidirectionalTracker(TrackerKind kind) : IBidirectionalIssueTracker
{
    public TrackerKind Kind { get; } = kind;
    public IReadOnlyList<TrackedIssue> Candidates { get; set; } = [];
    public IReadOnlyList<TrackedIssueComment> CommentsSince { get; set; } = [];
    public bool EchoPostedComments { get; set; }
    public bool ThrowOnPostComment { get; set; }
    public bool HangOnSetState { get; set; }
    public List<(string ExternalId, string Body)> PostCommentCalls { get; } = [];
    public List<(string ExternalId, string State, string? StateReason)> SetStateCalls { get; } = [];
    public List<(string Title, string Body, IReadOnlyList<string> Labels)> CreateIssueCalls { get; } = [];
    public int AddLabelCalls { get; private set; }
    public int RemoveLabelCalls { get; private set; }
    public int ReplaceLabelCalls { get; private set; }
    public int UpdateContentCalls { get; private set; }
    public int FetchByIdsCalls { get; private set; }
    public int WriteCallCount =>
        PostCommentCalls.Count + SetStateCalls.Count + CreateIssueCalls.Count
        + AddLabelCalls + RemoveLabelCalls + ReplaceLabelCalls + UpdateContentCalls;

    private int _nextCommentId = 1000;
    private int _nextIssueNumber = 200;

    public void ClearWriteCounters()
    {
        PostCommentCalls.Clear();
        SetStateCalls.Clear();
        CreateIssueCalls.Clear();
        AddLabelCalls = 0;
        RemoveLabelCalls = 0;
        ReplaceLabelCalls = 0;
        UpdateContentCalls = 0;
    }

    public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(IssueTrackerConfig config, CancellationToken ct) =>
        Task.FromResult(Candidates);

    public Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
        IssueTrackerConfig config, IReadOnlyList<string> states, CancellationToken ct) =>
        Task.FromResult(Candidates);

    public Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
        IssueTrackerConfig config, IReadOnlyList<string> externalIds, CancellationToken ct)
    {
        FetchByIdsCalls++;
        return Task.FromResult<IReadOnlyList<TrackedIssue>>(
            Candidates.Where(i => externalIds.Contains(i.ExternalId)).ToList());
    }

    public Task<IReadOnlyList<TrackedIssueComment>> FetchCommentsSinceAsync(
        IssueTrackerConfig config, DateTime? since, CancellationToken ct) =>
        Task.FromResult(CommentsSince);

    public Task<TrackedIssueComment> PostCommentAsync(
        IssueTrackerConfig config, string externalId, string body, CancellationToken ct)
    {
        if (ThrowOnPostComment)
            throw new InvalidOperationException("simulated post failure");

        PostCommentCalls.Add((externalId, body));
        var id = (++_nextCommentId).ToString();
        var comment = new TrackedIssueComment(
            id, externalId, "sync-bot", body,
            $"https://github.test/{externalId}#issuecomment-{id}",
            DateTime.UtcNow, DateTime.UtcNow);
        if (EchoPostedComments)
            CommentsSince = CommentsSince.Append(comment).ToList();
        return Task.FromResult(comment);
    }

    public Task AddLabelsAsync(
        IssueTrackerConfig config, string externalId, IReadOnlyList<string> labels, CancellationToken ct)
    {
        AddLabelCalls++;
        Candidates = Candidates.Select(i =>
            i.ExternalId == externalId
                ? i with { Labels = i.Labels.Concat(labels).Distinct(StringComparer.OrdinalIgnoreCase).ToList() }
                : i).ToList();
        return Task.CompletedTask;
    }

    public Task RemoveLabelAsync(
        IssueTrackerConfig config, string externalId, string label, CancellationToken ct)
    {
        RemoveLabelCalls++;
        Candidates = Candidates.Select(i =>
            i.ExternalId == externalId
                ? i with { Labels = i.Labels.Where(l => !string.Equals(l, label, StringComparison.OrdinalIgnoreCase)).ToList() }
                : i).ToList();
        return Task.CompletedTask;
    }

    public Task ReplaceLabelsAsync(
        IssueTrackerConfig config, string externalId, IReadOnlyList<string> labels, CancellationToken ct)
    {
        ReplaceLabelCalls++;
        Candidates = Candidates.Select(i =>
            i.ExternalId == externalId ? i with { Labels = labels } : i).ToList();
        return Task.CompletedTask;
    }

    public async Task SetStateAsync(
        IssueTrackerConfig config, string externalId, string state, string? stateReason, CancellationToken ct)
    {
        if (HangOnSetState)
            await Task.Delay(Timeout.Infinite, ct);

        SetStateCalls.Add((externalId, state, stateReason));
        Candidates = Candidates.Select(i =>
            i.ExternalId == externalId ? i with { State = state } : i).ToList();
    }

    public Task<TrackedIssue> CreateIssueAsync(
        IssueTrackerConfig config, string title, string body, IReadOnlyList<string> labels, CancellationToken ct)
    {
        CreateIssueCalls.Add((title, body, labels));
        var n = ++_nextIssueNumber;
        var issue = new TrackedIssue(
            $"acme/app#{n}", $"#{n}", title, body, "open", 0, labels, [],
            $"https://github.test/acme/app/issues/{n}", "{}");
        Candidates = Candidates.Append(issue).ToList();
        return Task.FromResult(issue);
    }

    public Task UpdateIssueContentAsync(
        IssueTrackerConfig config, string externalId, string title, string body, CancellationToken ct)
    {
        UpdateContentCalls++;
        return Task.CompletedTask;
    }
}
