using Antiphon.GitInProcess;
using LibGit2Sharp;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// The suite's git children are isolated from the machine's config (GIT_CONFIG_NOSYSTEM, GIT_CONFIG_GLOBAL,
/// a fixture HOME), but libgit2 in this process would still read the system, global and XDG files: Git for
/// Windows' own system file sets core.autocrlf, and a developer's ~/.config/git/ignore adds ignore rules.
/// Those differences make <see cref="Antiphon.GitInProcess.GitStatusConfigAgreement"/> send
/// every status to git. Pointing libgit2's non-repository search paths at one empty directory makes it read
/// what the isolated children read; a child that does read the machine's config disagrees and runs git.
/// Production is untouched: there the children inherit this process's config, and libgit2 reads the same files.
/// </summary>
public static class LibGit2ConfigIsolation
{
    public static string EmptyDirectory { get; } = Path.Combine(Path.GetTempPath(), "antiphon-libgit2-noconfig");

    [Before(Assembly)]
    public static void IsolateLibGit2Config()
    {
        Directory.CreateDirectory(EmptyDirectory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(EmptyDirectory))
        {
            try
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var level in new[] { ConfigurationLevel.System, ConfigurationLevel.Global, ConfigurationLevel.Xdg, ConfigurationLevel.ProgramData })
            GlobalSettings.SetConfigSearchPaths(level, EmptyDirectory);
    }
}
