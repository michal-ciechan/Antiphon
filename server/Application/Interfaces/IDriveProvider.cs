namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Enumerates the machine's drive roots (e.g. "C:/", "D:/") for the working-directory
/// autocomplete when no path has been typed yet. Isolated behind its own seam because
/// <c>MockFileSystem.DriveInfo.GetDrives()</c> does not reliably surface configured
/// drives, so drive enumeration is faked directly in tests.
/// </summary>
public interface IDriveProvider
{
    /// <summary>Drive roots as forward-slash paths, e.g. ["C:/", "D:/"].</summary>
    IReadOnlyList<string> GetDriveRoots();
}
