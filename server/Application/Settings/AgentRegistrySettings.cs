namespace Antiphon.Server.Application.Settings;

public class AgentRegistrySettings
{
    public string DefaultDefinition { get; set; } = "claude";
    public Dictionary<string, AgentDefinition> Definitions { get; set; } = new();
    public int ClaudeReadyQuietPeriodMs { get; set; } = 5000;
    public int ClaudeReadyMaxWaitMs { get; set; } = 60000;
    public int ClaudeDoneMaxWaitMs { get; set; } = 300000;
    public int CodexReadyQuietPeriodMs { get; set; } = 1000;
    public int CodexReadyMaxWaitMs { get; set; } = 60000;
    public int CodexDoneQuietPeriodMs { get; set; } = 3000;
    public int CodexDoneMaxWaitMs { get; set; } = 300000;
}

public class AgentDefinition
{
    public string Kind { get; set; } = "Raw";
    public string Exe { get; set; } = string.Empty;
    public List<string> ArgsTemplate { get; set; } = new();
    public Dictionary<string, string> Env { get; set; } = new();
}
