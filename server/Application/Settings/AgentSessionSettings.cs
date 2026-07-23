namespace Antiphon.Server.Application.Settings;

public sealed class AgentSessionSettings
{
    public int SignalRMaxChunkChars { get; set; } = 16 * 1024;
    public int ReplayBufferMaxChars { get; set; } = 512 * 1024;
    public string SessionLogPath { get; set; } = "logs/sessions";
    public int FirstDeltaTimeoutMs { get; set; } = 5_000;
    public int KillGraceMs { get; set; } = 5_000;
    public int StallTimeoutMs { get; set; } = 300_000;
    public int StallScanIntervalMs { get; set; } = 10_000;
    public int ManualTurnQuietPeriodMs { get; set; } = 3_000;
    public int MemoryLimitMb { get; set; } = 0;

    /// <summary>
    /// How long the boot sequence waits for the TUI to print "remote-control is active" after
    /// /remote-control before sending /rename. The rename must land while the bridge is genuinely
    /// connected or claude.ai never syncs the session title; on a resume the TUI can stay busy for
    /// many seconds, and typing into a busy composer jams commands into one submission.
    /// </summary>
    public int RemoteControlArmTimeoutMs { get; set; } = 20_000;
}
