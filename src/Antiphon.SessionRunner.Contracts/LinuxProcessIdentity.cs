using System.Globalization;

namespace Antiphon.SessionRunner;

/// <summary>Kernel process generation, stable across observers and distinct across boots.</summary>
public sealed record LinuxProcessIdentity(string BootId, long StartTicks)
{
    public static LinuxProcessIdentity? TryRead(int pid)
    {
        if (!OperatingSystem.IsLinux() || pid <= 0) return null;
        try
        {
            var boot = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return Guid.TryParse(boot, out _) && TryParseStartTicks(stat, out var ticks)
                ? new(boot, ticks) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static bool TryParseStartTicks(string stat, out long ticks)
    {
        ticks = 0;
        // comm (field 2) can contain spaces and parentheses. Only the final ')' ends it.
        var endName = stat.LastIndexOf(')');
        if (endName < 0 || endName + 2 >= stat.Length) return false;
        var fields = stat[(endName + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 19 && fields[0].Length == 1
            && long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out ticks)
            && ticks >= 0;
    }
}
