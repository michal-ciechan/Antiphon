using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Antiphon.Hosting;

/// <summary>
/// One process-private XDG cache so concurrent pwsh starts do not share a JIT profile.
/// </summary>
internal static class PwshPrivateCache
{
    internal const string VariableName = "XDG_CACHE_HOME";
    internal const string ProbeVariable = "ANTIPHON_PWSH_CACHE_PROBE";
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

            var directory = Path.Combine(Path.GetTempPath(), DirectoryName());
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, MarkerName), ProcessToken());
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

        var prefix = "antiphon-pwsh-cache-" + ProcessToken() + "-";
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
        "antiphon-pwsh-cache-" + ProcessToken() + "-" + Guid.NewGuid().ToString("N");

    private static string ProcessToken() =>
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
}
