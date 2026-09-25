using System.Runtime.InteropServices;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0589 D-5: the host's live available physical memory, read at grant time so the standing
/// floor (always-on sessions, their nodes, the OS) is measured rather than assumed.
/// </summary>
public interface IHostMemoryProbe
{
    /// <summary>Available bytes, or null when the host cannot say (an unknown answer never refuses).</summary>
    long? AvailableBytes { get; }
}

public sealed class SystemHostMemoryProbe : IHostMemoryProbe
{
    public long? AvailableBytes
    {
        get
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    return WindowsAvailable();
                if (OperatingSystem.IsLinux())
                    return ParseMemAvailable(File.ReadLines("/proc/meminfo"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
            {
            }
            return null;
        }
    }

    /// <summary>
    /// <c>MemAvailable</c> in kB from <c>/proc/meminfo</c>. Inside the server2 runner container it
    /// is the host's figure (no cgroup memory cap), which is the number that matters.
    /// </summary>
    internal static long? ParseMemAvailable(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                continue;
            var parts = line["MemAvailable:".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 && long.TryParse(parts[0], out var kb) ? kb * 1024 : null;
        }
        return null;
    }

    private static long? WindowsAvailable()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.AvailPhys : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
