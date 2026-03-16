using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Response DTO for LLM providers. ApiKey is always masked (NFR7).
/// </summary>
public record LlmProviderDto(
    Guid Id,
    string Name,
    ProviderType ProviderType,
    string ApiKeyMasked,
    string BaseUrl,
    bool IsEnabled,
    string DefaultModel,
    DateTime CreatedAt,
    DateTime UpdatedAt);
