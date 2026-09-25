using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Antiphon.Checkpoints;

public static class HostSnapshot
{
    public static string Capture(string? slotsEndpoint = null)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("os=" + RuntimeInformation.OSDescription.Trim());
        text.AppendLine("cores=" + Environment.ProcessorCount);
        text.AppendLine("memAvailableMb=" + ReadMemAvailableMb());
        text.AppendLine("loadAvg=" + ReadLoad());
        text.AppendLine("dotnet=" + ReadDotnetVersion());
        text.AppendLine("buildSlots=" + (slotsEndpoint ?? "(not probed)"));
        return text.ToString();
    }

    public static long ReadMemAvailableMb()
    {
        if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    continue;
                var digits = new string(line.Where(char.IsDigit).ToArray());
                if (long.TryParse(digits, out var kb))
                    return kb / 1024;
            }
        }

        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
    }

    public static string ReadLoad()
    {
        if (OperatingSystem.IsLinux() && File.Exists("/proc/loadavg"))
            return File.ReadAllText("/proc/loadavg").Trim();
        return "n/a";
    }

    private static string ReadDotnetVersion()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            if (process is null)
                return "unknown";
            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(15000))
            {
                process.Kill(entireProcessTree: true);
                return "unknown";
            }

            return output;
        }
        catch
        {
            return "unknown";
        }
    }
}
