using System.Diagnostics;

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
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var start = new ProcessStartInfo("cmd.exe")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "/d", "/c", "mklink", "/J", path, target }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start);
                if (process is null) return null;
                process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd();
                if (!process.WaitForExit(30_000) || process.ExitCode != 0) return null;
            }
            else Directory.CreateSymbolicLink(path, target);
            return IsLink(path) ? new DirectoryLink(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
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

    private static bool IsLink(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
}
