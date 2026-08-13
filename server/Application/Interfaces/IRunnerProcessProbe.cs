namespace Antiphon.Server.Application.Interfaces;

public interface IRunnerProcessProbe
{
    RunnerPathCheck CheckExecutable(string executable);
    RunnerPathCheck CheckFile(string path);
    RunnerPathCheck CheckDirectory(string path);
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
    bool SensitiveOutputDetected = false,
    string? Error = null);
