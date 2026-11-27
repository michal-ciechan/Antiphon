namespace Antiphon.Checkpoints;

internal static class CheckpointDiskSpace
{
    private const long MinimumFreeBytes = 20L << 30;

    public static bool Refuse(TextWriter output, params string[] paths)
    {
        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            var existing = full;
            while (!Directory.Exists(existing) && Path.GetDirectoryName(existing) is { } parent)
                existing = parent;
            try
            {
                // DriveInfo accepts a directory on Unix. Use the existing ancestor to account
                // for a results root that the checkpoint will create on a separate mount.
                var drive = new DriveInfo(OperatingSystem.IsWindows() ? Path.GetPathRoot(full)! : existing);
                var free = drive.AvailableFreeSpace;
                if (free >= MinimumFreeBytes)
                    continue;
                output.WriteLine($"checkpoints: workspace_disk_low: {path} has {free} bytes available. Free at least 20 GiB before retrying.");
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
        return false;
    }
}
