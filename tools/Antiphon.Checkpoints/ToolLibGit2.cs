namespace Antiphon.Checkpoints;

/// <summary>
/// libgit2 in the checkpoint tool reads only the repository's own config, as the hardened git child does
/// (GIT_CONFIG_NOSYSTEM, GIT_CONFIG_GLOBAL on the null device). Without this it would also read the system, global
/// and XDG files (Git for Windows' system file sets core.autocrlf). The user's default ignore file, which the git
/// child does still read, is handled by declining to git (WorkTreeIdentity.UserDefaultIgnoreApplies). Process-wide,
/// so it is applied once before the first libgit2 read. No long-lived host loads this assembly: the tool runs as
/// its own process, and the test host already isolates libgit2 the same way.
/// <para>
/// The search paths point at a fresh directory this process creates (owner-only on Unix) and removes at exit, never
/// a shared name another account could fill with config. When libgit2 cannot be set up (its native library does not
/// load in a host that does not read the tool's deps.json, or anything else fails), <see cref="Available"/> is false
/// and callers read through git instead.
/// </para>
/// </summary>
internal static class ToolLibGit2
{
    private static readonly Lazy<bool> Isolated = new(() =>
    {
        if (!Antiphon.GitInProcess.InProcessGitReads.Enabled) return false;
        string? empty = null;
        try
        {
            empty = Directory.CreateTempSubdirectory("antiphon-checkpoint-libgit2-noconfig-").FullName;
            var created = empty;
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Directory.Delete(created); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            };
            foreach (var level in new[]
                     {
                         LibGit2Sharp.ConfigurationLevel.System, LibGit2Sharp.ConfigurationLevel.Global,
                         LibGit2Sharp.ConfigurationLevel.Xdg, LibGit2Sharp.ConfigurationLevel.ProgramData,
                     })
                LibGit2Sharp.GlobalSettings.SetConfigSearchPaths(level, empty);
            return true;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            return false;
        }
    });

    /// <summary>libgit2 loaded and is isolated from user config; false means read through git.</summary>
    public static bool Available => Isolated.Value;

    /// <summary>libgit2's native library cannot be used in this process (not a refusal or a read error).</summary>
    public static bool IsNativeUnavailable(Exception ex) =>
        ex is DllNotFoundException or TypeInitializationException or BadImageFormatException or EntryPointNotFoundException;
}
