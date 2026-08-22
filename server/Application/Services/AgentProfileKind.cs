using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The CARD-0138 invariant, stated once: if a TUI profile is attached,
/// <see cref="Agent.Kind"/> equals that profile's <see cref="AgentTuiProfile.Kind"/>.
/// Callers that persist <see cref="Agent.TuiProfileId"/> go through here rather than
/// repeating the assignment. A null profile is not this helper's job — D1's second half
/// (leave <see cref="Agent.Kind"/> alone) is the early-return at each write path.
/// </summary>
public static class AgentProfileKind
{
    public static void Sync(Agent agent, AgentTuiProfile profile) =>
        agent.Kind = profile.Kind;
}
