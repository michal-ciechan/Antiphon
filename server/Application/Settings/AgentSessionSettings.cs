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

    /// <summary>
    /// When a session is relaunched with --resume and its persisted transcript shows the previous
    /// turn was cut off mid-flight (process died before its TurnEnd), automatically queue
    /// <see cref="ResumeContinuePrompt"/> so the interrupted work picks itself back up instead of
    /// sitting silently at the prompt (live miss 2026-08-08). The restart boundary record is
    /// written regardless — only the continue prompt is gated.
    /// </summary>
    public bool ResumeAutoContinue { get; set; } = true;

    /// <summary>Queued (WhenIdle) after a resume that interrupted a mid-flight turn.</summary>
    public string ResumeContinuePrompt { get; set; } =
        "Your previous turn was interrupted by a restart. Review where you got to and continue the "
        + "work you were doing; if it was already complete, briefly confirm that instead.";
}
