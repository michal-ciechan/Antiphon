namespace Antiphon.Server.Application.Settings;

public class LlmSettings
{
    public string DefaultProvider { get; set; } = "anthropic";
    public string DefaultModel { get; set; } = string.Empty;
    public List<LlmProviderSettings> Providers { get; set; } = [];
}

public class LlmProviderSettings
{
    public string Name { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public List<string> Models { get; set; } = [];
}
