using System.Diagnostics;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// What is actually running on this machine, as opposed to what the runner and the database each
/// believe. Three numbers, taken once per reconciliation sweep.
/// </summary>
/// <param name="PtyHostProcesses">Live <c>Antiphon.PtyHost</c> processes — one per detached session.</param>
/// <param name="ClaudeProcesses">Live <c>claude</c> processes — the agent children those hosts exist to hold.</param>
/// <param name="LivePids">
/// Every process id alive at the moment of the census, so a runner session's reported child pid can
/// be checked without a second enumeration (and without <c>Process.GetProcessById</c>'s throw-on-miss).
/// </param>
public sealed record PtyHostCensus(int PtyHostProcesses, int ClaudeProcesses, IReadOnlySet<int> LivePids)
{
    /// <summary>The census that says nothing — used when the probe is unavailable or throws.</summary>
    public static PtyHostCensus Unavailable { get; } = new(-1, -1, new HashSet<int>());

    /// <summary>True when the probe could not read the process table; every threshold is skipped.</summary>
    public bool IsUnavailable => PtyHostProcesses < 0;
}

/// <summary>
/// Reads the process table. An interface purely so the reconciliation tests can state a census
/// instead of spawning three dozen processes to produce one.
/// </summary>
public interface IPtyHostCensusProbe
{
    PtyHostCensus Take();
}

/// <summary>
/// The real probe: ONE enumeration of the process table per call.
///
/// <para>Cost is a few milliseconds against a sweep that already makes an HTTP round trip to the
/// runner and several database queries, and it is what turns the census from a number the server
/// could have computed into one it did (CARD-0102: the log line "46 runner sessions with no DB row
/// at all" was printed four hours before a human found the leak by hand, and nothing alerted).</para>
///
/// <para>Never throws. A process that exits between enumeration and inspection is normal, and a
/// census that failed is <see cref="PtyHostCensus.Unavailable"/> — which suppresses every threshold
/// rather than inventing a zero. "I could not look" must never read as "nothing is there": that is
/// the same mistake as treating an unreachable runner as an empty session list.</para>
/// </summary>
public sealed class PtyHostCensusProbe : IPtyHostCensusProbe
{
    /// <summary>The detached pty-host, by process name (no extension, as the API reports it).</summary>
    private const string PtyHostProcessName = "Antiphon.PtyHost";

    /// <summary>The agent child a host exists to hold.</summary>
    private const string ClaudeProcessName = "claude";

    public PtyHostCensus Take()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PtyHostCensus.Unavailable;
        }

        var hosts = 0;
        var claudes = 0;
        var pids = new HashSet<int>(processes.Length);
        foreach (var process in processes)
        {
            try
            {
                pids.Add(process.Id);
                var name = process.ProcessName;
                if (string.Equals(name, PtyHostProcessName, StringComparison.OrdinalIgnoreCase)) hosts++;
                else if (string.Equals(name, ClaudeProcessName, StringComparison.OrdinalIgnoreCase)) claudes++;
            }
            catch
            {
                // Exited between enumeration and inspection, or access denied on another user's
                // process. Both are ordinary; neither is evidence of anything.
            }
            finally
            {
                process.Dispose();
            }
        }

        return new PtyHostCensus(hosts, claudes, pids);
    }
}

/// <summary>
/// The repeat gate for the census alert, in memory for this server's uptime — the same shape as
/// <see cref="SessionReAdoptionState"/> and <see cref="RunnerReachabilityState"/> beside it.
///
/// <para>It exists because of §6.5 of the coverage plan, which is a lesson rather than a
/// preference: CARD-0101's refusal fault fired 37 identical Warnings over three hours, nobody
/// acted, and then the incident stream went quiet while the fault kept running — so "no new
/// incidents" was read as "fixed". A 15-second sweep raising on every tick would produce 240 rows
/// an hour and be worth exactly as much.</para>
///
/// <para>So: at most one raise per repeat window while the condition holds — and an ESCALATION
/// always goes through immediately. A Warning that has become Critical is news no matter how
/// recently the Warning was sent, and a gate with no escalation path is how the noise proves
/// nothing by its absence.</para>
/// </summary>
public sealed class PtyHostCensusAlertState
{
    private readonly object _gate = new();
    private DateTime _lastRaisedUtc = DateTime.MinValue;
    private int _lastSeverity = -1;

    /// <summary>
    /// Registers an intent to raise at <paramref name="severity"/> and reports whether it should
    /// go out. Clears the window when the condition stops holding, so a recurrence after a quiet
    /// period alerts immediately instead of waiting out a window it did not cause.
    /// </summary>
    public bool ShouldRaise(DateTime nowUtc, int severity, TimeSpan repeatWindow)
    {
        lock (_gate)
        {
            var escalating = severity > _lastSeverity;
            if (!escalating && nowUtc - _lastRaisedUtc < repeatWindow)
                return false;

            _lastRaisedUtc = nowUtc;
            _lastSeverity = severity;
            return true;
        }
    }

    /// <summary>The condition no longer holds; the next occurrence is news again.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _lastRaisedUtc = DateTime.MinValue;
            _lastSeverity = -1;
        }
    }
}
