using System.Diagnostics;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0604 D-17. Decides whether this runner may advertise the Linux custody backend at all.
///
/// Advertising custody it cannot actually perform is the failure mode CARD-0598 is about: the
/// server admits a SourceLanding Mutation, the execution is reserved, and only then does the
/// producer discover it has no containment -- leaving a reserved row with no possible receipt.
/// So the probe is positive and live, not a platform check: the custody root must exist for the
/// cgroup version this kernel actually presents, both root-owned helpers must succeed through
/// `sudo -n` (they create and remove an empty probe cgroup), and this process must NOT already
/// be running under PR_SET_NO_NEW_PRIVS -- because if it were, the very `sudo` the placement
/// shim depends on would be inert. Any of those failing means <see cref="Detect"/> returns null
/// and nothing is advertised (G-28).
/// </summary>
public static class LinuxCgroupCustodyProbe
{
    public const string EnterHelper = "/usr/local/bin/antiphon-custody-enter";
    public const string KillHelper = "/usr/local/bin/antiphon-custody-kill";
    public const string CustodyRootName = "antiphon-custody";

    public static string? Detect(ILinuxCustodyEnvironment? environment = null)
    {
        var env = environment ?? LinuxCustodyEnvironment.Real;
        if (!env.IsLinux) return null;
        if (!env.CustodyRootExists(CustodyRootName)) return null;
        // /proc/self/status reports NoNewPrivs: 1 once the bit is set, and it is irreversible.
        // A runner started under it can never reach sudo, so it must not claim it can.
        if (env.NoNewPrivs != 0) return null;
        if (!env.HelperProbeSucceeds(EnterHelper)) return null;
        if (!env.HelperProbeSucceeds(KillHelper)) return null;
        return SessionRunner.Contracts.VerificationCustodyBackends.LinuxCgroup;
    }
}

/// <summary>Seam for the four real-world reads the probe makes; production uses the real one.</summary>
public interface ILinuxCustodyEnvironment
{
    bool IsLinux { get; }

    /// <summary>True when the entrypoint-prepared root exists for the detected cgroup version.</summary>
    bool CustodyRootExists(string rootName);

    /// <summary>The `NoNewPrivs:` value from /proc/self/status, or -1 when it cannot be read.</summary>
    int NoNewPrivs { get; }

    /// <summary>`sudo -n &lt;helper&gt; --probe` exited 0.</summary>
    bool HelperProbeSucceeds(string helperPath);
}

internal sealed class LinuxCustodyEnvironment : ILinuxCustodyEnvironment
{
    public static readonly LinuxCustodyEnvironment Real = new();

    public bool IsLinux => OperatingSystem.IsLinux();

    public bool CustodyRootExists(string rootName) =>
        File.Exists("/sys/fs/cgroup/cgroup.controllers")
            ? Directory.Exists($"/sys/fs/cgroup/{rootName}")
            : Directory.Exists($"/sys/fs/cgroup/pids/{rootName}")
                && Directory.Exists($"/sys/fs/cgroup/freezer/{rootName}");

    public int NoNewPrivs
    {
        get
        {
            try
            {
                foreach (var line in File.ReadAllLines("/proc/self/status"))
                    if (line.StartsWith("NoNewPrivs:", StringComparison.Ordinal)
                        && int.TryParse(line.AsSpan("NoNewPrivs:".Length).Trim(), out var value))
                        return value;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return -1;
        }
    }

    public bool HelperProbeSucceeds(string helperPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("sudo")
            {
                ArgumentList = { "-n", helperPath, "--probe" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null) return false;
            if (!process.WaitForExit(TimeSpan.FromSeconds(20)))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
