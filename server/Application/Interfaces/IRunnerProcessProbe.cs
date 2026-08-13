namespace Antiphon.Server.Application.Interfaces;

public interface IRunnerProcessProbe
{
    Task<RunnerPathCheck> CheckExecutableAsync(
        string executable,
        CancellationToken cancellationToken);
    Task<RunnerPathCheck> CheckFileAsync(
        string path,
        CancellationToken cancellationToken);
    Task<RunnerPathCheck> CheckDirectoryAsync(
        string path,
        CancellationToken cancellationToken);
    Task<RunnerProcessResult> RunAsync(
        RunnerProcessRequest request,
        CancellationToken cancellationToken);
}

public sealed record RunnerPathCheck(bool IsAvailable, string Message);

public sealed record RunnerProcessRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> SecretValues,
    TimeSpan? StopAfter = null);

public sealed record RunnerProcessResult(
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool OutputTruncated = false,
    bool Cancelled = false,
    bool Started = true,
    bool CleanlyStopped = true,
    bool CleanupConfirmed = true,
    bool SensitiveOutputDetected = false,
    string? Error = null);
