using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Runs one reviewer group (review engine design 4.7): a read-only agent session that reviews its
/// context pack and answers with an ABF <c>review-findings/1</c> document, returned as raw text for
/// the engine to parse. An implementation may finish at once or hand back a
/// <see cref="ReviewAgentResult.Handle"/> (for example an agent task id) that the executor stores
/// on the node execution and polls on its next pass.
/// </summary>
public interface IReviewAgentRunner
{
    /// <summary>
    /// Starts the reviewer, or with <see cref="ReviewAgentRequest.Handle"/> set, asks whether the one
    /// already started has finished. Throws <see cref="ReviewAgentRunnerException"/> when it cannot run.
    /// </summary>
    Task<ReviewAgentResult> RunAsync(ReviewAgentRequest request, CancellationToken ct);
}

/// <summary>
/// One reviewer group's work: its pack (and hash), its assigned checks, and where the change lives.
/// <see cref="Tier"/> is the group's tier (<c>deep</c> or <c>standard</c>); <see cref="LocalPath"/>
/// is the repository of a local-git subject.
/// </summary>
public sealed record ReviewAgentRequest(
    Guid RunId,
    int Iteration,
    int Round,
    string GroupKey,
    string Pack,
    string PackHash,
    IReadOnlyList<string> AssignedChecks,
    string Repo,
    int? PrNumber,
    string HeadSha,
    int Attempt,
    string? Handle = null,
    string? Tier = null,
    ReviewHost Host = ReviewHost.GitHub,
    string? LocalPath = null,
    Guid? NodeExecutionId = null);

public enum ReviewAgentStatus
{
    /// <summary><see cref="ReviewAgentResult.Text"/> holds the reviewer's answer.</summary>
    Completed,

    /// <summary>Still running; poll again with <see cref="ReviewAgentResult.Handle"/>.</summary>
    Pending,
}

public sealed record ReviewAgentResult(ReviewAgentStatus Status, string? Text, string? Handle = null, ReviewAgentUsage? Usage = null)
{
    public static ReviewAgentResult Completed(string text, ReviewAgentUsage? usage = null) => new(ReviewAgentStatus.Completed, text, null, usage);

    /// <summary>Started (or still running) under <paramref name="handle"/>; the executor polls it on a later pass.</summary>
    public static ReviewAgentResult Pending(string handle) => new(ReviewAgentStatus.Pending, null, handle);
}

/// <summary>What the reviewer cost: a seat is <c>api-equivalent</c> (D-20).</summary>
public sealed record ReviewAgentUsage(
    string Provider,
    string? Model,
    ReviewTransport Transport,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd,
    string CostBasis);

public sealed class ReviewAgentRunnerException(string message) : Exception(message);
