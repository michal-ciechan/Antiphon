namespace Antiphon.SessionRunner;

public sealed class SessionRunnerSettings
{
    public string SessionLogPath { get; set; } = Path.Combine("workspace", "session-runner-logs");
    public int ReplayBufferMaxChars { get; set; } = 256 * 1024;

    /// <summary>How often the liveness sweep verifies that "Running" sessions still have a live OS process.</summary>
    public int LivenessSweepIntervalMs { get; set; } = 5_000;

    /// <summary>
    /// Root for pty-host state (manifests, shadow-copied binaries, host logs).
    /// Defaults to a "pty-hosts" dir next to the session logs.
    /// </summary>
    public string? PtyHostDir { get; set; }

    /// <summary>
    /// Directory containing the Antiphon.PtyHost build output to shadow-copy from.
    /// Defaults to the runner's own base directory (the host ships with the runner).
    /// </summary>
    public string? PtyHostSourceDir { get; set; }

    /// <summary>Seconds to wait for a freshly spawned host's pipe to accept the connection.</summary>
    public int PtyHostConnectTimeoutSec { get; set; } = 15;

    /// <summary>Host self-destructs if the runner never sends Launch within this window.</summary>
    public int PtyHostLaunchTimeoutSec { get; set; } = 30;

    /// <summary>
    /// How long an orphaned host lingers after child exit waiting for a runner ack before
    /// giving up and exiting (bounds orphan lifetime if the runner never comes back).
    /// </summary>
    public double PtyHostLingerHours { get; set; } = 24;

    public string ResolvedPtyHostDir => PtyHostDir ?? Path.Combine(SessionLogPath, "pty-hosts");
    public string PtyHostManifestDir => Path.Combine(ResolvedPtyHostDir, "manifests");
    public string PtyHostBinDir => Path.Combine(ResolvedPtyHostDir, "bin");
    public string PtyHostLogDir => Path.Combine(ResolvedPtyHostDir, "logs");
    public string ResolvedPtyHostSourceDir => PtyHostSourceDir ?? AppContext.BaseDirectory;
}
