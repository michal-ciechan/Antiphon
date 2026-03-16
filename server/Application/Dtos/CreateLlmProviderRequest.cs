using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public record CreateLlmProviderRequest(
    string Name,
    ProviderType ProviderType,
    string ApiKey,
    string BaseUrl,
    bool IsEnabled,
    string DefaultModel);
