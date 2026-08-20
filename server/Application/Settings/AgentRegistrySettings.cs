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
    public int CodexDoneQuietPeriodMs { get; set; } = 3000;
    public int CodexDoneMaxWaitMs { get; set; } = 300000;
    public int OpenCodeReadyQuietPeriodMs { get; set; } = 1000;
    public int OpenCodeReadyMaxWaitMs { get; set; } = 60000;
    public int OpenCodeDoneQuietPeriodMs { get; set; } = 3000;
    public int OpenCodeDoneMaxWaitMs { get; set; } = 300000;
    public int GrokReadyQuietPeriodMs { get; set; } = 1000;
    public int GrokReadyMaxWaitMs { get; set; } = 60000;
    public int GrokReadyMinTotalWaitMs { get; set; } = 2000;
    public int GrokDoneQuietPeriodMs { get; set; } = 3000;
    public int GrokDoneMaxWaitMs { get; set; } = 300000;
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
