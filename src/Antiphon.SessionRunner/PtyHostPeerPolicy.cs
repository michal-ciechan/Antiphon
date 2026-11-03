using Antiphon.PtyHost.Client;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1653 S2: builds the connect policy that proves the peer behind a session's pipe is the host
/// this runner expects, and holds the test seams.
/// </summary>
internal static class PtyHostPeerPolicy
{
    public const string UnprovenPendingReason = "pty_host_peer_unproven";

    internal static AsyncLocal<Func<SafePipeHandle, PtyHostPeerCredentials?>?> PeerReaderOverride { get; } = new();
    internal static AsyncLocal<IUnixFileProbe?> FileProbeOverride { get; } = new();

    /// <summary>Sees the exact policy each pty-host connect passes (null under Off).</summary>
    internal static AsyncLocal<Action<PtyHostConnectPolicy?>?> ConnectPolicyObserver { get; } = new();

    internal static IUnixFileProbe FileProbe => FileProbeOverride.Value ?? PtyHostSocketDirectory.DefaultProbe;

    /// <summary>
    /// Null under Off (the unchecked connect). Otherwise the peer must be <paramref name="expectedPid"/>,
    /// on Linux owned by the runner's euid, and, when a start is known, alive at that start. Windows
    /// Enforce adds <c>CurrentUserOnly</c>; Windows Observe cannot (it would refuse, not log).
    /// </summary>
    internal static PtyHostConnectPolicy? Create(PtyHostPeerCheckMode mode, bool isWindows, bool isLinux,
        int expectedPid, DateTime? expectedStart, IProcessLivenessProbe probe, IUnixFileProbe fileProbe,
        Func<SafePipeHandle, PtyHostPeerCredentials?> reader, Action<string> onObserved)
    {
        if (mode == PtyHostPeerCheckMode.Off) return null;
        return new PtyHostConnectPolicy
        {
            CurrentUserOnly = isWindows && mode == PtyHostPeerCheckMode.Enforce,
            CheckSocketFile = isLinux ? path => PtyHostSocketDirectory.CheckSocketFile(path, fileProbe) : null,
            ReadPeer = reader,
            AcceptPeer = peer => peer is { Pid: > 0 } && peer.Pid == expectedPid
                && (!isLinux || peer.Uid == fileProbe.Euid())
                && (expectedStart is null || probe.IsAlive(peer.Pid, expectedStart.Value)),
            Observe = mode == PtyHostPeerCheckMode.Observe,
            OnObserved = onObserved,
        };
    }
}
