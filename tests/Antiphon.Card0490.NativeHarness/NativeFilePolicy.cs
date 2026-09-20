namespace Antiphon.Card0490.NativeHarness;

public static class NativeFilePolicy
{
    public static bool IsContained(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(candidate);
        return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetFullPath(root), full, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReparse(string path) =>
        Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public static bool AllowRead(string root, string candidate) =>
        IsContained(root, candidate) && !IsReparse(candidate) && !IsReparse(root);

    public static bool AllowWrite(string root, string candidate) => AllowRead(root, candidate);

    public static bool AllowDelete(string root, string candidate, IReadOnlySet<string> inventory, bool ownerJoined) =>
        AllowWrite(root, candidate) && inventory.Contains(Path.GetFullPath(candidate)) && ownerJoined;
}
