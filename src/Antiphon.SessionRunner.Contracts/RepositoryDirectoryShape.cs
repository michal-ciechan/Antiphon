namespace Antiphon.SessionRunner.Contracts;

/// <summary>Filesystem markers exercised by C1032_InspectionHoldShapes (nested and bare variants).</summary>
public static class RepositoryDirectoryShape
{
    public static bool HasRepositoryMarker(string directory) =>
        Attributes(Path.Combine(directory, ".git")) is not null || HasBareMarker(directory);

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
