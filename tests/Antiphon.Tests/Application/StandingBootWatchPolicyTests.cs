using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1156 S2 (option A, detection only): the pure standing-boot emission whitelist
/// (<c>StandingBootWatchPolicy</c>) over a hand-built <c>StandingBootWatchObservation</c>, the way
/// <see cref="BootStallPolicyTests"/> drives <c>BootStallPolicy</c>. Design V-3 and V-4 in
/// <c>docs/superpowers/plans/2026-10-08-card-1156-alwayson-boot-watchdog-test-design.md</c>.
/// Every nullable observation field is unknown when null, and unknown is never admitted; a refused
/// emission is never a recovery of any kind, because the policy has no recovery member at all.
/// </summary>
[Category("Unit")]
public class StandingBootWatchPolicyTests
{
    /// <summary>
    /// V-3. One positive condition removed per argument. <c>admitted</c> is the pristine
    /// observation (every condition present, nine minutes after the prompt): stage Detected.
    /// Every other argument flips exactly the named condition and must select stage None with the
    /// named reason, with the same disposition (<c>Observe</c>: there is no stop, restart or
    /// latch member to select). The <c>*-unknown</c> arguments set the field to null rather than
    /// to a default false, so a mutant that reads a null as "not blocked" goes red.
    /// </summary>
    [Test]
    [Arguments("admitted")]
    [Arguments("deadline-zero")]
    [Arguments("deadline-negative")]
    [Arguments("provider-unsupported")]
    [Arguments("provider-unknown")]
    [Arguments("rules-pending")]
    [Arguments("rules-failed")]
    [Arguments("session-missing")]
    [Arguments("session-terminal")]
    [Arguments("ended-at-set")]
    [Arguments("generation-unknown")]
    [Arguments("owner-missing")]
    [Arguments("owner-ambiguous")]
    [Arguments("owner-unknown")]
    [Arguments("owner-not-alwayson")]
    [Arguments("owner-conflict")]
    [Arguments("task-queued")]
    [Arguments("task-dispatched")]
    [Arguments("task-working")]
    [Arguments("task-blocked")]
    [Arguments("task-unknown")]
    [Arguments("prompt-missing")]
    [Arguments("reply-present")]
    [Arguments("reply-unknown")]
    [Arguments("identity-changed")]
    public Task C1156_Emission_requires_each_positive_condition(string flip) =>
        Card1156Pending.Skip("S2", nameof(C1156_Emission_requires_each_positive_condition));

    /// <summary>
    /// V-4. Stages on the prompt clock with the shipped 8/20 defaults: just-before-boot (None),
    /// at-boot (Detected at equality, key <c>standingBoot:v1;g=;l=;p=;stage=detected</c>),
    /// just-before-operator (Detected), at-operator (NeedsOperator at equality, stage=operator key),
    /// boot-longer-than-model (boot 30, model 20: operator due at 30), model-disabled (model 0:
    /// operator due at 20, boot 8 still Detected first), first-seen-after-operator (one
    /// NeedsOperator, no Detected stage due in the same decision), clock-rollback (a recorded
    /// operator receipt covers a later Detected decision: no duplicate, no downgrade).
    /// </summary>
    [Test]
    [Arguments("just-before-boot")]
    [Arguments("at-boot")]
    [Arguments("just-before-operator")]
    [Arguments("at-operator")]
    [Arguments("boot-longer-than-model")]
    [Arguments("model-disabled")]
    [Arguments("first-seen-after-operator")]
    [Arguments("clock-rollback")]
    public Task C1156_Stages_use_the_prompt_clock(string moment) =>
        Card1156Pending.Skip("S2", nameof(C1156_Stages_use_the_prompt_clock));
}
