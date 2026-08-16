namespace Antiphon.SessionRunner;

public sealed class SessionRunnerSettings
{
    public string SessionLogPath { get; set; } = Path.Combine("workspace", "session-runner-logs");
    public int ReplayBufferMaxChars { get; set; } = 256 * 1024;

    /// <summary>How often the liveness sweep verifies that "Running" sessions still have a live OS process.</summary>
    public int LivenessSweepIntervalMs { get; set; } = 5_000;

    /// <summary>Master switch for the CPU spin watchdog (see SessionCpuWatchdogService).</summary>
    public bool CpuWatchdogEnabled { get; set; } = true;

    /// <summary>How often the CPU spin watchdog samples each Running session's process.</summary>
    public int CpuWatchdogIntervalMs { get; set; } = 5_000;

    /// <summary>
    /// CPU usage (percent of ONE core) at or above which an interval counts as hot. A genuinely
    /// idle claude.exe sits at ~0-2%; the observed spin pegs 100%+ — 50 splits them with a wide
    /// margin on both sides.
    /// </summary>
    public double CpuWatchdogHotCpuPercent { get; set; } = 50;

    /// <summary>
    /// How long a transcript-idle session must stay continuously hot before it is killed. Any
    /// cool interval (or a "working" transcript sample) resets the window.
    /// </summary>
    public int CpuWatchdogSustainedSeconds { get; set; } = 90;

    /// <summary>
    /// Grace after child start before the watchdog judges at all — startup and a --resume history
    /// load can be legitimately hot while old transcript records already read as idle.
    /// </summary>
    public int CpuWatchdogMinUptimeSeconds { get; set; } = 180;

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

    /// <summary>
    /// Which pseudoconsole every session on this runner spawns under: <c>inbox</c> (default, the
    /// kernel32/conhost path that strips bracketed-paste markers) or <c>modern</c> (the shipped
    /// conpty.dll + OpenConsole.exe, which delivers them). Exported to <c>ANTIPHON_PTY_BACKEND</c>
    /// at startup so the detached pty-hosts inherit it; an env var already set wins. A machine
    /// without the redistributable falls back to <c>inbox</c> — see <c>PtyBackendPolicy</c>.
    ///
    /// <para>CARD-0045: <see cref="SessionRunnerRuntime"/> also passes this value to each host as
    /// <c>--pty-backend</c>. In the daemon that is the same answer stated twice (the export already
    /// reached the host by inheritance); it exists because a runtime built in-process — a test's
    /// <c>DirectSessionRunnerClient</c> — has no daemon to do the export, and previously had no way
    /// at all to reach the backend of a pty three processes down.</para>
    /// </summary>
    public string? PtyBackend { get; set; }

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
