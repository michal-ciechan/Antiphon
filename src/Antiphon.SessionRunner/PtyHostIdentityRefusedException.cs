namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1653: the peer behind a session's pipe did not prove it is this session's host. The
/// message is the fixed code only, never an id the peer claimed (I-7). An adoption refusal holds
/// the manifest's host as a pending row instead of killing it; a fresh-launch refusal tears down
/// only the host this launch spawned.
/// </summary>
public sealed class PtyHostIdentityRefusedException(string code) : InvalidOperationException(code)
{
    public const string MismatchCode = "pty_host_identity_mismatch";
    public const string UnprovenPendingReason = "pty_host_identity_unproven";

    public string Code { get; } = code;
}
