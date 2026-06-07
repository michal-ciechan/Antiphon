using System.IO.Abstractions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.FileSystem;

/// <summary>
/// <see cref="IDriveProvider"/> backed by <see cref="IFileSystem"/>. Returns ready drive
/// roots as forward-slash paths (e.g. "C:/"). Faked directly in tests because
/// <c>MockFileSystem</c> does not surface configured drives via <c>DriveInfo.GetDrives()</c>.
/// </summary>
public sealed class DriveProvider : IDriveProvider
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<DriveProvider> _logger;

    public DriveProvider(IFileSystem fileSystem, ILogger<DriveProvider> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public IReadOnlyList<string> GetDriveRoots()
    {
        try
        {
            return _fileSystem.DriveInfo
                .GetDrives()
                .Where(d => d.IsReady)
                .Select(d => PathNormalizer.Normalize(d.Name))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetDriveRoots failed");
            return [];
        }
    }
}
