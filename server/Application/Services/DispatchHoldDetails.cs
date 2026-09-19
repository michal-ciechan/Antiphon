namespace Antiphon.Server.Application.Services;

/// <summary>
/// Stable dispatcher-hold detail strings (CARD-0535). Ages and counters must not appear in
/// Held details — <c>TraceHeldAsync</c> dedupes on exact text.
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
