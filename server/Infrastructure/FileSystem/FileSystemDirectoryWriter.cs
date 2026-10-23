using Antiphon.Observability;
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
    private readonly LiveTiming? _timing;

    public FileSystemDirectoryWriter(IFileSystem fileSystem, LiveTiming? timing = null)
    {
        _fileSystem = fileSystem;
        _timing = timing;
    }

    public void CreateDirectory(string path)
    {
        using var timing = _timing?.BeginExternal(ExternalOperation.Filesystem);
        _fileSystem.Directory.CreateDirectory(path);
        timing?.Complete();
    }
}
