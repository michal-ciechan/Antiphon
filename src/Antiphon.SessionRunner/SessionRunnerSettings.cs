namespace Antiphon.SessionRunner;

public sealed class SessionRunnerSettings
{
    public global::Antiphon.SessionRunner.Contracts.GrokRulesSettings GrokRules { get; set; } = new();
    public string SessionLogPath { get; set; } = Path.Combine("workspace", "session-runner-logs");

    /// <summary>
    /// CARD-1153 D-3: <c>SessionRunner:AbsenceEvidence:KeyPath</c> names the owner-readable key file
    /// for direct-HTTP absence evidence. Missing disables only HTTP absence certification.
    /// </summary>
    public RunnerAbsenceEvidenceSettings AbsenceEvidence { get; set; } = new();

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
    /// Instance backend request. Null reads ambient input once; explicit empty selects the platform
    /// default (Windows modern, Unix Porta). The daemon composes nonempty environment before config
    /// at startup. Runtime capabilities, custody and every host's <c>--pty-backend</c> use that same
    /// frozen request. Explicit inbox and missing-pair fallback remain deprecated in release A.
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

    /// <summary>
    /// CARD-1614. Linux custody stays unadvertised unless this is explicitly true.
    /// cgroup v2 is never admitted. The shipped value is false.
    /// </summary>
    public bool LinuxCustodyQualified { get; set; }

    /// <summary>
    /// CARD-1653 S2. Owned puts new non-custody Linux sockets in <see cref="ResolvedPtyHostSocketDir"/>,
    /// which must be owned by the runner, mode 0700, not a symlink and under safe ancestors; an unsafe
    /// directory fails the launch (no /tmp fallback). Legacy (the default) keeps
    /// <c>/tmp/antiphon-pty-&lt;id&gt;</c>. Ignored on Windows.
    /// </summary>
    public PtyHostSocketDirMode PtyHostSocketDirMode { get; set; } = PtyHostSocketDirMode.Legacy;

    /// <summary>CARD-1653 S2. Overrides <c>&lt;ResolvedPtyHostDir&gt;/sock</c>.</summary>
    public string? PtyHostSocketDir { get; set; }

    /// <summary>
    /// CARD-1653 S2. The kernel peer check after connect and before the hello. Off (the default) is
    /// the unchecked connect; Observe logs a mismatch and connects; Enforce refuses it.
    /// </summary>
    public PtyHostPeerCheckMode PtyHostPeerCheck { get; set; } = PtyHostPeerCheckMode.Off;

    public string ResolvedPtyHostDir => PtyHostDir ?? Path.Combine(SessionLogPath, "pty-hosts");
    public string PtyHostManifestDir => Path.Combine(ResolvedPtyHostDir, "manifests");
    public string PtyHostBinDir => Path.Combine(ResolvedPtyHostDir, "bin");
    public string PtyHostLogDir => Path.Combine(ResolvedPtyHostDir, "logs");
    public string ResolvedPtyHostSourceDir => PtyHostSourceDir ?? AppContext.BaseDirectory;
    public string ResolvedPtyHostSocketDir => Path.GetFullPath(PtyHostSocketDir ?? Path.Combine(ResolvedPtyHostDir, "sock"));
}

/// <summary>CARD-1653 S2: where new Linux pty-host sockets live.</summary>
public enum PtyHostSocketDirMode { Legacy, Owned }

/// <summary>CARD-1653 S2: the kernel peer check before the hello.</summary>
public enum PtyHostPeerCheckMode { Off, Observe, Enforce }

public sealed class RunnerAbsenceEvidenceSettings
{
    public string? KeyPath { get; set; }
}
