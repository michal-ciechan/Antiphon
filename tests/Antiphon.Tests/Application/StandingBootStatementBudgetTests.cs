using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1156 S5: <see cref="FullCommandCounter"/> on every context the sweep, the writer, the
/// attention projection and the prune open (including the runtime's persistence context), plus
/// runner transcript-pull counters, over an isolated PostgreSQL database through
/// <c>StandingBootWatchFixture</c>. Design V-11 in
/// <c>docs/superpowers/plans/2026-10-08-card-1156-alwayson-boot-watchdog-test-design.md</c>.
/// The expected totals are the design's enumerated rosters (measured cheap paths 0/1/2/2 at the
/// baseline; enumerated cold paths); Code pins the measured exact totals and prints every roster.
/// A measured total that differs from the design pin is a design revision to explain, never a
/// tolerance to widen.
/// </summary>
[Category("Integration")]
public class StandingBootStatementBudgetTests
{
    /// <summary>
    /// V-11a. One isolated one-session sweep per argument. Pulls are runner transcript requests.
    /// </summary>
    [Test]
    [Arguments("boot-deadline-disabled", 0, 0)]
    [Arguments("no-live-sessions", 1, 0)]
    [Arguments("healthy-unarmed-answered", 2, 0)]
    [Arguments("armed-before-deadline", 2, 0)]
    [Arguments("first-detected-stage", 15, 1)]
    [Arguments("same-recorded-episode", 3, 0)]
    [Arguments("first-operator-stage", 15, 1)]
    [Arguments("runtime-absent-detection", 14, 0)]
    public Task C1156_Boot_watch_statement_budgets(string path, int expected, int expectedPulls) =>
        Card1156Pending.Skip("S5", nameof(C1156_Boot_watch_statement_budgets));

    /// <summary>
    /// V-11b. The standing projection's own commands inside <c>GetAsync</c> (identified by SQL
    /// shape and asserted as a contiguous roster slice) and the prune's additional commands over
    /// its inherited delete/cap work, for zero, one and two eligible candidate sessions. Zero
    /// writes and zero runner calls beyond <c>GetAsync</c>'s inherited list in every argument.
    /// </summary>
    [Test]
    [Arguments("zero-candidates", 1, 1)]
    [Arguments("one-candidate", 6, 5)]
    [Arguments("two-candidates", 8, 7)]
    public Task C1156_Attention_and_pruning_statement_budgets(string candidates, int helper, int pruneDelta) =>
        Card1156Pending.Skip("S5", nameof(C1156_Attention_and_pruning_statement_budgets));
}
