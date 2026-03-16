using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// An LLM provider configuration (Anthropic, OpenAI, Ollama) with connection details.
/// API keys are stored server-side only and never returned to the frontend (NFR7).
/// </summary>
public class LlmProvider
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ProviderType ProviderType { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string DefaultModel { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<ModelRouting> ModelRoutings { get; set; } = [];
}
