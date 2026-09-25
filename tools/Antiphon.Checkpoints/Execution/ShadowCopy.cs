namespace Antiphon.Checkpoints;

public static class ShadowCopy
{
    public static void CopyToolOutput(string toolDirectory, string destination)
    {
        if (!Directory.Exists(toolDirectory))
            throw new DirectoryNotFoundException(toolDirectory);
        if (!Directory.EnumerateFiles(toolDirectory, "*.dll").Any())
            throw new InvalidOperationException("tool directory has no dll: " + toolDirectory);

        Directory.CreateDirectory(destination);
        CopyFiles(toolDirectory, destination);
    }

    private static void CopyFiles(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(destination, name), overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (name is "src" or "obj")
                continue;
            var next = Path.Combine(destination, name);
            Directory.CreateDirectory(next);
            CopyFiles(dir, next);
        }
    }
}
