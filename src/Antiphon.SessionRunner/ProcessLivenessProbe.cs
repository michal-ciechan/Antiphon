using System.Diagnostics;

namespace Antiphon.SessionRunner;

/// <summary>
/// Answers "is the process behind this session actually alive?" — injectable so the liveness
/// sweep is testable without killing real processes.
/// </summary>
public interface IProcessLivenessProbe
{
    /// <param name="pid">The session's recorded process id.</param>
    /// <param name="startedAt">When the session's process was started (UTC), used to detect PID reuse.</param>
    bool IsAlive(int pid, DateTime startedAt);
}

public sealed class SystemProcessLivenessProbe : IProcessLivenessProbe
{
    // A process found under the session's PID but started this much later than the session is a
    // DIFFERENT process wearing a recycled id — treat the session's process as dead.
    private static readonly TimeSpan PidReuseTolerance = TimeSpan.FromMinutes(2);

    public bool IsAlive(int pid, DateTime startedAt)
    {
        if (pid <= 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
                return false;

            return process.StartTime.ToUniversalTime() <= startedAt.ToUniversalTime() + PidReuseTolerance;
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
        catch (InvalidOperationException)
        {
            return false; // exited while we were looking
        }
        catch (Exception)
        {
            return true; // access denied etc. — assume alive rather than kill state on a guess
        }
    }
}
