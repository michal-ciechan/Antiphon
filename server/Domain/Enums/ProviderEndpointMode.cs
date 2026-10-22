namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// How an agent relates to its project's per-kind provider endpoint (CARD-0167 S2).
/// <see cref="Inherit"/> (0, the backfill for every pre-existing row) uses the project's endpoint
/// for the agent's kind when one is set; <see cref="Endpoint"/> uses the agent's own stored
/// endpoint instead; <see cref="Direct"/> exempts the agent from the project endpoint and stores
/// none of its own, which the generic launch-env box cannot express ("unset this").
/// </summary>
public enum ProviderEndpointMode
{
    Inherit = 0,
    Endpoint = 1,
    Direct = 2,
}
