using Antiphon.HostCleanup.Checkpoints;
namespace Antiphon.Checkpoints;

public static class ShadowCopy
{
    public static void CopyToolOutput(string toolDirectory, string destination, CheckpointCleanupContext? context = null)
    {
        context ??= CheckpointCleanupDefaults.Context;
        var write = new CheckpointWrite(context);
        if (!context.Io.DirectoryExists(toolDirectory))
            throw new DirectoryNotFoundException(toolDirectory);
        if (!context.Io.Enumerate(toolDirectory).Where(path => context.Io.FileExists(path) && Path.GetExtension(path) == ".dll").Any())
            throw new InvalidOperationException("tool directory has no dll: " + toolDirectory);

        var ownership = write.Under(destination);
        SafeWrite.Require(write.CreateFreshDirectory(destination, ownership));
        var bytes = CopyFiles(context, toolDirectory, destination, ownership);
        SafeWrite.Require(new CheckpointOutputLedger(context).Seal(destination));
        CheckpointUsageEvent.Write("copy", destination, bytes, context);
    }

    private static long CopyFiles(CheckpointCleanupContext context, string source, string destination, WriteOwnership ownership)
    {
        var write = new CheckpointWrite(context);
        var bytes = 0L;
        foreach (var file in context.Io.Enumerate(source).Where(context.Io.FileExists))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                continue;
            SafeWrite.Require(write.CopyOverwrite(file, Path.Combine(destination, name), ownership));
            bytes += context.Io.Length(file);
        }

        foreach (var dir in context.Io.Enumerate(source).Where(context.Io.DirectoryExists))
        {
            var name = Path.GetFileName(dir);
            if (name is "src" or "obj")
                continue;
            var next = Path.Combine(destination, name);
            SafeWrite.Require(write.CreateDirectory(next, ownership));
            bytes += CopyFiles(context, dir, next, ownership);
        }
        return bytes;
    }
}
