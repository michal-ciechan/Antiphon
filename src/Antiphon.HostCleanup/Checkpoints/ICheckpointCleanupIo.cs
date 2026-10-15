namespace Antiphon.HostCleanup.Checkpoints;

public enum CheckpointPlatform { Linux, Windows, Other }

// Observations only. Custody, admission and receipt interpretation belong to callers.
public readonly record struct CheckpointStat(string Kind, ulong Device, ulong Inode, long Size,
    long Modified, long Changed, ushort Mode = 0, uint Links = 0, uint Owner = 0);
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

public interface ICheckpointCleanupIo
{
    CheckpointPlatform Platform { get; }
    uint Owner { get; }
    bool FileExists(string path);
    bool DirectoryExists(string path);
    FileAttributes Attributes(string path);
    string? LinkTarget(string path);
    long Length(string path);
    IEnumerable<string> Enumerate(string path);
    CheckpointStat Stat(string path, bool portable = false);
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
    string? EnvironmentVariable(string name);
}
