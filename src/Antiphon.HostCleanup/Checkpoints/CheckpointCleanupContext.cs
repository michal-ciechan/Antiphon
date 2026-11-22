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

    // CARD-2052: how many times SafeDelete read the git index in process for a tracked-path check.
    private int _inProcessIndexReads;
    internal int InProcessIndexReads => Volatile.Read(ref _inProcessIndexReads);
    internal void CountInProcessIndexRead() => Interlocked.Increment(ref _inProcessIndexReads);

    // CARD-2052 test seams, all null in production: a test changes the repository at an exact point of one owned
    // removal (during a tracked read, after an entry's decision, after its unlink) or reports filesystem metadata that
    // did not move (an equal-size rewrite inside one timestamp tick) for the index stamp's observations.
    internal Action<IReadOnlyCollection<string>>? AfterInProcessIndexRead { get; set; }
    internal Action<string>? BeforeOwnedUnlink { get; set; }
    internal Action<string>? AfterOwnedUnlink { get; set; }
    internal Func<string, CheckpointStat, CheckpointStat>? IndexStampStat { get; set; }
    public StringComparison PathComparison => Io.Platform == CheckpointPlatform.Windows
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public bool IsStrictlyInside(string root, string path)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public void Delay(TimeSpan delay) => Task.Delay(delay, Time).GetAwaiter().GetResult();
}
