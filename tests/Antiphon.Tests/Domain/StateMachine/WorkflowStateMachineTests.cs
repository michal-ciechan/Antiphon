using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Domain.StateMachine;

public class WorkflowStateMachineTests
{
    // --- Valid transitions ---

    [Test]
    public void CanTransition_Created_To_Running_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Created, WorkflowStatus.Running)
            .ShouldBeTrue();
    }

    [Test]
    public void CanTransition_Running_To_GateWaiting_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Running, WorkflowStatus.GateWaiting)
            .ShouldBeTrue();
    }

    [Test]
    public void CanTransition_Running_To_Paused_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Running, WorkflowStatus.Paused)
            .ShouldBeTrue();
    }

    [Test]
    public void CanTransition_Running_To_Failed_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Running, WorkflowStatus.Failed)
            .ShouldBeTrue();
    }

    [Test]
    public void CanTransition_Running_To_Completed_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Running, WorkflowStatus.Completed)
            .ShouldBeTrue();
    }

    [Test]
    public void CanTransition_GateWaiting_To_Running_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.GateWaiting, WorkflowStatus.Running)
            .ShouldBeTrue();
    }

    [Test]
    public void CanTransition_GateWaiting_To_Paused_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.GateWaiting, WorkflowStatus.Paused)
            .ShouldBeTrue();
    }

    [Test]
    public void CanTransition_GateWaiting_To_Abandoned_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.GateWaiting, WorkflowStatus.Abandoned)
            .ShouldBeTrue();
    }

    [Test]
    public void CanTransition_Paused_To_Running_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Paused, WorkflowStatus.Running)
            .ShouldBeTrue();
    }

    [Test]
    public void CanTransition_Paused_To_Abandoned_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Paused, WorkflowStatus.Abandoned)
            .ShouldBeTrue();
    }

    [Test]
    public void CanTransition_Failed_To_Running_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Failed, WorkflowStatus.Running)
            .ShouldBeTrue();
    }

    // --- Terminal states have no transitions ---

    [Test]
    public void CanTransition_Completed_To_Any_ReturnsFalse()
    {
        foreach (var status in Enum.GetValues<WorkflowStatus>())
        {
            WorkflowStateMachine.CanTransition(WorkflowStatus.Completed, status)
                .ShouldBeFalse($"Completed should not transition to {status}");
        }
    }

    [Test]
    public void CanTransition_Abandoned_To_Any_ReturnsFalse()
    {
        foreach (var status in Enum.GetValues<WorkflowStatus>())
        {
            WorkflowStateMachine.CanTransition(WorkflowStatus.Abandoned, status)
                .ShouldBeFalse($"Abandoned should not transition to {status}");
        }
    }

    // --- Invalid transitions ---

    [Test]
    [Arguments(WorkflowStatus.Created, WorkflowStatus.Completed)]
    [Arguments(WorkflowStatus.Created, WorkflowStatus.Failed)]
    [Arguments(WorkflowStatus.Created, WorkflowStatus.Paused)]
    [Arguments(WorkflowStatus.Created, WorkflowStatus.GateWaiting)]
    [Arguments(WorkflowStatus.Created, WorkflowStatus.Abandoned)]
    [Arguments(WorkflowStatus.Running, WorkflowStatus.Created)]
    [Arguments(WorkflowStatus.Running, WorkflowStatus.Abandoned)]
    [Arguments(WorkflowStatus.GateWaiting, WorkflowStatus.Completed)]
    [Arguments(WorkflowStatus.GateWaiting, WorkflowStatus.Failed)]
    [Arguments(WorkflowStatus.GateWaiting, WorkflowStatus.Created)]
    [Arguments(WorkflowStatus.Paused, WorkflowStatus.Completed)]
    [Arguments(WorkflowStatus.Paused, WorkflowStatus.Failed)]
    [Arguments(WorkflowStatus.Paused, WorkflowStatus.GateWaiting)]
    [Arguments(WorkflowStatus.Paused, WorkflowStatus.Created)]
    [Arguments(WorkflowStatus.Failed, WorkflowStatus.Completed)]
    [Arguments(WorkflowStatus.Failed, WorkflowStatus.Paused)]
    [Arguments(WorkflowStatus.Failed, WorkflowStatus.GateWaiting)]
    [Arguments(WorkflowStatus.Failed, WorkflowStatus.Abandoned)]
    [Arguments(WorkflowStatus.Failed, WorkflowStatus.Created)]
    public void CanTransition_InvalidTransitions_ReturnFalse(WorkflowStatus from, WorkflowStatus to)
    {
        WorkflowStateMachine.CanTransition(from, to)
            .ShouldBeFalse($"{from} should not transition to {to}");
    }

    // --- Self-transitions are invalid ---

    [Test]
    [Arguments(WorkflowStatus.Created)]
    [Arguments(WorkflowStatus.Running)]
    [Arguments(WorkflowStatus.Paused)]
    [Arguments(WorkflowStatus.GateWaiting)]
    [Arguments(WorkflowStatus.Failed)]
    [Arguments(WorkflowStatus.Completed)]
    [Arguments(WorkflowStatus.Abandoned)]
    public void CanTransition_SelfTransition_ReturnsFalse(WorkflowStatus status)
    {
        WorkflowStateMachine.CanTransition(status, status)
            .ShouldBeFalse($"{status} should not transition to itself");
    }

    // --- GetAvailableTransitions ---

    [Test]
    public void GetAvailableTransitions_Created_ReturnsRunning()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Created)
            .ShouldBe([WorkflowStatus.Running]);
    }

    [Test]
    public void GetAvailableTransitions_Running_ReturnsFourOptions()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Running)
            .ShouldBe([
                WorkflowStatus.GateWaiting,
                WorkflowStatus.Paused,
                WorkflowStatus.Failed,
                WorkflowStatus.Completed
            ]);
    }

    [Test]
    public void GetAvailableTransitions_GateWaiting_ReturnsFourOptions()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.GateWaiting)
            .ShouldBe([
                WorkflowStatus.Running,
                WorkflowStatus.Paused,
                WorkflowStatus.Abandoned,
                WorkflowStatus.CascadeWaiting
            ]);
    }

    [Test]
    public void GetAvailableTransitions_Paused_ReturnsTwoOptions()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Paused)
            .ShouldBe([
                WorkflowStatus.Running,
                WorkflowStatus.Abandoned
            ]);
    }

    [Test]
    public void GetAvailableTransitions_Failed_ReturnsRunning()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Failed)
            .ShouldBe([WorkflowStatus.Running]);
    }

    [Test]
    public void GetAvailableTransitions_Completed_ReturnsEmpty()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Completed)
            .ShouldBeEmpty();
    }

    [Test]
    public void GetAvailableTransitions_Abandoned_ReturnsEmpty()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Abandoned)
            .ShouldBeEmpty();
    }

    // --- Consistency: GetAvailableTransitions matches CanTransition ---

    [Test]
    public void GetAvailableTransitions_IsConsistentWith_CanTransition()
    {
        foreach (var from in Enum.GetValues<WorkflowStatus>())
        {
            var available = WorkflowStateMachine.GetAvailableTransitions(from);
            foreach (var to in Enum.GetValues<WorkflowStatus>())
            {
                var canTransition = WorkflowStateMachine.CanTransition(from, to);
                var isAvailable = available.Contains(to);
                canTransition.ShouldBe(isAvailable,
                    $"CanTransition({from}, {to}) = {canTransition} but GetAvailableTransitions({from}).Contains({to}) = {isAvailable}");
            }
        }
    }
}
