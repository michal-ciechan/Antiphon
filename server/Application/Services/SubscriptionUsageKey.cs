using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The CARD-0143 identity, stated once: a TUI profile is the subscription; with no
/// profile the key degrades to the kind name (one account per kind).
/// </summary>
public static class SubscriptionUsageKey
{
    public static string For(Agent? owner, AgentKind kind) =>
        owner?.TuiProfileId is Guid id ? id.ToString("D") : kind.ToString();
}
