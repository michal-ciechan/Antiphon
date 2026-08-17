namespace Antiphon.Server.Application.Services;

/// <summary>
/// What kind of API-error stub killed a turn, and therefore what response is owed (spec
/// 2026-08-17-usage-limit-and-api-error-resilience §D3). The classes drive DIFFERENT machinery:
/// blind-retrying a quota wall is guaranteed to fail, and retrying an expired login forever is a
/// new failure mode, not a fix.
/// </summary>
public enum ApiErrorClassification
{
    /// <summary>
    /// Unrecognized class/status combination. Consumers treat it as <see cref="Transient"/> with a
    /// conservative attempt cap (S5) — retrying an unknown error a bounded number of times is the
    /// least-wrong default.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A quota wall (<c>rate_limit</c>, 429 — 18 of the 23 measured stubs): the session/account
    /// limit, stating its own reset time in the text ("resets 6:10pm (Europe/London)"). One resume
    /// at the stated reset; never a retry ladder.
    /// </summary>
    Wall = 1,

    /// <summary>
    /// A server-side failure (<c>server_error</c>: 529 Overloaded, connection-drop): retryable on
    /// the per-session ladder. Claude Code's own retry is already exhausted by the time the stub is
    /// written (measured 209s on the 529), so Antiphon-side retry is the only recovery left.
    /// </summary>
    Transient = 2,

    /// <summary>
    /// Nothing automatic can fix it (<c>authentication_failed</c>, <c>model_not_found</c>): never
    /// retried, incident immediately — auth-expired is fleet-wide Critical (one shared account).
    /// </summary>
    NeedsHuman = 3,
}

/// <summary>
/// Pure classifier for API-error stubs (CARD-0072): no clock, no DB, no session. Input is the
/// stub's carried evidence — <c>TranscriptEntry.ApiErrorClass</c>/<c>ApiErrorStatus</c>/<c>Text</c>
/// — and classification keys on the STRUCTURAL class first (Claude Code's own error plumbing,
/// stable across the whole measured record), falling back to the HTTP status only when the class is
/// missing or unrecognized. The error TEXT is deliberately not a classification input: the reset
/// time inside a Wall stub's text is consumed by <c>UsageLimitResetParser</c> (S4), and a parse
/// failure there degrades the RESPONSE (§D3), not the class.
/// </summary>
public static class ApiErrorClassifier
{
    /// <summary>
    /// Classifies a stub from its carried fields. Every value below is measured, not guessed
    /// (CARD-0072 sweep, 23 real stubs): rate_limit/429, server_error/529, server_error/no-status
    /// (connection drop), authentication_failed/no-status, model_not_found/404.
    /// </summary>
    /// <param name="apiErrorClass">The stub's raw top-level <c>error</c> value.</param>
    /// <param name="apiErrorStatus">The stub's <c>apiErrorStatus</c>, when present.</param>
    /// <param name="text">The stub's error text. Unused today — accepted so the whole of the
    /// stub's evidence flows through one seam and a future text-informed distinction (§6.5's
    /// longer-period cap) lands here rather than at every call site.</param>
    public static ApiErrorClassification Classify(string? apiErrorClass, int? apiErrorStatus, string? text)
    {
        switch (apiErrorClass)
        {
            case "rate_limit":
                return ApiErrorClassification.Wall;
            case "server_error":
                return ApiErrorClassification.Transient;
            case "authentication_failed":
            case "model_not_found":
                return ApiErrorClassification.NeedsHuman;
        }

        // Class missing or never-seen: the status is weaker evidence but still structural.
        return apiErrorStatus switch
        {
            429 => ApiErrorClassification.Wall,
            >= 500 => ApiErrorClassification.Transient,
            _ => ApiErrorClassification.Unknown,
        };
    }
}
