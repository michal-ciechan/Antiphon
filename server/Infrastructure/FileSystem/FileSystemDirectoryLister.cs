using Antiphon.Observability;
using System.IO.Abstractions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.FileSystem;

/// <summary>
/// <see cref="IDirectoryLister"/> backed by <see cref="IFileSystem"/>. Returns child
/// directories as forward-slash full paths and swallows access errors (enumerating
/// arbitrary directories can hit permission-denied paths) by returning an empty list.
/// </summary>
public sealed class FileSystemDirectoryLister : IDirectoryLister
{
    private readonly IFileSystem _fileSystem;
    private readonly LiveTiming? _timing;
    private readonly ILogger<FileSystemDirectoryLister> _logger;

    public FileSystemDirectoryLister(IFileSystem fileSystem, ILogger<FileSystemDirectoryLister> logger, LiveTiming? timing = null)
    {
        _fileSystem = fileSystem;
        _timing = timing;
        _logger = logger;
    }

    public bool DirectoryExists(string path)
    {
        using var timing = _timing?.BeginExternal(ExternalOperation.Filesystem);
        try
        {
            var exists = _fileSystem.Directory.Exists(path);
            timing?.Complete();
            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DirectoryExists failed for {Path}", path);
            return false;
        }
    }

    public IReadOnlyList<string> ListDirectories(string parentDir)
    {
        using var timing = _timing?.BeginExternal(ExternalOperation.Filesystem);
        try
        {
            var result = _fileSystem.Directory
                .GetDirectories(parentDir)
                .Select(PathNormalizer.Normalize)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            timing?.Complete();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ListDirectories failed for {ParentDir}", parentDir);
            return [];
        }
    }
}
