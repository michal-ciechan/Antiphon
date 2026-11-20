namespace Antiphon.Checkpoints;

/// <summary>
/// libgit2 in the checkpoint tool reads only the repository's own config, as the hardened git child does
/// (GIT_CONFIG_NOSYSTEM, GIT_CONFIG_GLOBAL and core.excludesFile on the null device). Without this it would also
/// read the system, global and XDG files: Git for Windows' system file sets core.autocrlf, and a user's default
/// ignore file hides untracked files. Process-wide, so it is applied once before the first libgit2 read. No
/// long-lived host loads this assembly: the tool runs as its own process, and the test host already isolates
/// libgit2 the same way.
/// <para>
/// The search paths point at a fresh directory this process creates (owner-only on Unix), never a shared name
/// another account could fill with config. When libgit2's native library cannot load (a host that does not read
/// the tool's deps.json), <see cref="Available"/> is false and callers read through git instead.
/// </para>
/// </summary>
internal static class ToolLibGit2
{
    private static readonly Lazy<bool> Isolated = new(() =>
    {
        try
        {
            var empty = Directory.CreateTempSubdirectory("antiphon-checkpoint-libgit2-noconfig-").FullName;
            foreach (var level in new[]
                     {
                         LibGit2Sharp.ConfigurationLevel.System, LibGit2Sharp.ConfigurationLevel.Global,
                         LibGit2Sharp.ConfigurationLevel.Xdg, LibGit2Sharp.ConfigurationLevel.ProgramData,
                     })
                LibGit2Sharp.GlobalSettings.SetConfigSearchPaths(level, empty);
            return true;
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return false;
        }
    });

    /// <summary>libgit2 loaded and is isolated from user config; false means read through git.</summary>
    public static bool Available => Isolated.Value;

    /// <summary>A failure that means libgit2 itself cannot be used in this process (as opposed to a refusal).</summary>
    public static bool IsUnavailable(Exception ex) =>
        ex is DllNotFoundException or TypeInitializationException or BadImageFormatException
            or EntryPointNotFoundException or IOException or UnauthorizedAccessException;
}
