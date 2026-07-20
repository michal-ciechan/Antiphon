namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Result of probing a Claude process for remote-control bridge health.
/// Calibration (2026-07-20, 57 consecutive 30s samples on two idle sessions): an idle session
/// with a live bridge holds 2-3 established Anthropic connections continuously and NEVER dipped
/// to zero, while `.claude/sessions/&lt;pid&gt;.json` `updatedAt` never changes during idleness (it
/// is a status-transition stamp, not a heartbeat). Connection count is therefore the liveness
/// signal; the session file's bridgeSessionId is only the "was armed" indicator.
/// </summary>
public sealed record RcProbeResult(
    /// <summary>Claude's per-process state file exists and records a bridgeSessionId.</summary>
    bool Armed,
    /// <summary>Established TCP connections from the pid to Anthropic (160.79.0.0/16:443).</summary>
    int BridgeConnections,
    /// <summary>False when the state file was unreadable/absent (probe result untrustworthy).</summary>
    bool StateFileFound);

public interface IRcBridgeProbe
{
    RcProbeResult Probe(int pid);
}
