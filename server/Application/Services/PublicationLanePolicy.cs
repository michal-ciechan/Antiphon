using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

public enum PublicationLaneDecision { Cooldown = 0, Queue = 1, Abandon = 2 }

/// <summary>
/// CARD-1446 D-4 (S4c): what the residue sweep's Publication lane does with one published operation whose
/// cleanup is not complete. S4c-test skeleton: always <see cref="PublicationLaneDecision.Queue"/>.
/// </summary>
public static class PublicationLanePolicy
{
    public static string CursorKey(Guid operationId) => "landing:" + operationId.ToString("N");

    public static PublicationLaneDecision Decide(AgentTaskLanding op, int queuedCount, WorktreeResidueCandidateCursor? cursor,
        DateTime now, WorktreeResidueSettings settings) => PublicationLaneDecision.Queue;
}
