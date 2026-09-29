using System.Diagnostics;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// Creates a directory reparse point for link-rejection tests. A symbolic link needs a privilege an
/// ordinary Windows account may not hold, so it falls back to a junction (no privilege needed);
/// both are reparse points the production link checks must reject.
/// </summary>
internal static class DirectoryLinkHelper
{
    public static void Create(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
        }

        var start = new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);
        using var process = Process.Start(start) ?? throw new IOException("cmd.exe did not start for mklink /J");
        process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(link))
            throw new IOException("mklink /J failed: " + error);
    }
}
