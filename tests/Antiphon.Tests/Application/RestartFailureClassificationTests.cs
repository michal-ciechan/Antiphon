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
        session.StartedAt = session.StartedAt.AddSeconds(1);
        session.RestartFailureKind = null;
        session.InteractiveLaunchCompletedAt = session.StartedAt;
        session.TerminationSource = SessionTerminationSource.ProcessExit;
        policy.Observe(state, session).ShouldBeTrue();
        state.ConsecutiveFailures.ShouldBe(8);
        state.RestartBackoffFailures.ShouldBe(2);
    }
}
