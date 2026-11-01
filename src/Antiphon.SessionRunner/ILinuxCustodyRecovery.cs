using Antiphon.Agents.Pty;

namespace Antiphon.SessionRunner;

/// <summary>Production dead-host release. Uses the installed helper for the recorded container only.</summary>
internal sealed class LinuxInstalledCustodyRecovery(IProcessLivenessProbe probe) : ILinuxCustodyRecovery
{
    private const string NamespaceMarker = "/run/antiphon-custody/namespace";

    public bool HostGone(int pid, DateTime startUtc) => !probe.IsAlive(pid, startUtc);

    public Guid ReadNamespace()
    {
        try
        {
            var text = File.ReadAllText(NamespaceMarker).Trim();
            return Guid.TryParseExact(text, "D", out var id) ? id : Guid.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Guid.Empty;
        }
    }

    public PtyStopCallback? StopSignal { get; set; }

    public PtyTerminationObservation Kill(Guid containerId) =>
        new LinuxCgroupContainment(containerId) { StopSignal = StopSignal }.Terminate();

    public IReadOnlyList<int> ReadTreePids(Guid containerId) =>
        new LinuxCgroupContainment(containerId).ReadActive().Pids;
}

/// <summary>
/// CARD-1614. Explicit dead-host release for one recorded Linux container.
/// Production uses the installed helper. Tests inject a recording fake.
/// A null recovery never authorizes a kill.
/// </summary>
public interface ILinuxCustodyRecovery
{
    PtyStopCallback? StopSignal { get => null; set { } }
    bool HostGone(int pid, DateTime startUtc);

    Guid ReadNamespace();

    PtyTerminationObservation Kill(Guid containerId);

    /// <summary>Throws <see cref="IOException"/> when the tree cannot be read. An empty list is observed zero.</summary>
    IReadOnlyList<int> ReadTreePids(Guid containerId);
}
