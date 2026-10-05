using Antiphon.Tests.Scripts;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// A directory link a test owns: a junction on Windows (no privilege needed), a symbolic link
/// elsewhere. <see cref="TryCreate"/> returns null when the host cannot make one, so the caller
/// skips instead of passing vacuously. Dispose removes only the link, never its target.
/// </summary>
internal sealed class DirectoryLink : IDisposable
{
    public string Path { get; private set; }

    private DirectoryLink(string path) => Path = path;

    public static DirectoryLink? TryCreate(string path, string target)
    {
        if (OperatingSystem.IsWindows())
            return Task.Run(() => TryCreateWindowsAsync(path, target)).GetAwaiter().GetResult();
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return IsLink(path) ? new DirectoryLink(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    // Owned-run failures deliberately remain outside the ordinary attribute/setup catch.
    // Callers must retain scratch after an exception unless an independent owner observer
    // confirms cleanup; an exception message is not process-cleanup authority.
    internal static async Task<DirectoryLink?> TryCreateWindowsAsync(string path, string target,
        ScriptHarnessOptions? options = null, string caseName = "Create", CancellationToken ct = default)
    {
        var result = await DirectoryLinkCommand.RunAsync(path, target, options, caseName, ct);
        return Complete(path, result);
    }

    internal static DirectoryLink? Complete(string path, ScriptHarnessResult result,
        Func<string, bool>? observe = null)
    {
        if (result.ExitCode != 0) return null;
        try { return (observe ?? IsLink)(path) ? new DirectoryLink(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { return null; }
    }

    /// <summary>Renames the link itself; the target is not followed.</summary>
    public void MoveTo(string path)
    {
        Directory.Move(Path, path);
        Path = path;
    }

    public void Dispose()
    {
        if (!IsLink(Path)) return;
        try
        {
            Directory.Delete(Path, recursive: false);
        }
        catch (DirectoryNotFoundException) when (!OperatingSystem.IsWindows())
        {
            // rmdir refuses a symbolic link whose target is already gone. The link is still here.
            File.Delete(Path);
        }
    }

    internal static bool IsLink(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
}
