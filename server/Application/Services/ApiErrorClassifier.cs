using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

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
            401 => ApiErrorClassification.NeedsHuman,
            >= 500 => ApiErrorClassification.Transient,
            _ => ApiErrorClassification.Unknown,
        };
    }
}
