using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1151 S1 (decision Q-1 option B): the pure boot disposition over a hand-built observation,
/// the way <c>AbsentLaunchPolicyTests</c> drives <c>AbsentLaunchPolicy</c>. Design V-2, re-derived
/// for option B: the disposition is <c>NotBoot</c> or <c>DetectOnly</c> (there is no failure
/// member), and the emission is a whitelist read in a fixed order. Each argument first decides an
/// independently built pristine observation (every positive condition present: an emitted
/// <c>BootStallDetected</c>) and then flips exactly the named condition.
/// </summary>
[Category("Unit")]
public class BootStallPolicyTests
{
    private static readonly DateTime PromptAt = new(2026, 10, 8, 6, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// V-2. One positive condition removed per argument. Decisive: the pristine observation is
    /// DetectOnly with Stage Detected; the flipped one keeps DetectOnly with no stage and names
    /// the condition, except <c>model-reply-present</c>, which alone is NotBoot.
    /// <c>would-be-absent-pristine</c> is option A's admitted shape (the session is gone and
    /// every absence fact holds): under option B it can only ever be DetectOnly.
    /// </summary>
    [Test]
    [Arguments("model-reply-present")]
    [Arguments("session-row-missing")]
    [Arguments("session-terminal")]
    [Arguments("brief-pending")]
    [Arguments("brief-state-unknown")]
    [Arguments("api-recovery-unresolved")]
    [Arguments("api-recovery-unknown")]
    [Arguments("commit-recovery-pending")]
    [Arguments("identity-changed")]
    [Arguments("stage-not-due")]
    [Arguments("would-be-absent-pristine")]
    public Task C1151_Whitelist_requires_positive_evidence(string missing)
    {
        Enum.GetValues<BootStallPolicy.Disposition>().ShouldBe(
            [BootStallPolicy.Disposition.NotBoot, BootStallPolicy.Disposition.DetectOnly],
            "option B has no automatic failure disposition");

        var pristine = BootStallPolicy.Decide(Pristine());
        pristine.Disposition.ShouldBe(BootStallPolicy.Disposition.DetectOnly);
        pristine.Stage.ShouldBe(BootStallPolicy.Stage.Detected);
        pristine.Reason.ShouldBe(BootStallPolicy.DetectedToken);

        var flipped = Pristine() with { };
        string? expectedReason = missing;
        switch (missing)
        {
            case "model-reply-present":
                flipped = flipped with { Boot = null };
                expectedReason = "model-reply-or-no-prompt";
                break;
            case "session-row-missing":
                flipped = flipped with { SessionTerminal = null };
                break;
            case "session-terminal":
                flipped = flipped with { SessionTerminal = true };
                expectedReason = "session-terminal-reconciler-owned";
                break;
            case "brief-pending":
                flipped = flipped with { BriefPending = true };
                expectedReason = "brief-pending-watchdog-owned";
                break;
            case "brief-state-unknown":
                flipped = flipped with { BriefPending = null };
                break;
            case "api-recovery-unresolved":
                flipped = flipped with { ApiRecoveryUnresolved = true };
                break;
            case "api-recovery-unknown":
                flipped = flipped with { ApiRecoveryUnresolved = null };
                break;
            case "commit-recovery-pending":
                flipped = flipped with { CommitRecoveryPending = true };
                break;
            case "identity-changed":
                flipped = flipped with { IdentityMatches = false };
                break;
            case "stage-not-due":
                flipped = flipped with { Now = PromptAt.AddMinutes(7) };
                break;
            case "would-be-absent-pristine":
                // The session row is gone, the runner lists nothing, the workspace is quiet and
                // the attempt matches: still a boot episode, still never a failure. The row is
                // the dead-session reconciler's (A-1), so nothing is written either.
                flipped = flipped with { SessionTerminal = true, Now = PromptAt.AddHours(5) };
                expectedReason = "session-terminal-reconciler-owned";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(missing), missing, null);
        }

        var decision = BootStallPolicy.Decide(flipped);
        decision.Stage.ShouldBe(BootStallPolicy.Stage.None, missing);
        decision.Reason.ShouldBe(expectedReason);
        decision.Disposition.ShouldBe(
            missing == "model-reply-present"
                ? BootStallPolicy.Disposition.NotBoot
                : BootStallPolicy.Disposition.DetectOnly);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The stage clock is the prompt's own: Detected at <c>promptAt + boot wait</c>, NeedsOperator
    /// at <c>promptAt + max(boot wait, model wait)</c> (20 when the model wait is disarmed), equality
    /// included, and a disabled boot wait drops only the first stage.
    /// </summary>
    [Test]
    public Task C1151_Stage_boundaries_come_from_the_prompt_clock()
    {
        var facts = Facts(bootWait: 8, modelWait: 20);
        facts.BootDueAt.ShouldBe(PromptAt.AddMinutes(8));
        facts.OperatorDueAt.ShouldBe(PromptAt.AddMinutes(20));
        BootStallPolicy.DueStage(facts, PromptAt.AddMinutes(8).AddTicks(-10)).ShouldBe(BootStallPolicy.Stage.None);
        BootStallPolicy.DueStage(facts, PromptAt.AddMinutes(8)).ShouldBe(BootStallPolicy.Stage.Detected);
        BootStallPolicy.DueStage(facts, PromptAt.AddMinutes(20).AddTicks(-10)).ShouldBe(BootStallPolicy.Stage.Detected);
        BootStallPolicy.DueStage(facts, PromptAt.AddMinutes(20)).ShouldBe(BootStallPolicy.Stage.NeedsOperator);

        var disabled = Facts(bootWait: 0, modelWait: 0);
        disabled.BootDueAt.ShouldBeNull();
        disabled.OperatorDueAt.ShouldBe(PromptAt.AddMinutes(BootStallPolicy.DefaultOperatorMinutes));
        BootStallPolicy.DueStage(disabled, PromptAt.AddMinutes(19)).ShouldBe(BootStallPolicy.Stage.None);

        Facts(bootWait: 8, modelWait: 5).OperatorDueAt.ShouldBe(PromptAt.AddMinutes(8), "never before the boot stage");

        var key = BootStallPolicy.EpisodeKey(Guid.NewGuid(), 1, Guid.NewGuid(), facts);
        var detail = BootStallPolicy.Detail(BootStallPolicy.Stage.Detected, key, facts);
        BootStallPolicy.IsRecorded([detail], key, BootStallPolicy.Stage.Detected).ShouldBeTrue();
        BootStallPolicy.IsRecorded([detail], key, BootStallPolicy.Stage.NeedsOperator).ShouldBeFalse();
        var escalated = BootStallPolicy.Detail(BootStallPolicy.Stage.NeedsOperator, key, facts);
        BootStallPolicy.IsRecorded([escalated], key, BootStallPolicy.Stage.Detected)
            .ShouldBeTrue("a recorded escalation covers the earlier stage after a clock step back");
        BootStallPolicy.IsRecorded([detail], key + "0", BootStallPolicy.Stage.Detected).ShouldBeFalse();
        return Task.CompletedTask;
    }

    private static BootStallFacts Facts(int bootWait, int modelWait) =>
        BootStallPolicy.Facts(
            PromptAt.AddMinutes(-1), PromptAt.AddMinutes(-1),
            new BootReplyWatch.BootTurn(PromptSequence: 3, PromptAt: PromptAt, PromptCount: 1),
            bootWait, modelWait);

    private static BootStallPolicy.Observation Pristine() => new(
        Facts(bootWait: 8, modelWait: 20),
        SessionTerminal: false,
        BriefPending: false,
        ApiRecoveryUnresolved: false,
        CommitRecoveryPending: false,
        IdentityMatches: true,
        Now: PromptAt.AddMinutes(9));
}
