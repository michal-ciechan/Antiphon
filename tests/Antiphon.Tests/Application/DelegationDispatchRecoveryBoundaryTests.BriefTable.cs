using Antiphon.Server.Application.Services;
using Shouldly;
namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1150 S2 repair 2: the ensure verdict is one ordered table over positive facts. Each case
/// starts from a pristine unattempted inline brief and flips one fact; every rule decides at
/// least one case, so dropping a rule or one of its conditions turns a named case red.
/// </summary>
public partial class DelegationDispatchRecoveryBoundaryTests
{
    [Test]
    [Arguments("pristine-unattempted", "Reuse", false)]
    [Arguments("task-closed", "Uncertain", false)]
    [Arguments("attempt-changed", "Superseded", false)]
    [Arguments("two-briefs", "Uncertain", true)]
    [Arguments("no-brief-other-evidence", "Uncertain", true)]
    [Arguments("no-brief", "Absent", false)]
    [Arguments("brief-and-other-evidence", "Reuse", false)]
    [Arguments("received", "Received", false)]
    [Arguments("received-payload-released", "Received", false)]
    [Arguments("received-canceled", "Received", false)]
    [Arguments("spill-intact", "Reuse", false)]
    [Arguments("spill-unavailable", "Unavailable", true)]
    [Arguments("spill-released-unreceived", "Unavailable", true)]
    [Arguments("inline-without-goal", "Reuse", false)]
    [Arguments("inline-attempted-without-payload", "AttemptOwned", false)]
    [Arguments("canceled-after-attempt", "Uncertain", true)]
    [Arguments("attempted", "AttemptOwned", false)]
    [Arguments("neither-attempted-nor-unattempted", "Uncertain", true)]
    public Task C1150_Brief_decision_table_flips_one_condition(string condition, string expected, bool hold)
    {
        var facts = new DispatchBriefFacts(
            TaskOpen: true,
            CurrentAttempt: true,
            RecognizedRows: 1,
            OtherCurrentEvidence: false,
            CompleteReceipt: false,
            SpillPointer: false,
            PayloadIntact: true,
            Canceled: false,
            Unattempted: true,
            Attempted: false);
        var sent = facts with { Unattempted = false, Attempted = true };
        facts = condition switch
        {
            "pristine-unattempted" => facts,
            "task-closed" => facts with { TaskOpen = false },
            "attempt-changed" => facts with { CurrentAttempt = false },
            "two-briefs" => facts with { RecognizedRows = 2 },
            "no-brief-other-evidence" => facts with { RecognizedRows = 0, OtherCurrentEvidence = true },
            "no-brief" => facts with { RecognizedRows = 0 },
            "brief-and-other-evidence" => facts with { OtherCurrentEvidence = true },
            "received" => sent with { CompleteReceipt = true },
            // F5: the queue released the spill bytes after the complete UserPrompt.
            "received-payload-released" => sent with { CompleteReceipt = true, SpillPointer = true, PayloadIntact = false },
            "received-canceled" => sent with { CompleteReceipt = true, Canceled = true },
            "spill-intact" => facts with { SpillPointer = true },
            // F2 and a genuine spill whose payload is missing, corrupt or unreadable.
            "spill-unavailable" => facts with { SpillPointer = true, PayloadIntact = false },
            "spill-released-unreceived" => sent with { SpillPointer = true, PayloadIntact = false },
            // F4: inline text is its own payload; a path it mentions is not a spill to lose.
            "inline-without-goal" => facts with { PayloadIntact = false },
            "inline-attempted-without-payload" => sent with { PayloadIntact = false },
            "canceled-after-attempt" => sent with { Canceled = true },
            "attempted" => sent,
            "neither-attempted-nor-unattempted" => facts with { Unattempted = false },
            _ => throw new ArgumentOutOfRangeException(nameof(condition)),
        };

        var decision = DispatchBriefEvidence.Decide(facts);
        decision.Kind.ToString().ShouldBe(expected, condition);
        decision.Hold.ShouldBe(hold, condition);
        if (hold)
            decision.Reason.ShouldNotBeNull(condition);
        if (decision.Reason is not null)
            decision.Reason.Contains("delivered", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(condition);
        return Task.CompletedTask;
    }
}
