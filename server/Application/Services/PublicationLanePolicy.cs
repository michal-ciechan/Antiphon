using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public enum PublicationLaneDecision { Cooldown = 0, Queue = 1, Abandon = 2 }

/// <summary>
/// CARD-1446 D-4 (S4c): what the residue sweep's Publication lane does with one published operation whose
/// cleanup is not complete. <see cref="PublicationLaneDecision.Abandon"/> when the cleanup is <c>Refused</c>
/// and <paramref name="queuedCount"/> earlier retries reached <c>WorktreeResidue:MaxCleanupRetries</c> (0
/// disables); otherwise <see cref="PublicationLaneDecision.Cooldown"/> while the operation's cursor
/// <c>NotBefore</c> is in the future and <c>CleanupRetryCooldownDays</c> is positive (0 disables pacing);
/// otherwise <see cref="PublicationLaneDecision.Queue"/>. Abandonment is checked first: a third refusal
/// stamps a cooldown, and waiting it out would only queue a fourth retry that the cap forbids.
/// </summary>
public static class PublicationLanePolicy
{
    public static string CursorKey(Guid operationId) => "landing:" + operationId.ToString("N");

    public static PublicationLaneDecision Decide(AgentTaskLanding op, int queuedCount, WorktreeResidueCandidateCursor? cursor,
        DateTime now, WorktreeResidueSettings settings)
    {
        if (settings.MaxCleanupRetries > 0 && op.Cleanup == LandCleanupStatus.Refused && queuedCount >= settings.MaxCleanupRetries)
            return PublicationLaneDecision.Abandon;
        if (settings.CleanupRetryCooldownDays > 0 && cursor?.NotBefore is DateTime notBefore && notBefore > now)
            return PublicationLaneDecision.Cooldown;
        return PublicationLaneDecision.Queue;
    }
}
