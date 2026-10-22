using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0167 S1 skeleton: <see cref="For"/> and <see cref="Describe"/> project nothing yet, so the
/// roster in <c>ProviderEndpointProjectionTests</c> goes red on its assertions (plan D-14).
/// </summary>
public static class ProviderEndpointProjection
{
    public const string CodexProviderId = "antiphon";

    public sealed record Projected(IReadOnlyDictionary<string, string> Env, IReadOnlyList<string> Args)
    {
        public static readonly Projected Empty = new(
            new Dictionary<string, string>(StringComparer.Ordinal), Array.Empty<string>());
    }

    public static Projected For(AgentKind kind, ProviderEndpoint endpoint) => Projected.Empty;

    public static Projected Describe(AgentKind kind) => Projected.Empty;
}
