using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public record UpdateLlmProviderRequest(
    string Name,
    ProviderType ProviderType,
    string? ApiKey,
    string BaseUrl,
    bool IsEnabled,
    string DefaultModel);
