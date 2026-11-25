using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Antiphon.Agents.Pty;

internal static class LinuxSpawnCapture
{
    private const int ClockTicksPerSecond = 2; // Linux _SC_CLK_TCK.

    // This worker takes a PID only. Its result can be ignored after admission expires.
    internal static LinuxSpawnIdentity? Read(int pid)
    {
        try
        {
            var stat = ReadBounded("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat", 2048);
            var bootId = ReadBounded("/proc/sys/kernel/random/boot_id", 40);
            if (stat is null || bootId is null || !Guid.TryParseExact(bootId.TrimEnd('\n'), "D", out var boot) ||
                boot == Guid.Empty || ParseStat(stat) is not { } identity || identity.Pid != pid)
                return null;

            var systemStat = ReadBounded("/proc/stat", 1024 * 1024);
            const string bootTimePrefix = "\nbtime ";
            var at = systemStat?.IndexOf(bootTimePrefix, StringComparison.Ordinal) ?? -1;
            if (systemStat is null || at < 0)
                return null;
            var start = at + bootTimePrefix.Length;
            var end = systemStat.IndexOf('\n', start);
            var hz = Sysconf(ClockTicksPerSecond);
            if (end < 0 || hz <= 0 ||
                !long.TryParse(systemStat.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out var bootTime))
                return null;

            var seconds = Math.DivRem(checked((long)identity.Ticks), hz, out var ticks);
            var utc = DateTimeOffset.FromUnixTimeSeconds(checked(bootTime + seconds)).UtcDateTime
                .AddTicks(checked(ticks * TimeSpan.TicksPerSecond / hz));
            return new(pid, identity.Parent, boot, identity.Ticks, utc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    internal static (int Pid, int Parent, ulong Ticks)? ParseStat(string stat)
    {
        var open = stat.IndexOf(" (", StringComparison.Ordinal);
        var close = stat.LastIndexOf(')');
        if (open <= 0 || close <= open || close + 2 >= stat.Length || stat[close + 1] != ' ' || !stat.EndsWith('\n'))
            return null;

        var fields = stat[(close + 2)..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 50 || fields[0].Length != 1 || !"RSDTtIW".Contains(fields[0][0]) ||
            !int.TryParse(stat.AsSpan(0, open), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0 ||
            !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent) || parent <= 0 ||
            !uint.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out var flags) || (flags & 4) != 0 ||
            !ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks == 0)
            return null;

        return (pid, parent, ticks);
    }

    private static string? ReadBounded(string path, int bound)
    {
        using var stream = File.OpenRead(path);
        var bytes = new byte[bound];
        var count = stream.ReadAtLeast(bytes, bound, throwIfEndOfStream: false);
        return count < bound && bytes.AsSpan(0, count).IndexOf((byte)0) < 0
            ? Encoding.ASCII.GetString(bytes, 0, count)
            : null;
    }

    [DllImport("libc", EntryPoint = "sysconf")]
    private static extern long Sysconf(int name);
}
