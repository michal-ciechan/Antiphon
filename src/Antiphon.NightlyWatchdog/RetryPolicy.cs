namespace Antiphon.NightlyWatchdog;

public enum RetryAction { None, SendExisting, SendNew }

public sealed record RetryDecision(RetryAction Action, int Attempt, string? Note)
{
    public static readonly RetryDecision Wait = new(RetryAction.None, 0, null);
}

/// <summary>
/// CARD-0545 D-7, applied after the tick's reader-first readback. One logical notification keeps its
/// <c>nid</c> across attempts; a held reader never triggers a resend until its hold expires.
/// </summary>
public static class RetryPolicy
{
    public static TimeSpan BackoffAfter(int attempt) => attempt switch
    {
        1 => TimeSpan.FromMinutes(2),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(10),
        4 => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromMinutes(60),
    };

    public static RetryDecision Decide(IReadOnlyList<AttemptRow> attempts, DateTime nowUtc, bool readerHeld, DateTime? heldSinceUtc,
        WatchdogOptions options)
    {
        if (attempts.Count == 0) return RetryDecision.Wait;
        var now = LondonClock.AsUtc(nowUtc);
        var last = attempts[^1];

        // An intent whose send never completed (fresh, or cut by a crash) is sent as the same attempt.
        if (!last.Sent)
            return new RetryDecision(RetryAction.SendExisting, last.Attempt, null);

        // Rule 4: a held reader blocks resends until the hold expires.
        var held = readerHeld && (heldSinceUtc is null || now - heldSinceUtc.Value < TimeSpan.FromMinutes(options.ReaderHoldExpiryMinutes));
        if (held) return RetryDecision.Wait;

        if (last.Accepted)
        {
            // Rule 3: accepted by Telegram but absent from the recipient's view after the grace period.
            var acceptedAt = last.AcceptedAt ?? last.StartedAt;
            return now - acceptedAt >= TimeSpan.FromMinutes(options.ReceiptGraceMinutes)
                ? new RetryDecision(RetryAction.SendNew, last.Attempt + 1, "accepted-but-unreceived")
                : RetryDecision.Wait;
        }

        // Rule 2: no acceptance; resend with the same nid after backoff and any retry-after.
        var next = last.StartedAt + BackoffAfter(last.Attempt);
        if (last.RetryAfterUtc is { } retryAfter && retryAfter > next) next = retryAfter;
        return now >= next ? new RetryDecision(RetryAction.SendNew, last.Attempt + 1, null) : RetryDecision.Wait;
    }
}
