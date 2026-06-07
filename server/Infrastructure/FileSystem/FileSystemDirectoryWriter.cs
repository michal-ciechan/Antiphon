using System.IO.Abstractions;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.FileSystem;

/// <summary>
/// <see cref="IDirectoryWriter"/> backed by <see cref="IFileSystem"/>.
/// <c>Directory.CreateDirectory</c> is idempotent — creating an existing directory is a no-op.
/// </summary>
public sealed class FileSystemDirectoryWriter : IDirectoryWriter
{
    private readonly IFileSystem _fileSystem;

    public FileSystemDirectoryWriter(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public void CreateDirectory(string path) => _fileSystem.Directory.CreateDirectory(path);
}
