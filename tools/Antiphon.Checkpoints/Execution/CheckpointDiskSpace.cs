namespace Antiphon.Checkpoints;

internal static class CheckpointDiskSpace
{
    private const long MinimumFreeBytes = 20L << 30;
    public const string LowReason = "workspace_disk_low";

    public static bool Refuse(TextWriter output, Func<string, long?>? measure, params string[] paths)
    {
        if (Diagnostic(measure, paths) is not { } message)
            return false;
        output.WriteLine(message);
        return true;
    }

    public static string? Diagnostic(Func<string, long?>? measure, params string[] paths)
    {
        measure ??= AvailableBytes;
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            if (measure(path) is long free && free < MinimumFreeBytes)
                return $"checkpoints: {LowReason}: {path} has {free} bytes available. Free at least 20 GiB before retrying.";
        }
        return null;
    }

    private static long? AvailableBytes(string path)
    {
        var full = Path.GetFullPath(path);
        var existing = full;
        while (!Directory.Exists(existing) && Path.GetDirectoryName(existing) is { } parent)
            existing = parent;
        try
        {
            // The existing ancestor accounts for a destination on a separate mount.
            return new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(full)! : existing).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
