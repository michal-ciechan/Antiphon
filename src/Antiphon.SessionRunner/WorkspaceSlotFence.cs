namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0824 S3 D-10. Cold and scheduled wipes wait while a qualification lock exists or its path
/// cannot be read. An empty path is not a fence. Warm lease and release are not this check.
/// </summary>
public interface IWorkspaceSlotFence
{
    bool Held();
}

/// <summary>Reads <see cref="PhoneHomeWorkspaceSlotSettings.RcFencePath"/>. A missing file is clear.</summary>
public sealed class WorkspaceSlotFenceFile : IWorkspaceSlotFence
{
    private readonly string _path;
    private readonly Func<string, bool>? _probe;

    public WorkspaceSlotFenceFile(string? path) : this(path, null)
    {
    }

    /// <summary>Test seam. The probe replaces the attribute read and may throw the same IO failures.</summary>
    internal WorkspaceSlotFenceFile(string? path, Func<string, bool>? probe)
    {
        _path = path?.Trim() ?? "";
        _probe = probe;
    }

    public bool Held()
    {
        if (_path.Length == 0)
            return false;
        try
        {
            return _probe is null ? Present(_path) : _probe(_path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return true;
        }
    }

    private static bool Present(string path)
    {
        _ = File.GetAttributes(path);
        return true;
    }
}
