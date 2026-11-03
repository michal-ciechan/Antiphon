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

    internal static PtyHostConnectPolicy? Create(PtyHostPeerCheckMode mode, bool isWindows, bool isLinux,
        int expectedPid, DateTime? expectedStart, IProcessLivenessProbe probe, IUnixFileProbe fileProbe,
        Func<SafePipeHandle, PtyHostPeerCredentials?> reader, Action<string> onObserved) => null;
}
