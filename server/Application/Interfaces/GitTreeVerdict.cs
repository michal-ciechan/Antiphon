using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// A <c>git status</c> whose caller only asks "is the tree clean?" (the output is tested for emptiness and
/// never parsed). It runs through the ordinary runner call, so a test double that overrides
/// <c>RunAsync</c> sees exactly the call it saw before, with the same argument list. The real runner may then
/// answer a dirty tree in process as well as a clean one: the output is non-empty porcelain-shaped rows (codes
/// and order are libgit2's reading, not git's bytes). Every status not asked this way keeps git's exact bytes
/// for a dirty tree. The mark is the argument list instance itself, so a nested or copied call is unmarked.
/// </summary>
public static class GitTreeVerdict
{
    private static readonly AsyncLocal<IReadOnlyList<string>?> Asked = new();

    /// <summary>Whether this exact argument list was passed by a verdict-only caller.</summary>
    internal static bool IsVerdictOnly(IReadOnlyList<string> arguments) =>
        Asked.Value is { } asked && ReferenceEquals(asked, arguments);

    public static Task<LandingGitResult> StatusVerdictAsync(this ILandingGit git, string checkout,
        IReadOnlyList<string> statusArguments, CancellationToken ct)
        => Mark(statusArguments, () => git.RunAsync(checkout, statusArguments, ct));

    public static Task<LandingGitResult> StatusVerdictAsync(this ITaskProgressGit git, string checkout,
        IReadOnlyList<string> statusArguments, CancellationToken ct)
        => Mark(statusArguments, () => git.RunAsync(checkout, statusArguments, ct));

    private static async Task<LandingGitResult> Mark(IReadOnlyList<string> arguments, Func<Task<LandingGitResult>> run)
    {
        var previous = Asked.Value;
        Asked.Value = arguments;
        try { return await run(); }
        finally { Asked.Value = previous; }
    }
}
