using System.Diagnostics;

namespace Antiphon.SessionRunner;

/// <summary>
/// Answers "how much CPU time has the process behind this session consumed?" — injectable so the
/// CPU spin watchdog is testable without real spinning processes. Sibling of
/// <see cref="IProcessLivenessProbe"/>, with the same PID-reuse guard.
/// </summary>
public interface IProcessCpuProbe
{
    /// <param name="pid">The session's recorded child process id.</param>
    /// <param name="startedAt">When the session's process was started (UTC), used to detect PID reuse.</param>
    /// <returns>The process's cumulative CPU time, or null when it cannot be trusted (process gone,
    /// PID recycled, access denied) — a null sample must never contribute to a kill decision.</returns>
    TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt);
}

public sealed class SystemProcessCpuProbe : IProcessCpuProbe
{
    // Same tolerance as SystemProcessLivenessProbe: a process under this PID that started much
    // later than the session is a DIFFERENT process wearing a recycled id.
    private static readonly TimeSpan PidReuseTolerance = TimeSpan.FromMinutes(2);

    public TimeSpan? TryGetTotalCpuTime(int pid, DateTime startedAt)
    {
        if (pid <= 0)
            return null;

        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
                return null;
            if (process.StartTime.ToUniversalTime() > startedAt.ToUniversalTime() + PidReuseTolerance)
                return null; // recycled PID — not the session's process
            return process.TotalProcessorTime;
        }
        catch (Exception)
        {
            // No such process / exited mid-look / access denied. Unlike the liveness probe (which
            // fails "alive" to avoid killing state on a guess), a CPU sample we can't take simply
            // must not count toward a kill — null achieves that on every failure.
            return null;
        }
    }
}
