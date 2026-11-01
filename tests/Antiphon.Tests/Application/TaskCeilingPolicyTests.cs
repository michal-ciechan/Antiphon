using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class TaskCeilingPolicyTests
{
    [Test]
    [Arguments("default")]
    [Arguments("one")]
    [Arguments("cap")]
    [Arguments("zero")]
    [Arguments("negative")]
    [Arguments("over-cap")]
    [Arguments("overflow")]
    [Arguments("reasonless")]
    public void C1610_Declared_budget_validation(string arm)
    {
        long? value = arm switch { "default" => null, "one" => 1, "cap" => 720, "zero" => 0,
            "negative" => -1, "over-cap" => 721, "overflow" => long.MaxValue, _ => 300 };
        var reason = arm == "reasonless" ? " " : "bounded inspection";
        if (arm is "default" or "one" or "cap")
            CeilingHoldFacts.ValidateInitial(value, reason).ShouldBe(value is null ? null : (int)value);
        else Should.Throw<ValidationException>(() => CeilingHoldFacts.ValidateInitial(value, reason));
    }

    [Test]
    [Arguments("phase-wins")]
    [Arguments("boot")]
    [Arguments("heartbeat")]
    [Arguments("uncertain")]
    public void C1610_Phase_dispatch_guards(string arm)
    {
        if (arm == "phase-wins")
        {
            var ceiling = new TaskDeadlinePolicy.Verdict(TaskDeadlinePolicy.DeadlineKind.Ceiling,
                TimeSpan.FromMinutes(240), TimeSpan.FromMinutes(1000), "ToolCall", TimeSpan.FromMinutes(91), "ceiling");
            var phase = new TaskDeadlinePolicy.Verdict(TaskDeadlinePolicy.DeadlineKind.LocalExecution,
                TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(91), "ToolCall", TimeSpan.FromMinutes(91), "phase");
            TaskDeadlinePolicy.Select(ceiling, phase).ShouldBe(phase);
        }
        else if (arm == "heartbeat")
        {
            var now = DateTime.UtcNow;
            TaskDeadlinePolicy.ModelWaitElapsed(now, now.AddMinutes(-241), now.AddMinutes(-21), now.AddMinutes(-1))
                .ShouldBe(TimeSpan.FromMinutes(1));
            TaskDeadlinePolicy.ModelWaitElapsed(now, now.AddMinutes(-241), now.AddMinutes(-21), null)
                .ShouldBe(TimeSpan.FromMinutes(21));
            var task = new AgentTask { Role = AgentTaskRole.Code };
            CeilingHoldFacts.Resolve(task, new DelegationSettings());
            CeilingHoldFacts.Bound(task, new DelegationSettings()).ShouldBe(240);
        }
        else if (arm == "uncertain")
        {
            var generation = DateTime.UtcNow;
            CeilingHoldFacts.CanHold(AgentTaskStatus.Working, generation, generation,
                true, false, true, false, false).ShouldBeFalse();
            CeilingHoldFacts.CanHold(AgentTaskStatus.Working, generation, generation,
                true, true, true, false, false).ShouldBeTrue();
        }
        else
        {
            var generation = DateTime.UtcNow;
            CeilingHoldFacts.CanHold(AgentTaskStatus.Working, generation, generation,
                true, true, true, true, false).ShouldBeFalse();
            CeilingHoldFacts.CanHold(AgentTaskStatus.Working, generation, generation,
                true, true, true, false, false).ShouldBeTrue();
        }
    }
}
