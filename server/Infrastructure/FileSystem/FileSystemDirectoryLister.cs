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
    private readonly ILogger<FileSystemDirectoryLister> _logger;

    public FileSystemDirectoryLister(IFileSystem fileSystem, ILogger<FileSystemDirectoryLister> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public bool DirectoryExists(string path)
    {
        try
        {
            return _fileSystem.Directory.Exists(path);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DirectoryExists failed for {Path}", path);
            return false;
        }
    }

    public IReadOnlyList<string> ListDirectories(string parentDir)
    {
        try
        {
            return _fileSystem.Directory
                .GetDirectories(parentDir)
                .Select(PathNormalizer.Normalize)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ListDirectories failed for {ParentDir}", parentDir);
            return [];
        }
    }
}
