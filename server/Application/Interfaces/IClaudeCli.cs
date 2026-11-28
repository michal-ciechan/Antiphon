namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// One-shot <c>claude -p</c> (review engine design 7.2): one prompt on stdin, one JSON result on stdout,
/// on the local Claude Code sign-in (never <c>--bare</c>, which disables it). The implementation is
/// <c>Infrastructure/Review/ClaudeCliProcess</c>; the seam lets tests answer without a process.
/// </summary>
public interface IClaudeCli
{
    /// <summary>
    /// Runs <c>claude -p</c> once. Throws <see cref="ClaudeCliException"/> for launch or authentication
    /// failures, a non-zero exit, an <c>is_error</c> result, unparseable output or the request's timeout,
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

public enum ClaudeCliFailureKind
{
    Unavailable,
    Start,
    Authentication,
    Exit,
    Timeout,
}

/// <summary>A <c>claude -p</c> call failed. Its message is suitable for the review node summary.</summary>
public sealed class ClaudeCliException(string message, ClaudeCliFailureKind kind = ClaudeCliFailureKind.Unavailable) : Exception(message)
{
    public ClaudeCliException(string message, bool timedOut)
        : this(message, timedOut ? ClaudeCliFailureKind.Timeout : ClaudeCliFailureKind.Unavailable)
    {
    }

    public ClaudeCliFailureKind Kind { get; } = kind;
    public bool TimedOut => Kind == ClaudeCliFailureKind.Timeout;
}

/// <summary>
/// A throwaway checkout of a reviewed head for a one-shot reviewer: a detached git worktree of a local
/// repository, fetched from origin's <c>refs/pull/&lt;n&gt;/head</c> first when the commit is missing.
/// </summary>
public interface IReviewCheckout
{
    /// <summary>
    /// Cuts a detached worktree of <paramref name="repoPath"/> at <paramref name="headSha"/> into
    /// <paramref name="directory"/> (which must not exist yet). With a <paramref name="remote"/>, the fetch
    /// from origin carries the GitHub host's credential, and <see cref="ReviewCheckoutRemote.CreateMirror"/>
    /// creates a missing <paramref name="repoPath"/> as a bare mirror of the repo first. Throws
    /// <see cref="ReviewAgentRunnerException"/> when the commit cannot be made reachable or the worktree
    /// cannot be added.
    /// </summary>
    Task AddAsync(string repoPath, ReviewCheckoutRemote? remote, int? prNumber, string headSha, string directory, CancellationToken ct);

    /// <summary>Removes the worktree and prunes; never throws (a failure is logged).</summary>
    Task RemoveAsync(string repoPath, string directory, CancellationToken ct);
}

/// <summary>
/// The GitHub repo (<c>owner/name</c>) a checkout fetches from. <see cref="CreateMirror"/>: the checkout
/// path is the server's own mirror (<c>Review:Agents:MirrorRoot</c>), created on first use.
/// </summary>
public sealed record ReviewCheckoutRemote(string Repo, bool CreateMirror);

/// <summary>
/// Where git fetches a GitHub repo from (<see cref="WebRoot"/>, such as <c>https://github.com/</c>) and the
/// token it sends there: the App's installation token, else the host's PAT, else none. The token reaches
/// git only through the child process environment, never its arguments, a log or an exception message.
/// </summary>
public sealed record ReviewGitCredential(Uri WebRoot, string? Token)
{
    public override string ToString() => $"{WebRoot} (token {(string.IsNullOrEmpty(Token) ? "none" : "set")})";
}

/// <summary>The git credential for a GitHub repo's code host (<c>Review:CodeHosts</c>).</summary>
public interface IReviewGitCredentials
{
    /// <summary>Throws <see cref="CodeHostException"/> when the repo's host is not a configured GitHub host or no token can be minted.</summary>
    Task<ReviewGitCredential> GetAsync(string repo, CancellationToken ct);
}
