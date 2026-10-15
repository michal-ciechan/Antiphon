namespace Antiphon.HostCleanup.Checkpoints;

public static class ExecutorImageGuard
{
    public static bool IsExecutorImage(CheckpointCleanupContext context, string runDirectory, string? imageDirectory = null)
    {
        var tool = Path.GetFullPath(Path.Combine(runDirectory, "tool"));
        var image = Path.GetFullPath(imageDirectory ?? context.Io.ImageDirectory);
        var prefix = tool.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!image.EndsWith(Path.DirectorySeparatorChar) && !image.EndsWith(Path.AltDirectorySeparatorChar))
            image += Path.DirectorySeparatorChar;
        return image.StartsWith(prefix, context.PathComparison);
    }
}
