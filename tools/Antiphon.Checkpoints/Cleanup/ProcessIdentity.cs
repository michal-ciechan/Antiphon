using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Antiphon.Checkpoints;

public sealed record ProcessIdentity(int Pid, long StartUtcTicks, string Host, string Boot, string PidNamespace);

public enum ProcessVerdict { AliveSame, Dead, ReusedPid, Unknown }

public sealed record ProcessObservation(ProcessVerdict Verdict, string Reason);

public class ProcessIdentityProbe
{
    public virtual ProcessIdentity Current() => Capture(Environment.ProcessId);

    public virtual ProcessIdentity Capture(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return new ProcessIdentity(pid, StartToken(pid, process).Token,
            Environment.MachineName, BootId(), PidNamespace());
    }

    public virtual ProcessObservation Observe(ProcessIdentity? expected)
    {
        if (expected is null || expected.Pid <= 0 || expected.StartUtcTicks <= 0)
            return new(ProcessVerdict.Unknown, "identity-missing");
        try
        {
            if (expected.Host != Environment.MachineName || !SameBoot(expected.Boot, BootId())
                || expected.PidNamespace != PidNamespace())
                return new(ProcessVerdict.Unknown, "identity-foreign");
            using var process = Process.GetProcessById(expected.Pid);
            var (start, state) = StartToken(expected.Pid, process);
            if (start != expected.StartUtcTicks)
                return new(ProcessVerdict.ReusedPid, "identity-reused");
            return state is 'Z' or 'X' || process.HasExited
                ? new(ProcessVerdict.Dead, "identity-dead")
                : new(ProcessVerdict.AliveSame, "identity-alive");
        }
        catch (ArgumentException)
        {
            return new(ProcessVerdict.Dead, "identity-dead");
        }
        catch (FileNotFoundException)
        {
            // The process may exit after GetProcessById and before /proc is read.
            return new(ProcessVerdict.Dead, "identity-dead");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException
                                   or System.ComponentModel.Win32Exception)
        {
            return new(ProcessVerdict.Unknown, "identity-unknown:" + ex.GetType().Name);
        }
    }

    private static (long Token, char State) StartToken(int pid, Process process)
    {
        if (!OperatingSystem.IsLinux())
            return (process.StartTime.ToUniversalTime().Ticks, 'R');
        // /proc starttime is a kernel generation in clock ticks since boot. Unlike
        // Process.StartTime's UTC conversion it is byte-stable across observers.
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var endName = stat.LastIndexOf(')');
        if (endName < 0 || endName + 2 >= stat.Length)
            throw new IOException("invalid proc stat");
        var fields = stat[(endName + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length <= 19 || fields[0].Length != 1
            || !long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var start))
            throw new IOException("invalid proc starttime");
        return (start, fields[0][0]);
    }

    private const string BootFormat = "yyyyMMddHHmm";

    // Windows boot readings within this distance are one boot, so a clock step cannot strand a
    // marker. Windows process start times are absolute: the start-token check, not the boot
    // reading, is what separates process generations there.
    private static readonly TimeSpan WindowsBootTolerance = TimeSpan.FromMinutes(5);

    private static bool SameBoot(string recorded, string current)
    {
        if (string.Equals(recorded, current, StringComparison.Ordinal)) return true;
        if (OperatingSystem.IsLinux()) return false;
        return TryParseBoot(recorded, out var a) && TryParseBoot(current, out var b)
            && (a - b).Duration() <= WindowsBootTolerance;
    }

    private static bool TryParseBoot(string? value, out DateTime boot) =>
        DateTime.TryParseExact(value, BootFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out boot);

    private static string BootId()
    {
        if (OperatingSystem.IsLinux())
            return File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
        if (OperatingSystem.IsWindows())
        {
            // The kernel's recorded boot time (CIM's LastBootUpTime). Now minus uptime drifted
            // with every clock slew (29 s after 20 days), so earlier markers read as foreign.
            var info = new byte[48];
            var status = NtQuerySystemInformation(SystemTimeOfDayInformation, info, info.Length, out _);
            if (status != 0)
                throw new InvalidOperationException($"boot time query failed: 0x{status:X8}");
            return DateTime.FromFileTimeUtc(BitConverter.ToInt64(info, 0))
                .ToString(BootFormat, CultureInfo.InvariantCulture);
        }
        // A change or clock correction can only prevent cleanup, never authorize it.
        return (DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64))
            .UtcDateTime.ToString(BootFormat, CultureInfo.InvariantCulture);
    }

    private const int SystemTimeOfDayInformation = 3;

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, byte[] info, int length, out int returned);

    private static string PidNamespace() => OperatingSystem.IsLinux()
        ? new FileInfo("/proc/self/ns/pid").LinkTarget ?? "unknown"
        : "windows-local";
}
