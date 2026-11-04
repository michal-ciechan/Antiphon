namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// The renames guarded removal performs before it deletes anything: the set-aside record's
/// replace and the tree's move aside. Production uses <see cref="System"/>; a test injects a
/// rename that fails the way Windows does, because a sharing violation cannot be raised on Linux.
/// </summary>
internal sealed record WorktreeRenames(Action<string, string> MoveDirectory, Action<string, string> ReplaceFile,
    Action<TimeSpan> Pause, bool Windows)
{
    /// <summary>A scanner or indexer holding an entry for a moment: five attempts 100 ms apart, as the no-follow open.</summary>
    public const int SharingAttempts = 5;
    public static readonly TimeSpan SharingPause = TimeSpan.FromMilliseconds(100);

    public static WorktreeRenames System { get; } = new(Directory.Move,
        (source, destination) => File.Move(source, destination, overwrite: true), Thread.Sleep, OperatingSystem.IsWindows());
}
