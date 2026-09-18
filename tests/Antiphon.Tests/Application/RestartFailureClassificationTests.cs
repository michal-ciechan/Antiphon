using Antiphon.Server.Application.Exceptions;
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

    /// <summary>
    /// V-511-3c / G-511-5. Named by TYPE, not only by its inner: a 404-shaped no-answer carries no
    /// transport exception at all, and "nobody answered" must still be paced as transport.
    /// </summary>
    [Test]
    public void C511_V3c_RunnerUnreachable_is_infrastructure_with_or_without_an_inner()
    {
        var policy = new RestartFailurePolicy();
        policy.Classify(new RunnerUnreachableException("no answer", null))
            .ShouldBe(RestartFailureKind.Infrastructure);
        policy.Classify(new RunnerUnreachableException("no answer", new TaskCanceledException()))
            .ShouldBe(RestartFailureKind.Infrastructure);
        policy.Classify(new AggregateException(new Exception("x"), new RunnerUnreachableException("y", null)))
            .ShouldBe(RestartFailureKind.Infrastructure);
    }

    /// <summary>
    /// V-511-14 / G-511-11, G-511-12. A stale runner binary is an external prerequisite no retry
    /// can change: pacing it with the 1.4 h ladder is pure loss, so it is never charged. Observe
    /// still consumes the generation exactly once so the schedule branch cannot re-handle it.
    /// </summary>
    [Test]
    public void C511_V14_RunnerBuildStale_is_classified_and_never_charged()
    {
        var policy = new RestartFailurePolicy();
        policy.Classify(new RunnerCapabilityMismatchException("x")).ShouldBe(RestartFailureKind.RunnerBuildStale);
        policy.Classify(new AggregateException(new Exception("other"), new RunnerCapabilityMismatchException("x")))
            .ShouldBe(RestartFailureKind.RunnerBuildStale);

        var state = new AgentSupervisionState { ConsecutiveFailures = 7, RestartBackoffFailures = 3 };
        policy.Charge(state, RestartFailureKind.RunnerBuildStale);
        state.ConsecutiveFailures.ShouldBe(7);
        state.RestartBackoffFailures.ShouldBe(3);

        var session = new AgentSession
        {
            Id = Guid.NewGuid(), StartedAt = DateTime.UtcNow, Status = SessionStatus.Failed,
            RestartFailureKind = RestartFailureKind.RunnerBuildStale,
        };
        policy.Observe(state, session).ShouldBeTrue();
        state.LastObservedRestartSessionId.ShouldBe(session.Id);
        state.LastObservedRestartStartedAt.ShouldBe(session.StartedAt);
        state.ConsecutiveFailures.ShouldBe(7);
        state.RestartBackoffFailures.ShouldBe(3);
        policy.Observe(state, session).ShouldBeFalse();
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
        session.StartedAt = session.StartedAt.AddSeconds(1);
        session.RestartFailureKind = null;
        session.InteractiveLaunchCompletedAt = session.StartedAt;
        session.TerminationSource = SessionTerminationSource.ProcessExit;
        policy.Observe(state, session).ShouldBeTrue();
        state.ConsecutiveFailures.ShouldBe(8);
        state.RestartBackoffFailures.ShouldBe(2);
    }
}
