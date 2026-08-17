using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Pins <see cref="ApiErrorClassifier"/> (S1 of the 2026-08-17 usage-limit spec, CARD-0072) against
/// the MEASURED stub population — 23 real API-error stubs on this machine: 18× rate_limit/429, 1×
/// server_error/529, 1× server_error connection-drop (no status), 2× authentication_failed, plus
/// the deliberately-provoked model_not_found/404 fixture. Pure: no session, no DB, no pty.
/// </summary>
public class ApiErrorClassifierTests
{
    // The 78% case: the quota wall. One resume at the stated reset (S4/S5); never a retry ladder.
    [Test]
    public void Rate_limit_is_a_wall()
    {
        ApiErrorClassifier
            .Classify("rate_limit", 429, "You've hit your session limit · resets 6:10pm (Europe/London)")
            .ShouldBe(ApiErrorClassification.Wall);
    }

    // The wall class keys on the structural error class, not on the reset text parsing — an
    // unparseable reset degrades the RESPONSE at S5 (30-minute ladder entry), never the class.
    [Test]
    public void A_wall_with_unparseable_reset_text_is_still_a_wall()
    {
        ApiErrorClassifier
            .Classify("rate_limit", 429, "You've hit your session limit · try again later")
            .ShouldBe(ApiErrorClassification.Wall);
    }

    [Test]
    public void Server_error_529_is_transient()
    {
        ApiErrorClassifier
            .Classify("server_error", 529, "API Error: 529 Overloaded. This is a server-side issue, usually temporary — try again in a moment.")
            .ShouldBe(ApiErrorClassification.Transient);
    }

    // The measured connection drop carries server_error and NO status: the class alone decides.
    [Test]
    public void A_connection_drop_without_a_status_is_transient()
    {
        ApiErrorClassifier
            .Classify("server_error", null, "API Error: Connection lost mid-response. The response above may be incomplete.")
            .ShouldBe(ApiErrorClassification.Transient);
    }

    // Retrying an expired login forever is a new failure mode, not a fix (§D3): auth stubs carry
    // no status at all on the real records, and must never reach a retry ladder.
    [Test]
    public void Authentication_failed_needs_a_human()
    {
        ApiErrorClassifier
            .Classify("authentication_failed", null, "Login expired · Please run /login")
            .ShouldBe(ApiErrorClassification.NeedsHuman);
    }

    [Test]
    public void Model_not_found_needs_a_human()
    {
        ApiErrorClassifier
            .Classify("model_not_found", 404, "There's an issue with the selected model (bogus-family).")
            .ShouldBe(ApiErrorClassification.NeedsHuman);
    }

    // A future Claude Code rewording of the CLASS (the structural field is stable plumbing, but
    // nothing is guaranteed) falls back to the status — 429 is still a wall, 5xx still transient.
    [Test]
    public void An_unknown_class_falls_back_to_the_status()
    {
        ApiErrorClassifier.Classify("quota_exceeded", 429, "some new wording").ShouldBe(ApiErrorClassification.Wall);
        ApiErrorClassifier.Classify(null, 429, null).ShouldBe(ApiErrorClassification.Wall);
        ApiErrorClassifier.Classify("upstream_glitch", 500, null).ShouldBe(ApiErrorClassification.Transient);
        ApiErrorClassifier.Classify(null, 529, null).ShouldBe(ApiErrorClassification.Transient);
    }

    // No class and no usable status: Unknown, which consumers treat as Transient with a
    // conservative attempt cap (§D1) — bounded retry of an unrecognized error, never silence.
    [Test]
    public void Nothing_recognizable_is_unknown()
    {
        ApiErrorClassifier.Classify(null, null, "API Error: something new").ShouldBe(ApiErrorClassification.Unknown);
        ApiErrorClassifier.Classify("mystery", null, null).ShouldBe(ApiErrorClassification.Unknown);
        ApiErrorClassifier.Classify("mystery", 404, null).ShouldBe(ApiErrorClassification.Unknown);
    }
}
