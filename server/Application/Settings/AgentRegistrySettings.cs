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

    public int ClaudeDoneMaxWaitMs { get; set; } = 300000;
    public int CodexReadyQuietPeriodMs { get; set; } = 1000;
    public int CodexReadyMaxWaitMs { get; set; } = 60000;
    public int CodexDoneQuietPeriodMs { get; set; } = 3000;
    public int CodexDoneMaxWaitMs { get; set; } = 300000;
    public int OpenCodeReadyQuietPeriodMs { get; set; } = 1000;
    public int OpenCodeReadyMaxWaitMs { get; set; } = 60000;
    public int OpenCodeDoneQuietPeriodMs { get; set; } = 3000;
    public int OpenCodeDoneMaxWaitMs { get; set; } = 300000;
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
