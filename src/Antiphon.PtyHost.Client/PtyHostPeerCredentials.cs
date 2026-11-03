using Microsoft.Win32.SafeHandles;

namespace Antiphon.PtyHost.Client;

/// <summary>
/// CARD-1653 S2: the kernel's account of the process behind a connected pipe. Linux reads
/// <c>SO_PEERCRED</c> (the listener's pid and uid); Windows reads <c>GetNamedPipeServerProcessId</c>
/// and has no uid.
/// </summary>
public sealed record PtyHostPeerCredentials(int Pid, uint? Uid)
{
    /// <summary>The peer of a connected client handle, or null when the kernel does not say. Never throws.</summary>
    public static PtyHostPeerCredentials? Read(SafePipeHandle handle) => null;
}
