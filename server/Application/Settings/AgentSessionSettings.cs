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
    /// CARD-0312 S2: the kill switch for the synthetic boot probe, in the shape of
    /// <c>TranscriptConfirmEnabled</c>. <c>false</c> leaves the reply WATCH intact — the watch
    /// costs nothing and needs no probe wherever a launch already types a real prompt.
    /// </summary>
    public bool BootProbeEnabled { get; set; } = true;

    /// <summary>
    /// CARD-0312 S2: the body of the synthetic boot probe. Deliberately one line with NO WORK in
    /// it — a probe with content invites a long turn, which is the cost the 2026-07-23 removal of
    /// the periodic pong probe complained about. Sent at most once per launch, only on an
    /// unattended launch that typed nothing at all, as
    /// <c>QueuedMessageOrigin.System</c>/<c>WhenIdle</c>, so a human reading the transcript sees
    /// the same class of row as the restart note that has always been there. Blank disables the
    /// probe as surely as <see cref="BootProbeEnabled"/> does.
    /// </summary>
    public string BootProbeBody { get; set; } =
        "Antiphon liveness check — reply with the single word: ready. Do not do any other work.";

    /// <summary>
    /// How long the boot sequence waits for the TUI to print "remote-control is active" after
    /// a successful /remote-control submit. Inner bound inside <see cref="RemoteControlSetupTimeoutMs"/>:
    /// the marker wait cannot exceed this even when the outer setup budget still has time left.
    /// The rename must land while the bridge is genuinely connected or claude.ai never syncs the
    /// session title; on a resume the TUI can stay busy for many seconds, and typing into a busy
    /// composer jams commands into one submission.
    /// </summary>
    public int RemoteControlArmTimeoutMs { get; set; } = 20_000;

    /// <summary>
    /// Outer wall-clock budget for the entire remote-control monitoring bootstrap: the
    /// /remote-control submit, first-output wait, armed-marker wait, and optional /rename.
    /// Does NOT cover the card work prompt or the launch note — those stay on their own
    /// verified-delivery path. CARD-0240: a hung runner snapshot used to ignore
    /// <see cref="RemoteControlArmTimeoutMs"/> because the call itself was synchronous and
    /// uncancellable; this linked-CTS deadline is what actually releases the launch.
    /// Sized at 60s so a first-try RC submit (evidence timeout 15s) plus the 20s arm wait plus
    /// rename still fit; CARD-0056's inner retries still run but cannot outlive this budget.
    /// </summary>
    public int RemoteControlSetupTimeoutMs { get; set; } = 60_000;

    /// <summary>
    /// CARD-0292 S1: on a resume-mode relaunch, how long the preamble polls
    /// <c>IRcBridgeProbe</c> for an already-armed bridge before falling through to today's
    /// <c>/remote-control</c> send. Claude re-establishes the bridge itself on resume; the ground
    /// truth is <c>bridgeSessionId</c> in its own per-process state file, so an armed observation
    /// skips the send entirely (the menu wedge cannot happen) and goes straight to <c>/rename</c>.
    /// Expiring unarmed is not an error — a resume of a never-bridged session legitimately never
    /// arms, and the S2 menu guard converts the late-arm race into one Esc. Inner bound inside
    /// <see cref="RemoteControlSetupTimeoutMs"/>.
    /// </summary>
    public int RemoteControlResumeProbeTimeoutMs { get; set; } = 5_000;

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
