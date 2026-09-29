using System.Diagnostics;
using System.Globalization;

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
            if (expected.Host != Environment.MachineName || expected.Boot != BootId()
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

    private static string BootId()
    {
        if (OperatingSystem.IsLinux())
            return File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
        // A change or clock correction can only prevent cleanup, never authorize it.
        return (DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64))
            .UtcDateTime.ToString("yyyyMMddHHmm");
    }

    private static string PidNamespace() => OperatingSystem.IsLinux()
        ? new FileInfo("/proc/self/ns/pid").LinkTarget ?? "unknown"
        : "windows-local";
}
