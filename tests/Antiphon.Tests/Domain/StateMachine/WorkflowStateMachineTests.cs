using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.StateMachine;
using FluentAssertions;
using Xunit;

namespace Antiphon.Tests.Domain.StateMachine;

public class WorkflowStateMachineTests
{
    // --- Valid transitions ---

    [Fact]
    public void CanTransition_Created_To_Running_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Created, WorkflowStatus.Running)
            .Should().BeTrue();
    }

    [Fact]
    public void CanTransition_Running_To_GateWaiting_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Running, WorkflowStatus.GateWaiting)
            .Should().BeTrue();
    }

    [Fact]
    public void CanTransition_Running_To_Paused_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Running, WorkflowStatus.Paused)
            .Should().BeTrue();
    }

    [Fact]
    public void CanTransition_Running_To_Failed_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Running, WorkflowStatus.Failed)
            .Should().BeTrue();
    }

    [Fact]
    public void CanTransition_Running_To_Completed_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Running, WorkflowStatus.Completed)
            .Should().BeTrue();
    }

    [Fact]
    public void CanTransition_GateWaiting_To_Running_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.GateWaiting, WorkflowStatus.Running)
            .Should().BeTrue();
    }

    [Fact]
    public void CanTransition_GateWaiting_To_Paused_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.GateWaiting, WorkflowStatus.Paused)
            .Should().BeTrue();
    }

    [Fact]
    public void CanTransition_GateWaiting_To_Abandoned_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.GateWaiting, WorkflowStatus.Abandoned)
            .Should().BeTrue();
    }

    [Fact]
    public void CanTransition_Paused_To_Running_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Paused, WorkflowStatus.Running)
            .Should().BeTrue();
    }

    [Fact]
    public void CanTransition_Paused_To_Abandoned_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Paused, WorkflowStatus.Abandoned)
            .Should().BeTrue();
    }

    [Fact]
    public void CanTransition_Failed_To_Running_ReturnsTrue()
    {
        WorkflowStateMachine.CanTransition(WorkflowStatus.Failed, WorkflowStatus.Running)
            .Should().BeTrue();
    }

    // --- Terminal states have no transitions ---

    [Fact]
    public void CanTransition_Completed_To_Any_ReturnsFalse()
    {
        foreach (var status in Enum.GetValues<WorkflowStatus>())
        {
            WorkflowStateMachine.CanTransition(WorkflowStatus.Completed, status)
                .Should().BeFalse($"Completed should not transition to {status}");
        }
    }

    [Fact]
    public void CanTransition_Abandoned_To_Any_ReturnsFalse()
    {
        foreach (var status in Enum.GetValues<WorkflowStatus>())
        {
            WorkflowStateMachine.CanTransition(WorkflowStatus.Abandoned, status)
                .Should().BeFalse($"Abandoned should not transition to {status}");
        }
    }

    // --- Invalid transitions ---

    [Theory]
    [InlineData(WorkflowStatus.Created, WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Created, WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Created, WorkflowStatus.Paused)]
    [InlineData(WorkflowStatus.Created, WorkflowStatus.GateWaiting)]
    [InlineData(WorkflowStatus.Created, WorkflowStatus.Abandoned)]
    [InlineData(WorkflowStatus.Running, WorkflowStatus.Created)]
    [InlineData(WorkflowStatus.Running, WorkflowStatus.Abandoned)]
    [InlineData(WorkflowStatus.GateWaiting, WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.GateWaiting, WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.GateWaiting, WorkflowStatus.Created)]
    [InlineData(WorkflowStatus.Paused, WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Paused, WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Paused, WorkflowStatus.GateWaiting)]
    [InlineData(WorkflowStatus.Paused, WorkflowStatus.Created)]
    [InlineData(WorkflowStatus.Failed, WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Failed, WorkflowStatus.Paused)]
    [InlineData(WorkflowStatus.Failed, WorkflowStatus.GateWaiting)]
    [InlineData(WorkflowStatus.Failed, WorkflowStatus.Abandoned)]
    [InlineData(WorkflowStatus.Failed, WorkflowStatus.Created)]
    public void CanTransition_InvalidTransitions_ReturnFalse(WorkflowStatus from, WorkflowStatus to)
    {
        WorkflowStateMachine.CanTransition(from, to)
            .Should().BeFalse($"{from} should not transition to {to}");
    }

    // --- Self-transitions are invalid ---

    [Theory]
    [InlineData(WorkflowStatus.Created)]
    [InlineData(WorkflowStatus.Running)]
    [InlineData(WorkflowStatus.Paused)]
    [InlineData(WorkflowStatus.GateWaiting)]
    [InlineData(WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Abandoned)]
    public void CanTransition_SelfTransition_ReturnsFalse(WorkflowStatus status)
    {
        WorkflowStateMachine.CanTransition(status, status)
            .Should().BeFalse($"{status} should not transition to itself");
    }

    // --- GetAvailableTransitions ---

    [Fact]
    public void GetAvailableTransitions_Created_ReturnsRunning()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Created)
            .Should().BeEquivalentTo([WorkflowStatus.Running]);
    }

    [Fact]
    public void GetAvailableTransitions_Running_ReturnsFourOptions()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Running)
            .Should().BeEquivalentTo([
                WorkflowStatus.GateWaiting,
                WorkflowStatus.Paused,
                WorkflowStatus.Failed,
                WorkflowStatus.Completed
            ]);
    }

    [Fact]
    public void GetAvailableTransitions_GateWaiting_ReturnsThreeOptions()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.GateWaiting)
            .Should().BeEquivalentTo([
                WorkflowStatus.Running,
                WorkflowStatus.Paused,
                WorkflowStatus.Abandoned
            ]);
    }

    [Fact]
    public void GetAvailableTransitions_Paused_ReturnsTwoOptions()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Paused)
            .Should().BeEquivalentTo([
                WorkflowStatus.Running,
                WorkflowStatus.Abandoned
            ]);
    }

    [Fact]
    public void GetAvailableTransitions_Failed_ReturnsRunning()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Failed)
            .Should().BeEquivalentTo([WorkflowStatus.Running]);
    }

    [Fact]
    public void GetAvailableTransitions_Completed_ReturnsEmpty()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Completed)
            .Should().BeEmpty();
    }

    [Fact]
    public void GetAvailableTransitions_Abandoned_ReturnsEmpty()
    {
        WorkflowStateMachine.GetAvailableTransitions(WorkflowStatus.Abandoned)
            .Should().BeEmpty();
    }

    // --- Consistency: GetAvailableTransitions matches CanTransition ---

    [Fact]
    public void GetAvailableTransitions_IsConsistentWith_CanTransition()
    {
        foreach (var from in Enum.GetValues<WorkflowStatus>())
        {
            var available = WorkflowStateMachine.GetAvailableTransitions(from);
            foreach (var to in Enum.GetValues<WorkflowStatus>())
            {
                var canTransition = WorkflowStateMachine.CanTransition(from, to);
                var isAvailable = available.Contains(to);
                canTransition.Should().Be(isAvailable,
                    $"CanTransition({from}, {to}) = {canTransition} but GetAvailableTransitions({from}).Contains({to}) = {isAvailable}");
            }
        }
    }
}
