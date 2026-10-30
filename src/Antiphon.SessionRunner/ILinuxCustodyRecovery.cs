using Antiphon.Agents.Pty;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1614. Explicit dead-host release for one recorded Linux container.
/// Production uses the installed helper. Tests inject a recording fake.
/// A null recovery never authorizes a kill.
/// </summary>
public interface ILinuxCustodyRecovery
{
    bool HostGone(int pid, DateTime startUtc);

    Guid ReadNamespace();

    PtyTerminationObservation Kill(Guid containerId);

    /// <summary>Throws <see cref="IOException"/> when the tree cannot be read. An empty list is observed zero.</summary>
    IReadOnlyList<int> ReadTreePids(Guid containerId);
}
