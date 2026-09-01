using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// Live Win32_Process snapshot for CARD-0298. Tests never call this seam.
/// A WMI query failure throws; it is never rewritten as an empty process list.
/// </summary>
public sealed class WindowsZombieProcessCensus : IZombieProcessCensus
{
    public async Task<IReadOnlyList<ZombieOsProcess>> SnapshotAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Zombie census WMI snapshot requires Windows.");

        cancellationToken.ThrowIfCancellationRequested();
        var first = QueryProcesses();
        var sample1 = first.ToDictionary(p => p.Process.ProcessId, p => p.KernelPlusUser);
        var clock = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        clock.Stop();

        Dictionary<int, long> sample2;
        try
        {
            sample2 = QueryProcesses().ToDictionary(p => p.Process.ProcessId, p => p.KernelPlusUser);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Zombie census WMI CPU resample failed.", ex);
        }

        var cores = Math.Max(1, Environment.ProcessorCount);
        var interval100ns = Math.Max(1, clock.Elapsed.Ticks);
        var result = new List<ZombieOsProcess>(first.Count);
        foreach (var row in first)
        {
            double? cpu = null;
            if (sample2.TryGetValue(row.Process.ProcessId, out var later))
            {
                var delta = Math.Max(0, later - row.KernelPlusUser);
                cpu = Math.Round((delta / (double)interval100ns) * 100.0 / cores, 1);
            }

            result.Add(row.Process with { CpuDeltaPercent = cpu });
        }

        return result;
    }

    [SupportedOSPlatform("windows")]
    private static List<(ZombieOsProcess Process, long KernelPlusUser)> QueryProcesses()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine, CreationDate, WorkingSetSize, KernelModeTime, UserModeTime FROM Win32_Process");
            using var results = searcher.Get();
            var list = new List<(ZombieOsProcess, long)>(results.Count);
            foreach (ManagementObject mo in results)
            {
                using (mo)
                {
                    var pid = ReadInt(mo, "ProcessId");
                    if (pid is null or <= 0)
                        continue;
                    var kernel = ReadLong(mo, "KernelModeTime") ?? 0;
                    var user = ReadLong(mo, "UserModeTime") ?? 0;
                    DateTimeOffset? created = null;
                    var rawCreated = ReadString(mo, "CreationDate");
                    if (!string.IsNullOrEmpty(rawCreated))
                    {
                        try
                        {
                            created = DateTime.SpecifyKind(
                                ManagementDateTimeConverter.ToDateTime(rawCreated), DateTimeKind.Local)
                                .ToUniversalTime();
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            created = null;
                        }
                    }

                    list.Add((new ZombieOsProcess(
                        ProcessId: pid.Value,
                        ParentProcessId: ReadInt(mo, "ParentProcessId") ?? 0,
                        Name: ReadString(mo, "Name") ?? "",
                        ExecutablePath: ReadString(mo, "ExecutablePath") ?? "",
                        CommandLine: ReadString(mo, "CommandLine") ?? "",
                        Cwd: "",
                        CreationUtc: created,
                        WorkingSetBytes: ReadLong(mo, "WorkingSetSize") ?? 0,
                        CpuDeltaPercent: null), kernel + user));
                }
            }

            return list;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Zombie census WMI process query failed.", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static int? ReadInt(ManagementBaseObject obj, string name)
    {
        try
        {
            var value = obj[name];
            if (value is null or DBNull)
                return null;
            return Convert.ToInt32(value);
        }
        catch (Exception ex) when (ex is ManagementException or InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static long? ReadLong(ManagementBaseObject obj, string name)
    {
        try
        {
            var value = obj[name];
            if (value is null or DBNull)
                return null;
            return Convert.ToInt64(value);
        }
        catch (Exception ex) when (ex is ManagementException or InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadString(ManagementBaseObject obj, string name)
    {
        try
        {
            var value = obj[name];
            return value is null or DBNull ? null : Convert.ToString(value);
        }
        catch (Exception ex) when (ex is ManagementException or InvalidCastException)
        {
            return null;
        }
    }
}
