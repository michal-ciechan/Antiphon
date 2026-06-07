namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Reads directory contents for the working-directory autocomplete. Kept as a narrow
/// seam (rather than depending on <c>IFileSystem</c> directly) so it is trivially
/// mockable/countable in tests. Implementations MUST return child paths using forward
/// slashes (Windows enumerates with backslashes).
/// </summary>
public interface IDirectoryLister
{
    bool DirectoryExists(string path);

    /// <summary>Immediate child directories of <paramref name="parentDir"/>, as full forward-slash paths.</summary>
    IReadOnlyList<string> ListDirectories(string parentDir);
}
