using System.IO.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.IO.WriteBehind;

// Info objects, streams and version info read the disk directly once created, so creating one first lands
// the queued write it would see: the path's own write for files and streams, every write for directories.

internal sealed class WriteBehindFileInfoFactory(IFileSystem owner, IFileSystem inner, WriteBehindQueue queue) : IFileInfoFactory
{
    public IFileSystem FileSystem => owner;

    public IFileInfo New(string fileName)
    {
        queue.Flush(fileName);
        return inner.FileInfo.New(fileName);
    }

    public IFileInfo? Wrap(FileInfo? fileInfo)
    {
        if (fileInfo is not null) queue.Flush(fileInfo.FullName);
        return inner.FileInfo.Wrap(fileInfo);
    }
}

internal sealed class WriteBehindDirectoryInfoFactory(IFileSystem owner, IFileSystem inner, WriteBehindQueue queue) : IDirectoryInfoFactory
{
    public IFileSystem FileSystem => owner;

    public IDirectoryInfo New(string path)
    {
        queue.Flush();
        return inner.DirectoryInfo.New(path);
    }

    public IDirectoryInfo? Wrap(DirectoryInfo? directoryInfo)
    {
        queue.Flush();
        return inner.DirectoryInfo.Wrap(directoryInfo);
    }
}

internal sealed class WriteBehindFileVersionInfoFactory(IFileSystem owner, IFileSystem inner, WriteBehindQueue queue) : IFileVersionInfoFactory
{
    public IFileSystem FileSystem => owner;

    public IFileVersionInfo GetVersionInfo(string fileName)
    {
        queue.Flush(fileName);
        return inner.FileVersionInfo.GetVersionInfo(fileName);
    }
}

internal sealed class WriteBehindFileStreamFactory(IFileSystem owner, IFileSystem inner, WriteBehindQueue queue) : IFileStreamFactory
{
    public IFileSystem FileSystem => owner;

    private string Landed(string path)
    {
        queue.Flush(path);
        return path;
    }

    public FileSystemStream New(SafeFileHandle handle, FileAccess access) => inner.FileStream.New(handle, access);
    public FileSystemStream New(SafeFileHandle handle, FileAccess access, int bufferSize) => inner.FileStream.New(handle, access, bufferSize);
    public FileSystemStream New(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync) => inner.FileStream.New(handle, access, bufferSize, isAsync);
    public FileSystemStream New(string path, FileMode mode) => inner.FileStream.New(Landed(path), mode);
    public FileSystemStream New(string path, FileMode mode, FileAccess access) => inner.FileStream.New(Landed(path), mode, access);
    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share) => inner.FileStream.New(Landed(path), mode, access, share);
    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize) => inner.FileStream.New(Landed(path), mode, access, share, bufferSize);
    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync) => inner.FileStream.New(Landed(path), mode, access, share, bufferSize, useAsync);
    public FileSystemStream New(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options) => inner.FileStream.New(Landed(path), mode, access, share, bufferSize, options);
    public FileSystemStream New(string path, FileStreamOptions options) => inner.FileStream.New(Landed(path), options);
    public FileSystemStream Wrap(FileStream fileStream) => inner.FileStream.Wrap(fileStream);
}
