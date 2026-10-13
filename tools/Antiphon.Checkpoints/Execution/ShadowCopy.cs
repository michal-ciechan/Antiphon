namespace Antiphon.Checkpoints;

public static class ShadowCopy
{
    public static void CopyToolOutput(string toolDirectory, string destination)
    {
        if (!Directory.Exists(toolDirectory))
            throw new DirectoryNotFoundException(toolDirectory);
        if (!Directory.EnumerateFiles(toolDirectory, "*.dll").Any())
            throw new InvalidOperationException("tool directory has no dll: " + toolDirectory);

        var ownership = SafeWrite.Under(destination);
        SafeWrite.Require(SafeWrite.CreateFreshDirectory(destination, ownership));
        var bytes = CopyFiles(toolDirectory, destination, ownership);
        SafeWrite.Require(OwnedOutputLedger.Seal(destination));
        CheckpointUsageEvent.Write("copy", destination, bytes);
    }

    private static long CopyFiles(string source, string destination, WriteOwnership ownership)
    {
        var bytes = 0L;
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                continue;
            SafeWrite.Require(SafeWrite.CopyOverwrite(file, Path.Combine(destination, name), ownership));
            bytes += new FileInfo(file).Length;
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (name is "src" or "obj")
                continue;
            var next = Path.Combine(destination, name);
            SafeWrite.Require(SafeWrite.CreateDirectory(next, ownership));
            bytes += CopyFiles(dir, next, ownership);
        }
        return bytes;
    }
}
