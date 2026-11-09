using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Antiphon.Hosting;

/// <summary>
/// One process-private XDG cache so concurrent pwsh starts do not share a JIT profile.
/// CARD-1793: the runner gives each agent session its own owned cache, and every install
/// removes family directories whose recorded owner is no longer a live process.
/// </summary>
internal static class PwshPrivateCache
{
    internal const string VariableName = "XDG_CACHE_HOME";
    internal const string ProbeVariable = "ANTIPHON_PWSH_CACHE_PROBE";
    internal const string DirectoryPrefix = "antiphon-pwsh-cache-";
    private const string MarkerName = "owner.pid";
    private static readonly object Gate = new();
    private static string? _created;

    static PwshPrivateCache() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Release();

    [ModuleInitializer]
    internal static void InstallAtStartup() => Install();

    // The probe runs from Main. Environment.Exit during the module initializer
    // deadlocks the runtime after the directory has already been removed.
    internal static int CompleteProbe(string outputPath)
    {
        var created = Install();
        var code = WriteProbe(created, outputPath);
        Release();
        return code;
    }

    internal static string Install()
    {
        lock (Gate)
        {
            var current = Environment.GetEnvironmentVariable(VariableName);
            if (OwnedByThisProcess(current))
                return Path.GetFullPath(current!);

            SweepStale(Path.GetTempPath(), IsLive);
            var directory = Path.Combine(Path.GetTempPath(), DirectoryName());
            Directory.CreateDirectory(directory);
            WriteOwner(directory, Environment.ProcessId);
            Environment.SetEnvironmentVariable(VariableName, directory);
            _created = directory;
            return directory;
        }
    }

    internal static void Release()
    {
        lock (Gate)
        {
            var directory = _created;
            if (directory is null)
                return;
            _created = null;
            if (OwnedByThisProcess(directory))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            if (string.Equals(Environment.GetEnvironmentVariable(VariableName), directory, StringComparison.Ordinal))
                Environment.SetEnvironmentVariable(VariableName, null);
        }
    }

    /// <summary>
    /// CARD-1793: a cache for one agent session. The caller names the owner (the runner until the
    /// pty-host pid is known) and deletes it at session end; the sweep covers a lost owner.
    /// </summary>
    internal static string CreateOwned(string label, int ownerPid)
    {
        var directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        WriteOwner(directory, ownerPid);
        return directory;
    }

    internal static void AssignOwner(string directory, int ownerPid)
    {
        try
        {
            WriteOwner(directory, ownerPid);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static void DeleteOwned(string directory)
    {
        if (IsFamilyDirectory(Path.GetTempPath(), directory))
            TryDelete(directory);
    }

    /// <summary>
    /// A SIGKILLed owner never runs <see cref="Release"/>. Remove each family directory whose
    /// marker names a process that is not live. An absent or unreadable marker is kept: its
    /// creator may sit between CreateDirectory and the marker move. Links are never followed.
    /// </summary>
    internal static int SweepStale(string root, Func<int, bool> isLive)
    {
        string[] candidates;
        try
        {
            candidates = Directory.GetDirectories(root, DirectoryPrefix + "*");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }

        var removed = 0;
        foreach (var directory in candidates)
        {
            if (!IsFamilyDirectory(root, directory) || ReadOwner(directory) is not { } owner
                || owner == Environment.ProcessId || isLive(owner))
                continue;
            if (TryDelete(directory))
                removed++;
        }

        return removed;
    }

    private static bool IsLive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsFamilyDirectory(string root, string directory)
    {
        try
        {
            var full = Path.GetFullPath(directory);
            if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.Ordinal)
                || !Path.GetFileName(full).StartsWith(DirectoryPrefix, StringComparison.Ordinal))
                return false;
            var attributes = File.GetAttributes(full);
            return (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static int? ReadOwner(string directory)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(directory, MarkerName)).Trim();
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0 ? pid : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Write then move, so a concurrent sweep never reads a truncated pid.
    private static void WriteOwner(string directory, int ownerPid)
    {
        var temporary = Path.Combine(directory, MarkerName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, ownerPid.ToString(CultureInfo.InvariantCulture));
        File.Move(temporary, Path.Combine(directory, MarkerName), overwrite: true);
    }

    private static bool TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int WriteProbe(string directory, string outputPath)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("[Console]::Out.Write($env:XDG_CACHE_HOME)");
        using var process = Process.Start(start);
        if (process is null)
            return 2;

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            process.WaitForExit(5_000);
            return 2;
        }

        var output = stdout.GetAwaiter().GetResult().Trim();
        _ = stderr.GetAwaiter().GetResult();
        try
        {
            File.WriteAllText(outputPath, output);
        }
        catch (IOException)
        {
            return 2;
        }
        catch (UnauthorizedAccessException)
        {
            return 2;
        }

        return process.ExitCode == 0 && string.Equals(output, directory, StringComparison.Ordinal) ? 0 : 2;
    }

    private static bool OwnedByThisProcess(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }

        var root = Path.GetFullPath(Path.GetTempPath());
        var relative = Path.GetRelativePath(root, full);
        if (relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            return false;

        var prefix = DirectoryPrefix + ProcessToken() + "-";
        if (!Path.GetFileName(full).StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var marker = Path.Combine(full, MarkerName);
        try
        {
            return File.Exists(marker)
                && string.Equals(File.ReadAllText(marker).Trim(), ProcessToken(), StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string DirectoryName() =>
        DirectoryPrefix + ProcessToken() + "-" + Guid.NewGuid().ToString("N");

    private static string ProcessToken() =>
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
}
