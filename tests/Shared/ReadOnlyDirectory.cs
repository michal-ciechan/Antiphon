namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Deletes a fixture directory that may contain read-only git loose objects.
/// Windows <see cref="Directory.Delete(string, bool)"/> does not clear that bit and throws
/// <see cref="UnauthorizedAccessException"/>. A directory link is not walked.
/// </summary>
internal static class ReadOnlyDirectory
{
    public static void Delete(string path)
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.Filesystem, "delete-tree");
        if (Directory.Exists(path))
            ClearReadOnly(path);
        Directory.Delete(path, recursive: true);
    }

    private static void ClearReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);

        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions
        {
            AttributesToSkip = 0,
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
        }))
            ClearReadOnly(entry);
    }
}
