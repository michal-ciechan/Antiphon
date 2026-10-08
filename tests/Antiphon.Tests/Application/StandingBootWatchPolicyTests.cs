using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
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
    private static readonly DateTime PromptAt = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Generation = PromptAt.AddHours(-6);
    private static readonly Guid Owner = Guid.Parse("11111111-2222-3333-4444-555555555555");

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
    public async Task C1156_Emission_requires_each_positive_condition(string flip)
    {
        var baseline = Admitted(PromptAt.AddMinutes(9));
        var (observation, reason) = flip switch
        {
            "admitted" => (baseline, StandingBootWatchPolicy.DetectedStage),
            "deadline-zero" => (baseline with { BootWaitMinutes = 0 }, "boot-wait-disabled"),
            "deadline-negative" => (baseline with { BootWaitMinutes = -1 }, "boot-wait-disabled"),
            "provider-unsupported" => (baseline with { ProviderVerified = false }, "provider-unverified"),
            "provider-unknown" => (baseline with { ProviderVerified = null }, "provider-unknown"),
            "rules-pending" => (baseline with { GrokRules = GrokRulesState.Pending }, "grok-rules-not-ready"),
            "rules-failed" => (baseline with { GrokRules = GrokRulesState.Failed }, "grok-rules-not-ready"),
            "session-missing" => (baseline with { SessionLive = null }, "session-missing"),
            "session-terminal" => (baseline with { SessionLive = false }, "session-terminal"),
            "ended-at-set" => (baseline with { EndedAtNull = false }, "session-ended"),
            "generation-unknown" => (baseline with { Generation = null }, "generation-unknown"),
            "owner-missing" => (baseline with { OwnerCount = 0, OwnerAlwaysOn = null, OwnerAgentId = null }, "owner-missing"),
            "owner-ambiguous" => (baseline with { OwnerCount = 2, OwnerAlwaysOn = null, OwnerAgentId = null }, "owner-ambiguous"),
            "owner-unknown" => (baseline with { OwnerCount = null }, "owner-unknown"),
            "owner-not-alwayson" => (baseline with { OwnerAlwaysOn = false }, "owner-not-alwayson"),
            "owner-conflict" => (baseline with { StandingAgentConflict = true }, "owner-conflict"),
            "task-queued" => (baseline with { TaskOwner = StandingBootTaskOwner.Queued }, "task-owned"),
            "task-dispatched" => (baseline with { TaskOwner = StandingBootTaskOwner.Dispatched }, "task-owned"),
            "task-working" => (baseline with { TaskOwner = StandingBootTaskOwner.Working }, "task-owned"),
            "task-blocked" => (baseline with { TaskOwner = StandingBootTaskOwner.Blocked }, "task-owned"),
            "task-unknown" => (baseline with { TaskOwner = null }, "task-unknown"),
            "prompt-missing" => (baseline with { PromptSequence = null, PromptAt = null }, "prompt-missing"),
            "reply-present" => (baseline with { ReplyObserved = true }, "reply-observed"),
            "reply-unknown" => (baseline with { ReplyObserved = null }, "reply-unknown"),
            "identity-changed" => (baseline with { IdentityMatches = false }, "identity-changed"),
            _ => throw new ArgumentOutOfRangeException(nameof(flip), flip, null),
        };

        // The policy has exactly one disposition and three notification stages: nothing it can
        // return stops, restarts, latches or re-queues a session.
        Enum.GetNames<StandingBootWatchPolicy.Disposition>().ShouldBe(["Observe"]);
        Enum.GetNames<StandingBootWatchPolicy.Stage>().ShouldBe(["None", "Detected", "NeedsOperator"]);

        var decision = StandingBootWatchPolicy.Decide(observation);

        decision.Disposition.ShouldBe(StandingBootWatchPolicy.Disposition.Observe);
        decision.Reason.ShouldBe(reason, flip);
        if (flip == "admitted")
        {
            decision.Stage.ShouldBe(StandingBootWatchPolicy.Stage.Detected);
            decision.Facts.ShouldNotBeNull();
            decision.Facts.PromptAt.ShouldBe(PromptAt);
            decision.Facts.BootDueAt.ShouldBe(PromptAt.AddMinutes(8));
            decision.Facts.OperatorDueAt.ShouldBe(PromptAt.AddMinutes(20));
        }
        else
        {
            decision.Stage.ShouldBe(StandingBootWatchPolicy.Stage.None, $"{flip}: a refused condition emits nothing");
            decision.Facts.ShouldBeNull($"{flip}: no episode facts are handed to a writer");
        }

        await Task.CompletedTask;
    }

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
    public async Task C1156_Stages_use_the_prompt_clock(string moment)
    {
        var tick = TimeSpan.FromTicks(1);
        var prefix = $"standingBoot:v1;g={Generation.Ticks};l={Generation.Ticks};p=7;";
        switch (moment)
        {
            case "just-before-boot":
            {
                var decision = StandingBootWatchPolicy.Decide(Admitted(PromptAt.AddMinutes(8) - tick));
                decision.Stage.ShouldBe(StandingBootWatchPolicy.Stage.None);
                decision.Reason.ShouldBe("stage-not-due");
                break;
            }
            case "at-boot":
            {
                var decision = StandingBootWatchPolicy.Decide(Admitted(PromptAt.AddMinutes(8)));
                decision.Stage.ShouldBe(StandingBootWatchPolicy.Stage.Detected, "equality is due");
                StandingBootWatchPolicy.EpisodePrefix(decision.Facts!).ShouldBe(prefix);
                StandingBootWatchPolicy.Key(StandingBootWatchPolicy.EpisodePrefix(decision.Facts!), decision.Stage)
                    .ShouldBe(prefix + "stage=detected");
                break;
            }
            case "just-before-operator":
                StandingBootWatchPolicy.Decide(Admitted(PromptAt.AddMinutes(20) - tick)).Stage
                    .ShouldBe(StandingBootWatchPolicy.Stage.Detected);
                break;
            case "at-operator":
            {
                var decision = StandingBootWatchPolicy.Decide(Admitted(PromptAt.AddMinutes(20)));
                decision.Stage.ShouldBe(StandingBootWatchPolicy.Stage.NeedsOperator, "equality is due");
                decision.Reason.ShouldBe(StandingBootWatchPolicy.OperatorStage);
                StandingBootWatchPolicy.Key(StandingBootWatchPolicy.EpisodePrefix(decision.Facts!), decision.Stage)
                    .ShouldBe(prefix + "stage=operator");
                break;
            }
            case "boot-longer-than-model":
            {
                var observe = (DateTime now) => Admitted(now) with { BootWaitMinutes = 30, ModelWaitMinutes = 20 };
                var before = StandingBootWatchPolicy.Decide(observe(PromptAt.AddMinutes(30) - tick));
                before.Stage.ShouldBe(StandingBootWatchPolicy.Stage.None, "the 30-minute boot wait is not over");
                var at = StandingBootWatchPolicy.Decide(observe(PromptAt.AddMinutes(30)));
                at.Facts!.OperatorDueAt.ShouldBe(PromptAt.AddMinutes(30), "operator due is max(boot, model)");
                at.Stage.ShouldBe(StandingBootWatchPolicy.Stage.NeedsOperator);
                break;
            }
            case "model-disabled":
            {
                var observe = (DateTime now) => Admitted(now) with { ModelWaitMinutes = 0 };
                var boot = StandingBootWatchPolicy.Decide(observe(PromptAt.AddMinutes(8)));
                boot.Stage.ShouldBe(StandingBootWatchPolicy.Stage.Detected);
                boot.Facts!.OperatorDueAt.ShouldBe(PromptAt.AddMinutes(20), "a disarmed model wait falls back to 20 minutes");
                StandingBootWatchPolicy.Decide(observe(PromptAt.AddMinutes(20) - tick)).Stage
                    .ShouldBe(StandingBootWatchPolicy.Stage.Detected);
                StandingBootWatchPolicy.Decide(observe(PromptAt.AddMinutes(20))).Stage
                    .ShouldBe(StandingBootWatchPolicy.Stage.NeedsOperator);
                break;
            }
            case "first-seen-after-operator":
            {
                var decision = StandingBootWatchPolicy.Decide(Admitted(PromptAt.AddMinutes(30)));
                decision.Stage.ShouldBe(StandingBootWatchPolicy.Stage.NeedsOperator,
                    "a first observation past the operator due decides the operator stage alone");
                StandingBootWatchPolicy.IsRecorded([], StandingBootWatchPolicy.EpisodePrefix(decision.Facts!), decision.Stage)
                    .ShouldBeFalse();
                break;
            }
            case "clock-rollback":
            {
                // The operator receipt was written at 21 minutes; the clock then steps back to 9.
                var operatorKey = prefix + "stage=operator";
                var rolledBack = StandingBootWatchPolicy.Decide(Admitted(PromptAt.AddMinutes(9)));
                rolledBack.Stage.ShouldBe(StandingBootWatchPolicy.Stage.Detected);
                StandingBootWatchPolicy.IsRecorded([operatorKey], prefix, rolledBack.Stage)
                    .ShouldBeTrue("a recorded operator stage covers a later Detected decision: no downgrade");
                StandingBootWatchPolicy.IsRecorded([operatorKey], prefix, StandingBootWatchPolicy.Stage.NeedsOperator)
                    .ShouldBeTrue("and is itself not recorded twice");
                StandingBootWatchPolicy.IsRecorded([prefix + "stage=detected"], prefix, StandingBootWatchPolicy.Stage.NeedsOperator)
                    .ShouldBeFalse("a detected receipt never covers the operator stage");
                StandingBootWatchPolicy.IsRecorded(
                        [$"standingBoot:v1;g={Generation.Ticks};l={Generation.Ticks};p=8;stage=operator"],
                        prefix, StandingBootWatchPolicy.Stage.Detected)
                    .ShouldBeFalse("another episode's receipt covers nothing here");
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(moment), moment, null);
        }

        await Task.CompletedTask;
    }

    private static StandingBootWatchObservation Admitted(DateTime now) => new(
        BootWaitMinutes: 8,
        ModelWaitMinutes: 20,
        ProviderVerified: true,
        GrokRules: GrokRulesState.None,
        SessionLive: true,
        EndedAtNull: true,
        Generation: Generation,
        LaunchClock: Generation,
        OwnerCount: 1,
        OwnerAlwaysOn: true,
        OwnerAgentId: Owner,
        StandingAgentConflict: false,
        TaskOwner: StandingBootTaskOwner.None,
        PromptSequence: 7,
        PromptAt: PromptAt,
        PromptKind: TranscriptKinds.UserPrompt,
        ReplyObserved: false,
        IdentityMatches: true,
        Now: now);
}
