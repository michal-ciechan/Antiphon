namespace Antiphon.HostCleanup.Checkpoints;

public sealed class CheckpointCleanupContext
{
    public CheckpointCleanupContext(ICheckpointCleanupIo io, ICheckpointGitRunner git, TimeProvider? time = null)
    {
        Io = io;
        Git = git;
        Time = time ?? TimeProvider.System;
        Registry = new CheckpointCreationRegistry(io.Platform == CheckpointPlatform.Windows);
    }

    public ICheckpointCleanupIo Io { get; }
    public ICheckpointGitRunner Git { get; }
    public TimeProvider Time { get; }
    public CheckpointCreationRegistry Registry { get; }
    public StringComparison PathComparison => Io.Platform == CheckpointPlatform.Windows
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public bool IsStrictlyInside(string root, string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison);

    public void Delay(TimeSpan delay) => Task.Delay(delay, Time).GetAwaiter().GetResult();
}
