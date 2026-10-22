namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// How one agent relates to its project's provider endpoint (CARD-0167). The generic launch-env box
/// cannot express "unset", which is why <see cref="Direct"/> exists.
/// </summary>
public enum ProviderEndpointMode
{
    /// <summary>Use the project's endpoint for the agent's kind, if one is set.</summary>
    Inherit = 0,

    /// <summary>Use the agent's own endpoint; it outranks the project's.</summary>
    Endpoint = 1,

    /// <summary>Exempt this agent from the project endpoint; no endpoint is projected.</summary>
    Direct = 2,
}
