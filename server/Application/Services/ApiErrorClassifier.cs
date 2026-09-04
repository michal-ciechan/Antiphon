using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Pure classifier for API-error stubs (CARD-0072 / CARD-0281): no clock, no DB, no session. Input
/// is the stub's carried evidence — <c>TranscriptEntry.ApiErrorClass</c>/<c>ApiErrorStatus</c>/<c>Text</c>
/// — and classification keys on the STRUCTURAL class first (Claude Code's own error plumbing,
/// stable across the whole measured record), falling back to the HTTP status when the class is
/// missing or unrecognized. The error TEXT is used for the 403 capacity vocabulary gate
/// (CARD-0281): a 403 that looks like a spending/credits wall is Wall, otherwise NeedsHuman.
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
    /// <param name="text">The stub's error text. Used for the 403 capacity vocabulary gate
    /// (CARD-0281); otherwise consumed by <c>UsageLimitWallParser</c> for reset/alias, not class.</param>
    public static ApiErrorClassification Classify(string? apiErrorClass, int? apiErrorStatus, string? text)
    {
        switch (apiErrorClass)
        {
            case "rate_limit":
                return ApiErrorClassification.Wall;
            case "server_error":
            case TranscriptKinds.ApiErrorClasses.Transport:
                return ApiErrorClassification.Transient;
            case "authentication_failed":
            case "model_not_found":
                return ApiErrorClassification.NeedsHuman;
        }

        // Class missing or never-seen: the status is weaker evidence but still structural.
        return apiErrorStatus switch
        {
            429 => ApiErrorClassification.Wall,
            402 => ApiErrorClassification.Wall,
            403 => UsageLimitWallParser.LooksLikeCapacity(text)
                ? ApiErrorClassification.Wall
                : ApiErrorClassification.NeedsHuman,
            400 => ApiErrorClassification.NeedsHuman,
            401 => ApiErrorClassification.NeedsHuman,
            >= 500 => ApiErrorClassification.Transient,
            _ => ApiErrorClassification.Unknown,
        };
    }
}
