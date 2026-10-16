using Antiphon.HostCleanup.Checkpoints;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Antiphon.Checkpoints;

public sealed record ProcessIdentity(int Pid, long StartUtcTicks, string Host, string Boot, string PidNamespace);

public enum ProcessVerdict { AliveSame, Dead, ReusedPid, Unknown }

public sealed record ProcessObservation(ProcessVerdict Verdict, string Reason);

public class ProcessIdentityProbe
{
    private readonly CheckpointCleanupContext _context;
    public ProcessIdentityProbe(CheckpointCleanupContext? context = null) => _context = context ?? CheckpointCleanupDefaults.Context;
    public virtual ProcessIdentity Current() => Capture(_context.Io.CurrentProcessId);

    public virtual ProcessIdentity Capture(int pid)
    {
        var process = _context.Io.Process(pid);
        return new ProcessIdentity(pid, process.StartToken,
            _context.Io.MachineName, _context.Io.BootId, _context.Io.PidNamespace);
    }

    public virtual ProcessObservation Observe(ProcessIdentity? expected)
    {
        if (expected is null || expected.Pid <= 0 || expected.StartUtcTicks <= 0)
            return new(ProcessVerdict.Unknown, "identity-missing");
        try
        {
            if (expected.Host != _context.Io.MachineName || !SameBoot(expected.Boot, _context.Io.BootId)
                || expected.PidNamespace != _context.Io.PidNamespace)
                return new(ProcessVerdict.Unknown, "identity-foreign");
            var process = _context.Io.Process(expected.Pid);
            var (start, state, _) = process;
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

    private const string BootFormat = "yyyyMMddHHmm";

    // Windows boot readings within this distance are one boot, so a clock step cannot strand a
    // marker. Windows process start times are absolute: the start-token check, not the boot
    // reading, is what separates process generations there.
    private static readonly TimeSpan WindowsBootTolerance = TimeSpan.FromMinutes(5);

    private bool SameBoot(string recorded, string current)
    {
        if (string.Equals(recorded, current, StringComparison.Ordinal)) return true;
        if (_context.Io.Platform == CheckpointPlatform.Linux) return false;
        return TryParseBoot(recorded, out var a) && TryParseBoot(current, out var b)
            && (a - b).Duration() <= WindowsBootTolerance;
    }

    private static bool TryParseBoot(string? value, out DateTime boot) =>
        DateTime.TryParseExact(value, BootFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out boot);

}
