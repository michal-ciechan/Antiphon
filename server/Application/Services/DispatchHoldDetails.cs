using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Stable dispatcher-hold detail strings (CARD-0535 / CARD-0537). Ages and counters must not appear
/// in Held details — <c>TraceHeldAsync</c> dedupes on exact text.
/// </summary>
public static class DispatchHoldDetails
{
    public const string WarningPrefix = "Warning:";
    public const string ErrorPrefix = "Error:";
    public const string RoutingPinPrefix = "routing pin not before";

    public const string LeaseOccupiedUnknown =
        "Held: repository mutation lease is occupied by another process (no running land on this repository; a worktree creation, gated commit, verification cleanup or another server instance holds landing.lock).";

    public static string ConcurrencyCap(int max) =>
        $"Held: concurrency cap reached ({max} process-spawning tasks running); waiting for a slot.";

    public static string LeaseHeldByLand(
        string holderShort, string holderTitle, string requestShort, DateTime startedAt)
    {
        var title = holderTitle.Length <= 60 ? holderTitle : holderTitle[..60];
        return $"Held: repository mutation lease is held by the land of task {holderShort} ({title}); land request {requestShort}, admitted {startedAt:O}.";
    }

    public static string LeaseFenced(string reason) =>
        $"Held: repository mutation lease is fenced: {reason}.";

    public static string PinnedAgentParkedOn(
        string agentName, string parkedShort, AgentTaskStatus parkedStatus)
    {
        var text =
            $"Held: pinned agent '{agentName}' is not idle; it is parked on task {parkedShort} ({parkedStatus}).";
        if (parkedStatus == AgentTaskStatus.Blocked)
        {
            text +=
                $" That task is waiting for an answer: reply to it (delegate.ps1 -Reply {parkedShort} \"...\") or cancel it (POST /api/agent-tasks/{parkedShort}/cancel); this follow-up dispatches when the agent is released to the pool.";
        }

        return text;
    }

    public static string PinnedAgentNoOpenTask(string agentName, AgentStatus agentStatus, Guid? agentId = null)
    {
        var stopRoute = agentId is Guid id
            ? $"POST /api/agents/{id:D}/stop"
            : "POST /api/agents/{id}/stop";
        return $"Held: pinned agent '{agentName}' is {agentStatus} with no open task and has not been released to the pool; stop it ({stopRoute}) to relaunch, or cancel this task.";
    }

    public static string StandingAgentBusy(
        string agentName, string busyShort, AgentTaskStatus busyStatus) =>
        $"Held: standing agent '{agentName}' is busy with task {busyShort} ({busyStatus}) on its live session.";

    /// <summary>CARD-0633 D-5 (b): prefix of <see cref="RemoteMirrorRequested"/>.</summary>
    public const string RemoteMirrorRequestedPrefix = "Held: remote workspace preparation is in flight";

    /// <summary>
    /// CARD-0633 D-5 (b): the branch push and runner mirror are running off the tick. The instant
    /// is the operation's start, so repeated ticks of one operation dedupe on the same text.
    /// </summary>
    public static string RemoteMirrorRequested(string runnerId, DateTime since) =>
        $"{RemoteMirrorRequestedPrefix} for runner '{runnerId}' (branch push and mirror requested {since:O}); the task dispatches once the mirror is recorded.";

    /// <summary>CARD-0633 D-5 (a) / D-6: a failed preparation's backoff is holding the task.</summary>
    public static string RemotePrepBackoff(string runnerId, int failures, DateTime notBefore) =>
        $"Held: remote workspace preparation for runner '{runnerId}' failed {failures} time(s) in a row; next attempt not before {notBefore:O}.";

    /// <summary>CARD-0633 D-5 (c): the runner is not connected or not dispatch-eligible.</summary>
    public static string RunnerUnavailable(string runnerId, string reason) =>
        $"Held: RunnerUnavailable: runner '{runnerId}' is not dispatch-eligible ({reason}); the task stays Queued.";

    public static string StandingAgentNoSession(string agentName) =>
        $"Held: standing agent '{agentName}' (always-on) has no live session; waiting for supervision to restart it.";

    public static string Escalation(
        string prefix,
        int seconds,
        DateTime heldSince,
        DateTime createdAt,
        string lastHeldDetail,
        int running,
        int max,
        IReadOnlyList<string> occupantShorts)
    {
        var occupants = string.Join(",", occupantShorts.Take(8));
        return $"{prefix} dispatch held {seconds}s since {heldSince:O}; created {createdAt:O}; reason={lastHeldDetail}; running={running} of {max}; occupants={occupants}.";
    }
}
