namespace Antiphon.Server.Application.Settings;

public class AgentRegistrySettings
{
    internal const int MaximumDefinitionNameLength = 200;

    public string DefaultDefinition { get; set; } = "claude";
    public Dictionary<string, AgentDefinition> Definitions { get; set; } = new();
    public int ClaudeReadyQuietPeriodMs { get; set; } = 5000;
    public int ClaudeReadyMaxWaitMs { get; set; } = 60000;

    /// <summary>
    /// Floor on how soon after process start a Claude session may be called ready. The TUI renders
    /// its prompt in ~1s but the backend connection takes 3-7s, and input sent before it is up is
    /// accepted by the composer and silently dropped. Was a hardcoded 9s; it is a setting only so
    /// tests do not have to spend it.
    /// </summary>
    public int ClaudeReadyMinTotalWaitMs { get; set; } = 9000;

    /// <summary>
    /// How long a launch waits for an answered trust dialog to actually leave the screen before
    /// giving up and reporting the session blocked.
    /// </summary>
    public int ClaudeTrustPromptSettleMs { get; set; } = 15000;

    /// <summary>
    /// CARD-0103. Budget for the input-responsiveness probe — the final ready gate, which writes a
    /// short token, requires it to RENDER, and clears it again. Quiet is not reading: measured
    /// 2026-08-20, the same body took 48.8s to render when written ~2s after "ready" and 0.74s when
    /// written 45s after it, on the same runner, three times. 90s because the measured dead zone ran
    /// 48-200s and the 15s evidence window that failed sat entirely inside it; a healthy launch pays
    /// ~1s of it. Zero or negative disables the probe (the kill switch — readiness then means what
    /// it meant before, which is why it is off only deliberately).
    /// </summary>
    public int ClaudeInputProbeTimeoutMs { get; set; } = 90000;

    /// <summary>Screen polling cadence for the input probe.</summary>
    public int ClaudeInputProbePollIntervalMs { get; set; } = 250;

    /// <summary>
    /// How long a written probe token may go unrendered before it is written again (at most
    /// <see cref="ClaudeInputProbeMaxWrites"/> writes in total). A belt only: the measured shape is a
    /// RETAINED ConPTY buffer that drains in order on wake, so the first token is expected to arrive
    /// late rather than to be lost.
    /// </summary>
    public int ClaudeInputProbeRetypeIntervalMs { get; set; } = 30000;

    /// <summary>Total probe-token writes inside the budget, including the first.</summary>
    public int ClaudeInputProbeMaxWrites { get; set; } = 3;

    /// <summary>
    /// How long the composer has to lose the probe token after Ctrl+U. A composer that will not
    /// empty fails the launch: appending a boot prompt to a line we could not clear is how a body
    /// arrives spliced onto junk.
    /// </summary>
    public int ClaudeInputProbeClearTimeoutMs { get; set; } = 10000;

    public int ClaudeDoneMaxWaitMs { get; set; } = 300000;
    public int CodexReadyQuietPeriodMs { get; set; } = 1000;
    public int CodexReadyMaxWaitMs { get; set; } = 60000;

    /// <summary>
    /// CARD-0299 S3. After quiet+trust, how long ready waits for
    /// <c>Starting MCP servers</c> / <c>Booting MCP server</c> to leave the screen
    /// (plus 500 ms of consecutive absence). Zero disables the wait. The bound is a
    /// cap, not a sleep; expiry logs a Warning and proceeds — the boot line is not a modal.
    /// </summary>
    public int CodexBootStatusMaxWaitMs { get; set; } = 10_000;
    public int CodexDoneQuietPeriodMs { get; set; } = 3000;
    public int CodexDoneMaxWaitMs { get; set; } = 300000;

    /// <summary>
    /// CARD-0108 S1. How long a typed Codex prompt has to produce its confirming <c>UserPrompt</c>
    /// transcript row before Enter is pressed again. 4 s is the measured working interval: the
    /// probe's extra Enter went ~4 s after the failed CR and submitted 6/6, and after the Enter
    /// that actually submits the row is observable within ~2.8 s. Enter only — the body is never
    /// re-typed (see <c>CodexSubmitConfirmation</c>).
    /// </summary>
    public int CodexSubmitReEnterIntervalMs { get; set; } = 4000;

    /// <summary>
    /// Enter presses AFTER the initial body+CR. Three, because a re-press onto an empty composer
    /// was measured to submit nothing, so the only cost of an unnecessary one is the interval.
    /// </summary>
    public int CodexSubmitAttempts { get; set; } = 3;

    /// <summary>
    /// Total budget for the submit-confirm loop. <b>Do not trim below ~20 s:</b> a first turn's
    /// confirmation races the rollout file's lazy creation AND the tailer's discovery/bind
    /// (250 ms locate poll plus CARD-0006's C1-C4), and "no transcript yet" must poll as
    /// not-yet-confirmed rather than as failure.
    /// </summary>
    public int CodexSubmitConfirmTimeoutMs { get; set; } = 20000;
    public int OpenCodeReadyQuietPeriodMs { get; set; } = 1000;
    public int OpenCodeReadyMaxWaitMs { get; set; } = 60000;
    public int OpenCodeDoneQuietPeriodMs { get; set; } = 3000;
    public int OpenCodeDoneMaxWaitMs { get; set; } = 300000;
    public int GrokReadyQuietPeriodMs { get; set; } = 1000;
    public int GrokReadyMaxWaitMs { get; set; } = 60000;
    public int GrokReadyMinTotalWaitMs { get; set; } = 2000;
    /// <summary>
    /// How long <c>RunnerGrokAdapter.WaitForReadyAsync</c> waits for Grok's directory-trust
    /// dialog to leave the rendered screen after sending <c>y</c> (CARD-0315). Zero skips the
    /// verify and treats the send as success.
    /// </summary>
    public int GrokTrustPromptSettleMs { get; set; } = 15000;
    public int GrokDoneQuietPeriodMs { get; set; } = 3000;
    public int GrokDoneMaxWaitMs { get; set; } = 300000;

    /// <summary>
    /// CARD-0324. Pre-launch <c>auth.json</c> probe on registry-path Grok dispatches.
    /// Default true. Disables only this layer — the screen detector still runs.
    /// </summary>
    public bool GrokCredentialProbeEnabled { get; set; } = true;
}

public class AgentDefinition
{
    public string Kind { get; set; } = "Raw";
    public string Exe { get; set; } = string.Empty;
    public List<string> ArgsTemplate { get; set; } = new();
    public Dictionary<string, string> Env { get; set; } = new();
    public List<string> SecretEnvironmentNames { get; set; } = new();
    public List<string> NonSecretEnvironmentNames { get; set; } = new();
}
