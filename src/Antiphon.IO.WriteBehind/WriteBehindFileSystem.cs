using System.IO.Abstractions;

namespace Antiphon.IO.WriteBehind;

/// <summary>
/// An <see cref="IFileSystem"/> whose whole-file writes (<c>File.WriteAll*</c>) return at once and land on
/// the inner file system on a background thread, each as a temporary file renamed over the target.
/// <para>
/// Reads through this instance see queued writes immediately: <c>File.ReadAll*</c>, <c>ReadLines</c>,
/// <c>OpenText</c> and <c>Exists</c> answer from memory. Every other operation on a path with a queued write
/// first waits for that write (<c>File.Delete</c> drops it instead), and directory operations wait for all
/// queued writes, so listings include them. Streams and info objects reflect the disk as it is when they are
/// created. Other processes see a file once its write lands.
/// </para>
/// <para>
/// A failed background write is raised by the next <see cref="Flush()"/> (or <see cref="Dispose"/>) as an
/// <see cref="AggregateException"/> of <see cref="WriteBehindException"/>; its content is dropped.
/// </para>
/// </summary>
public sealed class WriteBehindFileSystem : IFileSystem, IDisposable
{
    private readonly IFileSystem inner;

    /// <summary>Decorates <paramref name="inner"/> (for example <c>new FileSystem()</c>).</summary>
    public WriteBehindFileSystem(IFileSystem inner, WriteBehindOptions? options = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Queue = new WriteBehindQueue(inner, options ?? new WriteBehindOptions());
        File = new WriteBehindFile(this, inner, Queue);
        Directory = new WriteBehindDirectory(this, inner, Queue);
        FileInfo = new WriteBehindFileInfoFactory(this, inner, Queue);
        DirectoryInfo = new WriteBehindDirectoryInfoFactory(this, inner, Queue);
        FileStream = new WriteBehindFileStreamFactory(this, inner, Queue);
        FileVersionInfo = new WriteBehindFileVersionInfoFactory(this, inner, Queue);
    }

    internal WriteBehindQueue Queue { get; }

    /// <summary>The decorated file system.</summary>
    public IFileSystem Inner => inner;

    /// <summary>Writes queued or in progress.</summary>
    public int PendingWrites => Queue.PendingCount;

    /// <inheritdoc />
    public IFile File { get; }

    /// <inheritdoc />
    public IDirectory Directory { get; }

    /// <inheritdoc />
    public IFileInfoFactory FileInfo { get; }

    /// <inheritdoc />
    public IFileStreamFactory FileStream { get; }

    /// <inheritdoc />
    public IPath Path => inner.Path;

    /// <inheritdoc />
    public IDirectoryInfoFactory DirectoryInfo { get; }

    /// <inheritdoc />
    public IDriveInfoFactory DriveInfo => inner.DriveInfo;

    /// <inheritdoc />
    public IFileSystemWatcherFactory FileSystemWatcher => inner.FileSystemWatcher;

    /// <inheritdoc />
    public IFileVersionInfoFactory FileVersionInfo { get; }

    /// <summary>Waits until every queued write is on disk; throws the writes that failed since the last flush.</summary>
    public void Flush() => Queue.Flush();

    /// <summary>Waits until the path's queued write is on disk.</summary>
    public void Flush(string path) => Queue.Flush(path);

    /// <summary>
    /// The length in bytes of the path's queued write that has not landed yet, or null when none is queued
    /// (what is on disk then is current).
    /// </summary>
    public long? PendingLength(string path) => Queue.TryGet(path, out var content) ? content.LongLength : null;

    /// <summary>Flushes, then refuses further writes.</summary>
    public void Dispose() => Queue.Dispose();
}
