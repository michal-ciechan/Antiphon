namespace Antiphon.Server.Application.Interfaces;

/// <summary>The external command boundary used by GitService.</summary>
public interface IGitCommandExecutor
{
    Task<GitCommandResult> ExecuteAsync(
        string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken ct);
}

public sealed record GitCommandResult(int ExitCode, string Stdout, string Stderr);
