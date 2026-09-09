using System.Data.Common;
using System.Net.Sockets;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Retry pacing evidence never grants permission to replace a conversation.</summary>
public sealed class RestartFailurePolicy
{
    public RestartFailureKind Classify(Exception exception)
    {
        var chain = Flatten(exception).ToArray();
        if (chain.Any(e => e is DbException { IsTransient: true }
            or HttpRequestException or SocketException or IOException or UnauthorizedAccessException or TimeoutException or OperationCanceledException))
            return RestartFailureKind.Infrastructure;
        if (chain.Any(e => e is AgentSessionService.ResumeTargetMissingException))
            return RestartFailureKind.ContinuityUnavailable;
        if (chain.Any(e => e is AgentLaunchBlockedException or ArgumentException
            or System.ComponentModel.Win32Exception))
            return RestartFailureKind.LaunchOrProcessFailure;
        return RestartFailureKind.Unknown;
    }

    private IEnumerable<Exception> Flatten(Exception exception)
    {
        yield return exception;
        var children = exception is AggregateException aggregate ? aggregate.InnerExceptions
            : exception.InnerException is { } inner ? new[] { inner }.AsEnumerable() : [];
        foreach (var child in children)
            foreach (var item in Flatten(child)) yield return item;
    }

    public bool Observe(AgentSupervisionState state, AgentSession session)
    {
        if (session.Status is not (SessionStatus.Stopped or SessionStatus.Failed)
            || (state.LastObservedRestartSessionId == session.Id
                && state.LastObservedRestartStartedAt == session.StartedAt)) return false;
        state.LastObservedRestartSessionId = session.Id;
        state.LastObservedRestartStartedAt = session.StartedAt;
        var kind = session.RestartFailureKind ?? (session.InteractiveLaunchCompletedAt is not null
            && session.TerminationSource == SessionTerminationSource.ProcessExit
                ? RestartFailureKind.LaunchOrProcessFailure : RestartFailureKind.Unknown);
        Charge(state, kind);
        return true;
    }

    public void Charge(AgentSupervisionState state, RestartFailureKind kind)
    {
        if (kind == RestartFailureKind.ContinuityUnavailable) return;
        state.RestartBackoffFailures = Math.Min(state.RestartBackoffFailures, int.MaxValue - 1) + 1;
        if (kind == RestartFailureKind.LaunchOrProcessFailure)
            state.ConsecutiveFailures = Math.Min(state.ConsecutiveFailures, int.MaxValue - 1) + 1;
    }
}
