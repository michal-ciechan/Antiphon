namespace Antiphon.Checkpoints;

public static class RepoPaths
{
    public static string FindRoot(string? start = null)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(start ?? Environment.CurrentDirectory));
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new ManifestValidationException("repoRoot", "could not find Antiphon.sln walking up from " + (start ?? Environment.CurrentDirectory));
    }

    public static string RunId()
    {
        var suffix = Guid.NewGuid().ToString("N")[..4];
        return DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + suffix;
    }
}
