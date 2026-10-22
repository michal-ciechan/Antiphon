using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0167 S1 skeleton: the shape only. <see cref="Validate"/> returns its input so the roster in
/// <c>ProviderEndpointProjectionTests</c> goes red on its assertions (plan D-14).
/// </summary>
public sealed record ProviderEndpoint(string BaseUrl, string? ApiKeyName)
{
    public const int MaxBaseUrlLength = 2000;
    public const string DefaultField = "providerEndpoint";

    private static readonly AgentKind[] Supported = [AgentKind.ClaudeCode, AgentKind.Grok, AgentKind.Codex];

    public static IReadOnlyList<AgentKind> SupportedKinds => Supported;

    public static bool IsSupportedKind(AgentKind kind) => Array.IndexOf(Supported, kind) >= 0;

    public static bool RequiresKeyName(AgentKind kind) => kind == AgentKind.Codex;

    public ProviderEndpoint Validate(AgentKind? kind, string field = DefaultField) => this;
}
