namespace Antiphon.Checkpoints;

/// <summary>
/// libgit2 in the checkpoint tool reads only the repository's own config, as the hardened git child does
/// (GIT_CONFIG_NOSYSTEM, GIT_CONFIG_GLOBAL set to the null device). Without this it would also read the system,
/// global and XDG files: Git for Windows' system file sets core.autocrlf, and a user's global excludes hide
/// untracked files. Process-wide, so it is applied once before the first libgit2 read. No long-lived host loads
/// this assembly: the tool runs as its own process, and the test host already isolates libgit2 the same way.
/// </summary>
internal static class ToolLibGit2
{
    private static readonly Lazy<bool> Isolated = new(() =>
    {
        var empty = Path.Combine(Path.GetTempPath(), "antiphon-checkpoint-libgit2-noconfig");
        Directory.CreateDirectory(empty);
        foreach (var level in new[]
                 {
                     LibGit2Sharp.ConfigurationLevel.System, LibGit2Sharp.ConfigurationLevel.Global,
                     LibGit2Sharp.ConfigurationLevel.Xdg, LibGit2Sharp.ConfigurationLevel.ProgramData,
                 })
            LibGit2Sharp.GlobalSettings.SetConfigSearchPaths(level, empty);
        return true;
    });

    public static void EnsureIsolated() => _ = Isolated.Value;
}
