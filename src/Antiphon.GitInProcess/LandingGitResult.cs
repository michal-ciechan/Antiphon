// Kept in its original namespace: the server and its tests use it in about 450 places.
namespace Antiphon.Server.Application.Dtos;

public sealed record LandingGitResult(int ExitCode, string Output, string Diagnostic)
{
    public bool Succeeded => ExitCode == 0;
    /// <summary>HEAD observed at successful rebase completion, before returning to its caller.</summary>
    public string? RebaseHeadSha { get; init; }
    /// <summary>
    /// The process wrote anything to stderr. Only the fact, never the text (stderr can carry secrets). A git
    /// read that exits 0 can still warn that it skipped part of its input (Debug 52410842: git status cannot
    /// open a directory past MAX_PATH or without permission, warns and lists nothing under it).
    /// </summary>
    public bool WroteStandardError { get; init; }
}
