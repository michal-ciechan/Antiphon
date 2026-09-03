using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0348: elapsed on a check header and a completion note counts from the latest reply,
/// not from dispatch. This is not <c>AgentSession.LaunchResumedAt</c> — that is CARD-0340's
/// session-level interrupted-launch clock, stamped by the reconciler's pass 1c, read only by
/// FailNeverStartedAsync, and never touched by a reply.
/// </summary>
internal static class AgentTaskResumeClock
{
    public static DateTime? ActiveSince(AgentTask task) =>
        task.RepliedAt > task.DispatchedAt ? task.RepliedAt : task.DispatchedAt;

    public static bool WasResumed(AgentTask task) =>
        task.RepliedAt > task.DispatchedAt;
}
