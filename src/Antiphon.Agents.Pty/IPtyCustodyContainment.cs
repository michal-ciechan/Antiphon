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

    /// <summary>
    /// Accounting file for one execution inside one namespace. Callers pass the namespace;
    /// there is no fallback to the global custody root.
    /// </summary>
    public static string ExecutionProcsPath(Guid namespaceId, Guid containerId) =>
        LinuxCustodyOperations.ResolveProcsPath(namespaceId, containerId);

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

/// <summary>
/// Where the real reader looks. Production is the host cgroup and the runtime marker.
/// A test may supply a private root. There is no environment override.
/// </summary>
internal readonly record struct LinuxCustodyLayout(
    string CgroupRoot,
    string NamespaceMarkerPath,
    string? NamespaceRecordPath)
{
    public static LinuxCustodyLayout Production { get; } =
        new("/sys/fs/cgroup", "/run/antiphon-custody/namespace", null);
}

internal sealed class LinuxCustodyOperations : ILinuxCustodyOperations
{
    public static readonly LinuxCustodyOperations Real = new(LinuxCustodyLayout.Production);

    private readonly LinuxCustodyLayout _layout;

    public LinuxCustodyOperations(LinuxCustodyLayout layout) => _layout = layout;

    public IReadOnlyList<int> ReadTreePids(Guid containerId)
    {
        var path = TreeProcsPath(containerId);
        // A missing tree, a missing procs file, a link, or a read that fails is unknown.
        // Unknown must never look like zero. An empty procs file is observed zero.
        // There is no fallback to the global custody root.
        if (IsReparse(path))
            throw Unreadable();

        try
        {
            var pids = new List<int>();
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.Length == 0)
                    continue;
                if (!int.TryParse(line, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var pid) || pid <= 0)
                    throw Unreadable();
                pids.Add(pid);
            }

            return pids;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (ex.Message == UnreadableMessage)
                throw;
            throw Unreadable(ex);
        }
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

    /// <summary>Accounting file for one execution inside the given namespace.</summary>
    public static string ResolveProcsPath(Guid namespaceId, Guid containerId) =>
        FormatProcsPath(LinuxCustodyLayout.Production, namespaceId, containerId);

    private string TreeProcsPath(Guid containerId) =>
        FormatProcsPath(_layout, RequireNamespace(), containerId);

    private Guid RequireNamespace()
    {
        var marker = _layout.NamespaceMarkerPath;
        if (string.IsNullOrWhiteSpace(marker))
            throw Unreadable();
        if (NodeExists(marker))
        {
            if (!TryReadPlainGuid(marker, out var fromMarker))
                throw Unreadable();
            return fromMarker;
        }

        var record = _layout.NamespaceRecordPath;
        if (!string.IsNullOrWhiteSpace(record) && NodeExists(record))
        {
            if (!TryReadNamespaceRecord(record, out var fromRecord))
                throw Unreadable();
            return fromRecord;
        }

        throw Unreadable();
    }

    private static string FormatProcsPath(LinuxCustodyLayout layout, Guid namespaceId, Guid containerId)
    {
        var name = LinuxCgroupContainment.CustodyRootName;
        var v2 = File.Exists(Path.Combine(layout.CgroupRoot, "cgroup.controllers"));
        return v2
            ? Path.Combine(layout.CgroupRoot, name, namespaceId.ToString("D"), containerId.ToString("D"), "tree", "cgroup.procs")
            : Path.Combine(layout.CgroupRoot, "pids", name, namespaceId.ToString("D"), containerId.ToString("D"), "tree", "cgroup.procs");
    }

    private const string UnreadableMessage = "verification_custody_tree_unreadable";

    private static IOException Unreadable(Exception? inner = null) =>
        inner is null ? new IOException(UnreadableMessage) : new IOException(UnreadableMessage, inner);

    private static bool NodeExists(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists || info.LinkTarget is not null || Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsReparse(string path)
    {
        try
        {
            // A missing path reports attributes as -1, and every bit of -1 is set, including
            // ReparsePoint. That is not a link. Only a real symlink is refused.
            var info = new FileInfo(path);
            if (info.LinkTarget is not null)
                return true;
            if (!info.Exists)
                return false;
            var attributes = info.Attributes;
            return (int)attributes >= 0 && (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryReadPlainGuid(string path, out Guid id)
    {
        id = Guid.Empty;
        if (!TryReadPlainText(path, out var text))
            return false;
        return Guid.TryParseExact(text.Trim(), "D", out id) && id != Guid.Empty;
    }

    private static bool TryReadNamespaceRecord(string path, out Guid id)
    {
        id = Guid.Empty;
        if (!TryReadPlainText(path, out var text))
            return false;
        foreach (var key in new[] { "\"namespaceId\"", "\"NamespaceId\"" })
        {
            var at = text.IndexOf(key, StringComparison.Ordinal);
            if (at < 0)
                continue;
            var colon = text.IndexOf(':', at + key.Length);
            if (colon < 0)
                continue;
            var open = text.IndexOf('"', colon + 1);
            if (open < 0)
                continue;
            var close = text.IndexOf('"', open + 1);
            if (close < 0)
                continue;
            var value = text.Substring(open + 1, close - open - 1);
            if (Guid.TryParseExact(value, "D", out id) && id != Guid.Empty)
                return true;
        }

        return false;
    }

    private static bool TryReadPlainText(string path, out string text)
    {
        text = "";
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null || !info.Exists)
                return false;
            var attributes = info.Attributes;
            if ((int)attributes >= 0 && (attributes & FileAttributes.ReparsePoint) != 0)
                return false;
            text = File.ReadAllText(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
