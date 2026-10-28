namespace Antiphon.HostCleanup.Checkpoints;

public enum CheckpointPlatform { Linux, Windows, Other }

// Observations only. Custody, admission and receipt interpretation belong to callers.
public readonly record struct CheckpointStat(string Kind, ulong Device, ulong Inode, long Size,
    long Modified, long Changed, ushort Mode = 0, uint Links = 0, uint Owner = 0, long Created = 0);

// Volume is the Linux device id or the Windows volume serial. FileId is the inode or
// file index. Created is the birth time in Unix nanoseconds, or zero when the filesystem
// has none. A zero FileId is not a bound identity.
public readonly record struct FilesystemIdentity(ulong Volume, ulong FileId, long Created)
{
    public bool IsBound => FileId != 0;
    public bool SameAs(FilesystemIdentity other) =>
        IsBound && Volume == other.Volume && FileId == other.FileId && Created == other.Created;
}

public static class FilesystemIdentityReader
{
    public static FilesystemIdentity Read(ICheckpointCleanupIo io, string path)
    {
        try
        {
            var stat = io.Platform == CheckpointPlatform.Windows ? io.CustodyStat(path) : io.Stat(path);
            if (stat.Kind is not ("file" or "directory") || stat.Inode == 0) return default;
            return new FilesystemIdentity(stat.Device, stat.Inode, stat.Created);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or FileNotFoundException or DirectoryNotFoundException)
        {
            return default;
        }
    }
}

// The parent handle must already have been opened without following a reparse point.
// A junction or symlink at that path, or an entry whose identity is no longer the one
// recorded for it, is not deleted. Native Windows junction execution is not this decision.
public static class AnchoredDelete
{
    public static bool Allowed(bool parentOpenedWithoutFollow, bool parentIsDirectory, bool parentIsReparse,
        FilesystemIdentity expectedEntry, FilesystemIdentity entryThroughParent, bool entryIsReparse) =>
        parentOpenedWithoutFollow && parentIsDirectory && !parentIsReparse && !entryIsReparse
        && expectedEntry.SameAs(entryThroughParent);

    // A colon is an NTFS alternate-data-stream separator. Linux file names may contain one.
    public static bool EntryNameSafe(bool windows, string name) =>
        !string.IsNullOrEmpty(name)
        && name.IndexOfAny(['/', '\\']) < 0
        && (!windows || !name.Contains(':'));
}
public readonly record struct AnchoredChild(string Name, string Kind, long Size);
public readonly record struct CheckpointHandleStat(bool Regular, ulong Links, ulong Size, string Path);
public readonly record struct CheckpointReadOpen(int UnixFlags, uint WindowsFlags);
public sealed record CheckpointGitRequest(string Executable, string WorkingDirectory,
    IReadOnlyList<string> Arguments, int TimeoutMilliseconds);
public sealed record CheckpointGitResult(int Exit, string Stdout, string Stderr, bool TimedOut);

public interface ICheckpointGitRunner
{
    CheckpointGitResult Run(CheckpointGitRequest request);
}

public interface ICheckpointReadHandle : IDisposable
{
    CheckpointHandleStat Inspect();
    Stream OpenStream();
}

public readonly record struct CheckpointProcessSnapshot(long StartToken, char State, bool HasExited);

public interface ICheckpointCleanupIo
{
    int CurrentProcessId { get; }
    string MachineName { get; }
    string BootId { get; }
    string PidNamespace { get; }
    string ImageDirectory { get; }
    string CurrentDirectory { get; }
    CheckpointProcessSnapshot Process(int pid);
    DateTime CreationTimeUtc(string path);
    DateTime LastWriteTimeUtc(string path);
    string StreamPath(Stream stream);
    CheckpointPlatform Platform { get; }
    uint Owner { get; }
    bool FileExists(string path);
    bool DirectoryExists(string path);
    FileAttributes Attributes(string path);
    string? LinkTarget(string path);
    long Length(string path);
    IEnumerable<string> Enumerate(string path);
    CheckpointStat Stat(string path, bool portable = false);
    // Seal custody. Linux returns Stat. Windows reads the opened handle. The ledger stamp stays Stat,
    // which on Windows has no inode and no change time.
    CheckpointStat CustodyStat(string path);
    int CreateExclusiveDirectory(string path);
    void CreateDirectory(string path);
    Stream Open(string path, FileMode mode, FileAccess access, FileShare share);
    ICheckpointReadHandle OpenRead(string path, CheckpointReadOpen request);
    void WriteBytes(string path, ReadOnlySpan<byte> bytes);
    void AppendText(string path, string text);
    void Copy(string source, string destination, bool overwrite);
    void Move(string source, string destination, bool overwrite);
    void Unlink(string path);
    void RemoveDirectory(string path, bool recursive);
    // Open the parent without following reparse points, require expectedEntry, then delete
    // the final name relative to that directory handle.
    void DeleteAnchored(string path, FilesystemIdentity expectedEntry, bool directory);
    // One name observed without following it. Kind is file, directory, symlink, fifo,
    // socket, or other. Size is the byte length of a regular file and zero otherwise.
    IReadOnlyList<AnchoredChild> ListAnchored(string directory);
    // Unlink this name through an ancestor-safe nofollow walk of directory.
    // Symlink, FIFO and socket names are removed without being opened.
    void UnlinkAnchored(string directory, string name, bool directoryEntry);
    // Shrink a regular file through the same walk. A symlink or FIFO is refused.
    void TruncateAnchored(string directory, string name, long length);
    string? EnvironmentVariable(string name);
}
