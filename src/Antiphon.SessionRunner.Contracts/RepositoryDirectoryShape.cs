namespace Antiphon.SessionRunner.Contracts;

/// <summary>Filesystem markers exercised by C1032_InspectionHoldShapes (nested and bare variants).</summary>
public static class RepositoryDirectoryShape
{
    // A partial or case-varied signature is unproven content, never disposable.
    // Recognizing a marker does not certify the health of a repository.
    public static bool HasRepositoryMarker(string directory) =>
        Directory.EnumerateFileSystemEntries(directory).Any(entry =>
            Path.GetFileName(entry).Equals(".git", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(entry).Equals("HEAD", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(entry).Equals("objects", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(entry).Equals("refs", StringComparison.OrdinalIgnoreCase));

    public static bool HasBareMarker(string directory) =>
        Attributes(Path.Combine(directory, "HEAD")) is { } head && !head.HasFlag(FileAttributes.Directory)
        && Attributes(Path.Combine(directory, "objects")) is { } objects && objects.HasFlag(FileAttributes.Directory)
        && Attributes(Path.Combine(directory, "refs")) is { } refs && refs.HasFlag(FileAttributes.Directory);

    // C1032_InspectionHoldShapes preserves markers; absence alone is returned as null.
    public static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
