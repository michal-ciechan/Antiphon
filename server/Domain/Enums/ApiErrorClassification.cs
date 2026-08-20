namespace Antiphon.Server.Domain.Enums;

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
    /// at the stated reset; never a retry ladder. Until CARD-0022's parser ships, every Wall
    /// degrades to the Transient ladder entering at the 30-minute rung (§D3).
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
