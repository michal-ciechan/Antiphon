using System.Diagnostics;

namespace Antiphon.Agents.Pty;

/// <summary>
/// CARD-0604 D-17. The OS mechanism that holds a tracked execution's whole process tree.
///
/// There are exactly two, and they answer the same four questions in very different ways:
/// Windows uses the retained spawn job object (breakaway denied in the kernel, accounting read
/// from the job); Linux uses a root-owned cgroup the child is placed in before it exists, by a
/// setuid-inert shim the child cannot re-enter. Both are "containment the tracked tree cannot
/// leave", which is the property CARD-0598 asked for; neither is a PID census, which CARD-0598
/// explicitly rejected as authority.
/// </summary>
public interface IPtyCustodyContainment
{
    /// <summary>Identity of this container, recorded on the receipt as the host's ContainerId.</summary>
    Guid ContainerId { get; }

    /// <summary>
    /// Rewrite the launch so the child lands inside the container. Windows returns it unchanged
    /// (placement is part of the suspended spawn); Linux prefixes the root-owned shim, which is
    /// the only way uid 1654 can reach a root-owned cgroup at all.
    /// </summary>
    PtyTrackedLaunch Place(PtyTrackedLaunch launch);

    /// <summary>
    /// What the container itself reports is still alive. Never a guess, never a process-name
    /// match: a read that cannot be performed throws rather than returning zero.
    /// </summary>
    PtyCustodyPopulation ReadActive();

    /// <summary>Terminate the whole container. Idempotent; reports whether the OS agreed.</summary>
    PtyTerminationObservation Terminate();
}

/// <summary>The executable and argument vector a tracked launch will actually spawn.</summary>
public sealed record PtyTrackedLaunch(string App, IReadOnlyList<string> CommandLine);

/// <summary>Live population of a container. <paramref name="Pids"/> is empty where the mechanism only counts.</summary>
public sealed record PtyCustodyPopulation(uint Count, IReadOnlyList<int> Pids);

/// <summary>
/// Windows, unchanged. Every observation is the same job-object call the Windows lane has made
/// since CARD-0478; this type only puts a name on the seam so Linux can offer the other half.
/// </summary>
internal sealed class WindowsJobContainment(ModernConPtyConnection connection, Guid containerId)
    : IPtyCustodyContainment
{
    public Guid ContainerId { get; } = containerId;

    // The job is created and assigned as part of the suspended spawn, before the first
    // instruction of the child runs. There is nothing to rewrite.
    public PtyTrackedLaunch Place(PtyTrackedLaunch launch) => launch;

    public PtyCustodyPopulation ReadActive() => new(connection.QueryActiveProcesses(), []);

    public PtyTerminationObservation Terminate()
    {
        connection.TerminateCustodyJob();
        return connection.LastTermination ?? new PtyTerminationObservation(false, 0);
    }
}

/// <summary>
/// Linux, CARD-0604 D-17. This type deliberately creates nothing and owns no privilege: the
/// custody root was made by the container entrypoint running as root, the per-execution cgroup
/// is made by the root-owned shim, and every write into it happens under `sudo -n` against one
/// of exactly two allow-listed commands. All this class may do is rewrite the launch, read a
/// world-readable procs file, and ask the kill helper to empty the tree.
/// </summary>
public sealed class LinuxCgroupContainment : IPtyCustodyContainment
{
    public const string SudoExe = "sudo";
    public const string EnterHelper = "/usr/local/bin/antiphon-custody-enter";
    public const string KillHelper = "/usr/local/bin/antiphon-custody-kill";
    public const string CustodyRootName = "antiphon-custody";

    private readonly ILinuxCustodyOperations _operations;

    public LinuxCgroupContainment(Guid containerId, ILinuxCustodyOperations? operations = null)
    {
        if (containerId == Guid.Empty) throw new ArgumentException("Container id must not be empty.", nameof(containerId));
        ContainerId = containerId;
        _operations = operations ?? LinuxCustodyOperations.Real;
    }

    public Guid ContainerId { get; }

    /// <summary>
    /// `sudo -n antiphon-custody-enter &lt;id&gt; -- &lt;exe&gt; &lt;args&gt;`. The `--` is what keeps an
    /// argument of the tracked command from being read as an argument of the shim.
    /// </summary>
    public PtyTrackedLaunch Place(PtyTrackedLaunch launch)
    {
        var args = new List<string> { "-n", EnterHelper, ContainerId.ToString("D"), "--", launch.App };
        args.AddRange(launch.CommandLine);
        return new PtyTrackedLaunch(SudoExe, args);
    }

    public PtyCustodyPopulation ReadActive()
    {
        var pids = _operations.ReadTreePids(ContainerId);
        return new((uint)pids.Count, pids);
    }

    public PtyTerminationObservation Terminate() => _operations.Kill(ContainerId);
}

/// <summary>Seam over the two real-world effects, so the Windows-executing tests can drive both.</summary>
public interface ILinuxCustodyOperations
{
    /// <summary>Pids currently in the execution's tree. Throws if the tree cannot be read.</summary>
    IReadOnlyList<int> ReadTreePids(Guid containerId);

    /// <summary>`sudo -n antiphon-custody-kill &lt;id&gt;`.</summary>
    PtyTerminationObservation Kill(Guid containerId);
}

internal sealed class LinuxCustodyOperations : ILinuxCustodyOperations
{
    public static readonly LinuxCustodyOperations Real = new();

    public IReadOnlyList<int> ReadTreePids(Guid containerId)
    {
        var path = TreeProcsPath(containerId);
        // A tree directory that is gone after a successful kill is an empty tree. A tree that
        // cannot be read for any other reason is unknown, and unknown must never look like zero.
        if (!File.Exists(path)) return Directory.Exists(Path.GetDirectoryName(path)!)
            ? throw new IOException("verification_custody_tree_unreadable") : [];
        return File.ReadAllLines(path)
            .Where(line => line.Length != 0)
            .Select(int.Parse)
            .ToArray();
    }

    public PtyTerminationObservation Kill(Guid containerId)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(LinuxCgroupContainment.SudoExe)
            {
                ArgumentList = { "-n", LinuxCgroupContainment.KillHelper, containerId.ToString("D") },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null) return new(false, 0);
            // The helper's own drain is bounded at 30 s; allow it to finish and report rather
            // than racing it with a second kill.
            if (!process.WaitForExit(TimeSpan.FromSeconds(45)))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new(false, 0);
            }
            return new(process.ExitCode == 0, process.ExitCode);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new(false, 0);
        }
    }

    private static string TreeProcsPath(Guid containerId)
    {
        var id = containerId.ToString("D");
        return File.Exists("/sys/fs/cgroup/cgroup.controllers")
            ? $"/sys/fs/cgroup/{LinuxCgroupContainment.CustodyRootName}/{id}/tree/cgroup.procs"
            : $"/sys/fs/cgroup/pids/{LinuxCgroupContainment.CustodyRootName}/{id}/tree/cgroup.procs";
    }
}
