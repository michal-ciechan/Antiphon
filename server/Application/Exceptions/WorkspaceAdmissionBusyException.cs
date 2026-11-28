namespace Antiphon.Server.Application.Exceptions;

public sealed class WorkspaceAdmissionBusyException(Exception cause) : ServiceUnavailableException(
    "Workspace admission is busy. Retry shortly.", WorkspaceAdmissionBusyException.ProblemCode,
    cause, retryAfterSeconds: WorkspaceAdmissionBusyException.RetryAfter)
{
    public const string ProblemCode = "workspace_admission_busy";
    private const int RetryAfter = 2;

    internal static TimeSpan RetryDelay() => TimeSpan.FromMilliseconds(RetryAfter * 1000 + Random.Shared.Next(250, 751));
}
