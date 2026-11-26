using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// A <c>git status</c> whose caller tests the listing for emptiness. Every caller now gets libgit2's listing for a
/// clean or dirty tree in git's layout (InProcessGitStatus, 2026-11-25), so the verdict-only mark no longer changes
/// the answer. A caller that also checks Git's standard-error flag still needs the actual command because the
/// in-process listing can omit an unreadable directory silently.
/// Both requests use the ordinary <c>RunAsync</c> call with the original argument list, so overrides
/// see the same command. The mark applies only to that argument list instance.
/// </summary>
public static class GitTreeVerdict
{
    private static readonly AsyncLocal<(IReadOnlyList<string> Arguments, bool RequireStandardError)?> Asked = new();

    /// <summary>Whether this exact argument list was passed by a verdict-only caller.</summary>
    internal static bool IsVerdictOnly(IReadOnlyList<string> arguments) =>
        Asked.Value is { } asked && !asked.RequireStandardError && ReferenceEquals(asked.Arguments, arguments);

    internal static bool RequiresStandardError(IReadOnlyList<string> arguments) =>
        Asked.Value is { } asked && asked.RequireStandardError && ReferenceEquals(asked.Arguments, arguments);

    public static Task<LandingGitResult> StatusVerdictAsync(this ILandingGit git, string checkout,
        IReadOnlyList<string> statusArguments, CancellationToken ct)
        => Mark(statusArguments, () => git.RunAsync(checkout, statusArguments, ct));

    public static Task<LandingGitResult> StatusVerdictAsync(this ITaskProgressGit git, string checkout,
        IReadOnlyList<string> statusArguments, CancellationToken ct)
        => Mark(statusArguments, () => git.RunAsync(checkout, statusArguments, ct));

    public static Task<LandingGitResult> StatusWithStandardErrorAsync(this ILandingGit git, string checkout,
        IReadOnlyList<string> statusArguments, CancellationToken ct)
        => Mark(statusArguments, () => git.RunAsync(checkout, statusArguments, ct), requireStandardError: true);

    private static async Task<LandingGitResult> Mark(IReadOnlyList<string> arguments, Func<Task<LandingGitResult>> run,
        bool requireStandardError = false)
    {
        var previous = Asked.Value;
        Asked.Value = (arguments, requireStandardError);
        try { return await run(); }
        finally { Asked.Value = previous; }
    }
}
