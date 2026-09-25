using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class RestartFailureClassificationTests
{
    [Test]
    public void Infrastructure_wrappers_and_cancellation_are_classified_by_evidence()
    {
        var postgres = new PostgresException("database starting", "FATAL", "FATAL", "57P03");
        Exception[] evidence = [postgres, new DbUpdateException("save", postgres),
            new Exception("No conversation found with session ID", new DbUpdateException("wrapped", postgres)),
            new AggregateException(new Exception("other"), new AggregateException(postgres)),
            new SyntheticTransientDbException(),
            new HttpRequestException("connection refused", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused)),
            new AggregateException(new AgentSessionService.ResumeTargetMissingException(), postgres),
            new TimeoutException(), new TaskCanceledException(),
            new OperationCanceledException()];
        var policy = new RestartFailurePolicy();
        foreach (var error in evidence) policy.Classify(error).ShouldBe(RestartFailureKind.Infrastructure);
        policy.Classify(new Exception("57P03 No conversation found with session ID")).ShouldBe(RestartFailureKind.Unknown);
        policy.Classify(new PostgresException("bad config", "ERROR", "ERROR", "42601")).ShouldBe(RestartFailureKind.Unknown);
        policy.Classify(new AgentSessionService.ResumeTargetMissingException()).ShouldBe(RestartFailureKind.ContinuityUnavailable);
    }

    private sealed class SyntheticTransientDbException : System.Data.Common.DbException
    {
        public override bool IsTransient => true;
    }

    [Test]
    public void Terminal_generation_is_charged_once_and_healthy_completion_defines_a_process_failure()
    {
        var policy = new RestartFailurePolicy();
        var state = new AgentSupervisionState { ConsecutiveFailures = 7 };
        var session = new AgentSession { Id = Guid.NewGuid(), StartedAt = DateTime.UtcNow,
            Status = SessionStatus.Failed, RestartFailureKind = RestartFailureKind.Infrastructure };
        policy.Observe(state, session).ShouldBeTrue();
        new RestartFailurePolicy().Observe(state, session).ShouldBeFalse();
        state.ConsecutiveFailures.ShouldBe(7);
        state.RestartBackoffFailures.ShouldBe(1);
        state.ContinuityResumeFailures.ShouldBe(0);
        session.StartedAt = session.StartedAt.AddSeconds(1);
        session.RestartFailureKind = null;
        session.InteractiveLaunchCompletedAt = session.StartedAt;
        session.TerminationSource = SessionTerminationSource.ProcessExit;
        policy.Observe(state, session).ShouldBeTrue();
        state.ConsecutiveFailures.ShouldBe(8);
        state.RestartBackoffFailures.ShouldBe(2);
        state.ContinuityResumeFailures.ShouldBe(1);
    }

    [Test]
    public void C561_only_non_infrastructure_outcomes_charge_the_resume_failure_counter()
    {
        var policy = new RestartFailurePolicy();
        var state = new AgentSupervisionState();
        policy.Charge(state, RestartFailureKind.Unknown);
        state.ContinuityResumeFailures.ShouldBe(1);
        state.RestartBackoffFailures.ShouldBe(1);
        policy.Charge(state, RestartFailureKind.LaunchOrProcessFailure);
        state.ContinuityResumeFailures.ShouldBe(2);
        state.RestartBackoffFailures.ShouldBe(2);
        state.ConsecutiveFailures.ShouldBe(1);
        policy.Charge(state, RestartFailureKind.Infrastructure);
        state.ContinuityResumeFailures.ShouldBe(2);
        state.RestartBackoffFailures.ShouldBe(3);
        policy.Charge(state, RestartFailureKind.ContinuityUnavailable);
        state.ContinuityResumeFailures.ShouldBe(2);
        state.RestartBackoffFailures.ShouldBe(3);
        state.ContinuityResumeFailures = int.MaxValue;
        policy.Charge(state, RestartFailureKind.Unknown);
        state.ContinuityResumeFailures.ShouldBe(int.MaxValue);
    }

    // CARD-0679 R5 repair 2 (review 18f52a40): the runner confirmed the launch's process ran and exited
    // before it was ready. That is a process failure, and the supervisor's consecutive-failure budget
    // is charged for it; Unknown left it uncharged.
    [Test]
    public void Runner_confirmed_early_exit_is_a_launch_or_process_failure_that_charges_consecutive_failures()
    {
        var policy = new RestartFailurePolicy();
        var exited = new RemoteLaunchAlreadyExitedException(
            "runner-a", Guid.NewGuid(), 3, AgentExitReason.ProcessExited, "ProcessExited");

        policy.Classify(exited).ShouldBe(RestartFailureKind.LaunchOrProcessFailure);

        var state = new AgentSupervisionState();
        var session = new AgentSession { Id = Guid.NewGuid(), StartedAt = DateTime.UtcNow,
            Status = SessionStatus.Failed, RestartFailureKind = policy.Classify(exited) };
        policy.Observe(state, session).ShouldBeTrue();
        state.ConsecutiveFailures.ShouldBe(1);
        state.RestartBackoffFailures.ShouldBe(1);
    }
}
