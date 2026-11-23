namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// One-shot <c>claude -p</c> (review engine design 7.2): one prompt on stdin, one JSON result on stdout,
/// on the local Claude Code sign-in (never <c>--bare</c>, which disables it). The implementation is
/// <c>Infrastructure/Review/ClaudeCliProcess</c>; the seam lets tests answer without a process.
/// </summary>
public interface IClaudeCli
{
    /// <summary>
    /// Runs <c>claude -p</c> once. Throws <see cref="ClaudeCliException"/> for a non-zero exit, an
    /// <c>is_error</c> result, unparseable output or the request's timeout (the process tree is killed),
    /// and <see cref="OperationCanceledException"/> when <paramref name="ct"/> is cancelled (also killed).
    /// </summary>
    Task<ClaudeCliResult> RunAsync(ClaudeCliRequest request, CancellationToken ct);
}

/// <summary>
/// One call. <see cref="Tools"/> are the built-in tools the model may use (sent as both <c>--tools</c> and
/// <c>--allowedTools</c>); empty means none. The prompt goes on stdin and is never logged.
/// </summary>
public sealed record ClaudeCliRequest(string Prompt, string Model, IReadOnlyList<string> Tools, string WorkingDirectory, TimeSpan Timeout);

/// <summary>
/// The answer: the final message text, the model that served it (the main <c>modelUsage</c> key), input
/// tokens (fresh plus cache read and cache creation), output tokens, and <c>total_cost_usd</c>, which on a
/// subscription is an API-equivalent estimate.
/// </summary>
public sealed record ClaudeCliResult(string Text, string Model, long InputTokens, long OutputTokens, decimal CostUsd);

/// <summary>A <c>claude -p</c> call failed. The message carries the CLI's error text, never the prompt.</summary>
public sealed class ClaudeCliException(string message, bool timedOut = false) : Exception(message)
{
    /// <summary>The call ran past its timeout and the process tree was killed.</summary>
    public bool TimedOut { get; } = timedOut;
}

/// <summary>
/// A throwaway checkout of a reviewed head for a one-shot reviewer: a detached git worktree of a local
/// repository, fetched from origin's <c>refs/pull/&lt;n&gt;/head</c> first when the commit is missing.
/// </summary>
public interface IReviewCheckout
{
    /// <summary>
    /// Cuts a detached worktree of <paramref name="repoPath"/> at <paramref name="headSha"/> into
    /// <paramref name="directory"/> (which must not exist yet). Throws <see cref="ReviewAgentRunnerException"/>
    /// when the commit cannot be made reachable or the worktree cannot be added.
    /// </summary>
    Task AddAsync(string repoPath, int? prNumber, string headSha, string directory, CancellationToken ct);

    /// <summary>Removes the worktree and prunes; never throws (a failure is logged).</summary>
    Task RemoveAsync(string repoPath, string directory, CancellationToken ct);
}
