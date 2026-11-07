using System.IO.Abstractions;

namespace Antiphon.IO.WriteBehind;

/// <summary>
/// <see cref="IDirectory"/> for <see cref="WriteBehindFileSystem"/>: anything that lists, moves, deletes or
/// dates a directory first waits for every queued write, so it sees those files; the rest goes straight to
/// the inner file system.
/// </summary>
internal sealed class WriteBehindDirectory : IDirectory
{
    private readonly IFileSystem owner;
    private readonly IDirectory inner;
    private readonly WriteBehindQueue queue;

    public WriteBehindDirectory(IFileSystem owner, IFileSystem inner, WriteBehindQueue queue)
    {
        this.owner = owner;
        this.inner = inner.Directory;
        this.queue = queue;
    }

    public IFileSystem FileSystem => owner;

    private string Landed(string path)
    {
        queue.Flush();
        return path;
    }

    public IDirectoryInfo CreateDirectory(string path) => inner.CreateDirectory(path);
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public IDirectoryInfo CreateDirectory(string path, UnixFileMode unixCreateMode) => inner.CreateDirectory(path, unixCreateMode);
    public IFileSystemInfo CreateSymbolicLink(string path, string pathToTarget) => inner.CreateSymbolicLink(path, pathToTarget);
    public IDirectoryInfo CreateTempSubdirectory(string? prefix = null) => inner.CreateTempSubdirectory(prefix);
    public bool Exists(string? path) => inner.Exists(path);
    public string GetCurrentDirectory() => inner.GetCurrentDirectory();
    public void SetCurrentDirectory(string path) => inner.SetCurrentDirectory(path);
    public string GetDirectoryRoot(string path) => inner.GetDirectoryRoot(path);
    public string[] GetLogicalDrives() => inner.GetLogicalDrives();
    public IDirectoryInfo? GetParent(string path) => inner.GetParent(path);
    public IFileSystemInfo? ResolveLinkTarget(string linkPath, bool returnFinalTarget) => inner.ResolveLinkTarget(linkPath, returnFinalTarget);

    public void Delete(string path) => inner.Delete(Landed(path));
    public void Delete(string path, bool recursive) => inner.Delete(Landed(path), recursive);
    public void Move(string sourceDirName, string destDirName) => inner.Move(Landed(sourceDirName), destDirName);

    public IEnumerable<string> EnumerateDirectories(string path) => inner.EnumerateDirectories(Landed(path));
    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern) => inner.EnumerateDirectories(Landed(path), searchPattern);
    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption) => inner.EnumerateDirectories(Landed(path), searchPattern, searchOption);
    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, EnumerationOptions enumerationOptions) => inner.EnumerateDirectories(Landed(path), searchPattern, enumerationOptions);
    public IEnumerable<string> EnumerateFiles(string path) => inner.EnumerateFiles(Landed(path));
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern) => inner.EnumerateFiles(Landed(path), searchPattern);
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) => inner.EnumerateFiles(Landed(path), searchPattern, searchOption);
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, EnumerationOptions enumerationOptions) => inner.EnumerateFiles(Landed(path), searchPattern, enumerationOptions);
    public IEnumerable<string> EnumerateFileSystemEntries(string path) => inner.EnumerateFileSystemEntries(Landed(path));
    public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern) => inner.EnumerateFileSystemEntries(Landed(path), searchPattern);
    public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption) => inner.EnumerateFileSystemEntries(Landed(path), searchPattern, searchOption);
    public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, EnumerationOptions enumerationOptions) => inner.EnumerateFileSystemEntries(Landed(path), searchPattern, enumerationOptions);
    public string[] GetDirectories(string path) => inner.GetDirectories(Landed(path));
    public string[] GetDirectories(string path, string searchPattern) => inner.GetDirectories(Landed(path), searchPattern);
    public string[] GetDirectories(string path, string searchPattern, SearchOption searchOption) => inner.GetDirectories(Landed(path), searchPattern, searchOption);
    public string[] GetDirectories(string path, string searchPattern, EnumerationOptions enumerationOptions) => inner.GetDirectories(Landed(path), searchPattern, enumerationOptions);
    public string[] GetFiles(string path) => inner.GetFiles(Landed(path));
    public string[] GetFiles(string path, string searchPattern) => inner.GetFiles(Landed(path), searchPattern);
    public string[] GetFiles(string path, string searchPattern, SearchOption searchOption) => inner.GetFiles(Landed(path), searchPattern, searchOption);
    public string[] GetFiles(string path, string searchPattern, EnumerationOptions enumerationOptions) => inner.GetFiles(Landed(path), searchPattern, enumerationOptions);
    public string[] GetFileSystemEntries(string path) => inner.GetFileSystemEntries(Landed(path));
    public string[] GetFileSystemEntries(string path, string searchPattern) => inner.GetFileSystemEntries(Landed(path), searchPattern);
    public string[] GetFileSystemEntries(string path, string searchPattern, SearchOption searchOption) => inner.GetFileSystemEntries(Landed(path), searchPattern, searchOption);
    public string[] GetFileSystemEntries(string path, string searchPattern, EnumerationOptions enumerationOptions) => inner.GetFileSystemEntries(Landed(path), searchPattern, enumerationOptions);

    public DateTime GetCreationTime(string path) => inner.GetCreationTime(Landed(path));
    public DateTime GetCreationTimeUtc(string path) => inner.GetCreationTimeUtc(Landed(path));
    public DateTime GetLastAccessTime(string path) => inner.GetLastAccessTime(Landed(path));
    public DateTime GetLastAccessTimeUtc(string path) => inner.GetLastAccessTimeUtc(Landed(path));
    public DateTime GetLastWriteTime(string path) => inner.GetLastWriteTime(Landed(path));
    public DateTime GetLastWriteTimeUtc(string path) => inner.GetLastWriteTimeUtc(Landed(path));
    public void SetCreationTime(string path, DateTime creationTime) => inner.SetCreationTime(Landed(path), creationTime);
    public void SetCreationTimeUtc(string path, DateTime creationTimeUtc) => inner.SetCreationTimeUtc(Landed(path), creationTimeUtc);
    public void SetLastAccessTime(string path, DateTime lastAccessTime) => inner.SetLastAccessTime(Landed(path), lastAccessTime);
    public void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc) => inner.SetLastAccessTimeUtc(Landed(path), lastAccessTimeUtc);
    public void SetLastWriteTime(string path, DateTime lastWriteTime) => inner.SetLastWriteTime(Landed(path), lastWriteTime);
    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => inner.SetLastWriteTimeUtc(Landed(path), lastWriteTimeUtc);
}
