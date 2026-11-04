namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// The renames guarded removal performs before it deletes anything (the set-aside record's
/// replace and the tree's move aside) and the set-aside tree's delete. Production uses
/// <see cref="System"/>; a test injects a rename or delete that fails the way Windows does,
/// because a sharing violation cannot be raised on Linux.
/// </summary>
internal sealed record WorktreeRenames(Action<string, string> MoveDirectory, Action<string, string> ReplaceFile,
    Action<TimeSpan> Pause, bool Windows)
{
    /// <summary>A scanner or indexer holding an entry for a moment: five attempts 100 ms apart, as the no-follow open.</summary>
    public const int SharingAttempts = 5;
    public static readonly TimeSpan SharingPause = TimeSpan.FromMilliseconds(100);

    public static WorktreeRenames System { get; } = new(Directory.Move,
        (source, destination) => File.Move(source, destination, overwrite: true), Thread.Sleep, OperatingSystem.IsWindows());

    /// <summary>The set-aside tree's no-follow delete; a test injects the refusal Windows gives while a scanner holds an entry.</summary>
    public Action<string, bool> DeleteTree { get; init; } = WorktreeNoFollowDelete.Delete;

    /// <summary>
    /// Runs one rename, retrying only a Windows sharing conflict, at most <see cref="SharingAttempts"/>
    /// times. Any other error, or the conflict at the last attempt, is the refusal: nothing is forced.
    /// </summary>
    public void Retrying(Action<string, string> rename, string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { rename(source, destination); return; }
            catch (Exception ex) when (attempt < SharingAttempts && IsSharingConflict(ex)) { Pause(SharingPause); }
        }
    }

    /// <summary>
    /// ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION, or ERROR_ACCESS_DENIED, which is how Windows refuses
    /// a directory rename while a child is open. Off Windows an HResult carries errno and access denied
    /// is a lasting permission, so nothing there is transient.
    /// </summary>
    public bool IsSharingConflict(Exception ex) => Windows && ex is IOException or UnauthorizedAccessException
        && ex.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021) or unchecked((int)0x80070005);

    /// <summary>
    /// A Windows refusal of the set-aside delete that a scanner or indexer holding an entry gives:
    /// access denied, sharing or lock violation, or a directory whose deleted child is still visible
    /// (ERROR_DIR_NOT_EMPTY). The no-follow walk raises the raw Win32 code and .NET's own I/O the
    /// 0x8007xxxx form, so both are read. A preserved nested repository is never transient.
    /// </summary>
    public bool IsTransientDelete(Exception ex) => Windows && ex is not NestedRepositoryPreservedException
        && ex is IOException or UnauthorizedAccessException && Win32Code(ex) is 5 or 32 or 33 or 145;

    /// <summary>The Win32 code an exception carries, from either a raw code or its 0x8007xxxx HResult.</summary>
    public static int Win32Code(Exception ex) =>
        (ex.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) ? ex.HResult & 0xFFFF : ex.HResult;
}
