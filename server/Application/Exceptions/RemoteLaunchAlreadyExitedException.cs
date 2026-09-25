using System.Globalization;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0679 R5 repair (review 137c1631): the runner answered a re-sent Launch with the session that
/// generation already started, ran and exited. The launch happened and ended: it is neither a
/// transport loss to retry nor a fresh start, and there is nothing left on the runner to kill. The
/// message is what the launch persists as <c>FailureReason</c>.
/// </summary>
public sealed class RemoteLaunchAlreadyExitedException : Exception
{
    public RemoteLaunchAlreadyExitedException(
        string runnerId, Guid sessionId, int? exitCode, AgentExitReason exitReason, string runnerExitReason)
        : base(Describe(runnerId, sessionId, exitCode, exitReason, runnerExitReason))
    {
        RunnerId = runnerId;
        SessionId = sessionId;
        ExitCode = exitCode;
        ExitReason = exitReason;
    }

    public string RunnerId { get; }
    public Guid SessionId { get; }
    public int? ExitCode { get; }
    public AgentExitReason ExitReason { get; }

    private static string Describe(
        string runnerId, Guid sessionId, int? exitCode, AgentExitReason exitReason, string runnerExitReason)
    {
        var code = exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        var reason = string.IsNullOrWhiteSpace(runnerExitReason) ? exitReason.ToString() : runnerExitReason;
        return $"Remote launch of session {sessionId} on runner '{runnerId}' already ran and exited before its "
            + $"acknowledgement arrived (exit code {code}, reason {reason}); the same generation is not started again.";
    }
}
