namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// Result of probing a Claude process for remote-control bridge health.
/// Calibration (2026-07-20, 57 consecutive 30s samples on two idle sessions): an IDLE session
/// with a live bridge holds 2-3 established Anthropic connections continuously and NEVER dipped
/// to zero, while `.claude/sessions/&lt;pid&gt;.json` `updatedAt` never changes during idleness (it
/// is a status-transition stamp, not a heartbeat).
///
/// The two fields answer different questions and neither substitutes for the other:
/// <list type="bullet">
/// <item><see cref="Armed"/> — was the bridge EVER armed. A fact, written by the bridge itself.
/// Valid whether the session is busy or idle.</item>
/// <item><see cref="BridgeConnections"/> — is an armed bridge still alive. An inference, and only
/// on a quiet session: a busy session holds Anthropic connections for its own API calls, so a
/// non-zero count says nothing about the bridge (2026-08-08: a working agent showed 2 connections
/// while never armed at all).</item>
/// </list>
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
