using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1156 S4 (option A): the current-state standing-boot attention projection and the cold
/// incident prune, over an isolated PostgreSQL database with a <c>FakeTimeProvider</c>, through
/// <c>AttentionServiceTests.BuildService</c> and the real <c>AgentSupervisorService.PruneIncidentsAsync</c>
/// built by <c>AgentSupervisionTests.BuildHarness</c> on the same database. Design V-8, V-9 and
/// V-10 in <c>docs/superpowers/plans/2026-10-08-card-1156-alwayson-boot-watchdog-test-design.md</c>.
/// The projection is read-only: every method asserts zero writes on its context and zero runner
/// calls beyond <c>GetAsync</c>'s inherited one list.
/// </summary>
[Category("Integration")]
public class StandingBootAttentionTests
{
    /// <summary>
    /// V-8. One derived LivenessProbeFailed row per current episode, independent of incident
    /// history: no-incident (no receipt at all), failed-save (the sweep's writer faulted),
    /// warning-at-eight (Warning, prompt age, boot due and operator due in the evidence),
    /// error-at-twenty (Error, the operator wording: inspect, wait, reply, or explicitly Stop and
    /// Start/resume; actions OpenAgent and OpenDrawer; never Retry/Cancel), older-than-24-hours (a
    /// 25-hour-old receipt still projects the current row), pruned-history (receipts deleted; the row
    /// remains), legacy-and-current (an old <c>bootSeq=</c> receipt on the same session yields one
    /// row with the new wording and no "restart ladder" text).
    /// </summary>
    [Test]
    [Arguments("no-incident")]
    [Arguments("failed-save")]
    [Arguments("warning-at-eight")]
    [Arguments("error-at-twenty")]
    [Arguments("older-than-24-hours")]
    [Arguments("pruned-history")]
    [Arguments("legacy-and-current")]
    public Task C1156_Current_boot_attention_survives_optional_history(string history) =>
        Card1156Pending.Skip("S4", nameof(C1156_Current_boot_attention_survives_optional_history));

    /// <summary>
    /// V-9. The five real model kinds (assistant, thinking, tool-call, tool-result, turn-end) past
    /// the prompt clear the derived row; terminal-session (Stopped with EndedAt) and
    /// replaced-launch (a new LaunchResumedAt past the old prompt, with its own unanswered prompt)
    /// clear the old episode, and the replacement can independently appear. Receipts remain in the
    /// table in every argument.
    /// </summary>
    [Test]
    [Arguments("assistant")]
    [Arguments("thinking")]
    [Arguments("tool-call")]
    [Arguments("tool-result")]
    [Arguments("turn-end")]
    [Arguments("terminal-session")]
    [Arguments("replaced-launch")]
    public Task C1156_Positive_resolution_clears_only_the_current_episode(string resolution) =>
        Card1156Pending.Skip("S4", nameof(C1156_Positive_resolution_clears_only_the_current_episode));

    /// <summary>
    /// V-10. The real cold prune with the shipped age cutoff and a lowered per-agent cap:
    /// age-cutoff (a 31-day-old current-episode receipt survives while an unrelated 31-day-old
    /// incident is deleted), agent-cap (cap 2 with six newer unrelated incidents: both current
    /// receipts survive, unrelated rows are capped), unknown-current-read (a read fault while
    /// computing the protected keys retains every <c>standingBoot:v1</c> receipt and prunes the
    /// rest), positive-resolution (after a model reply the same receipts are deleted by ordinary
    /// retention). After each prune a sweep tick cannot re-mint a retained receipt.
    /// </summary>
    [Test]
    [Arguments("age-cutoff")]
    [Arguments("agent-cap")]
    [Arguments("unknown-current-read")]
    [Arguments("positive-resolution")]
    public Task C1156_Prune_preserves_active_dedup_and_releases_resolved_history(string pressure) =>
        Card1156Pending.Skip("S4", nameof(C1156_Prune_preserves_active_dedup_and_releases_resolved_history));
}
