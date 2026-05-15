using Antiphon.Server.Domain.ValueObjects;

namespace Antiphon.Server.Application.Interfaces;

public interface IWorkspaceHookRunner
{
    Task<WorkspaceHookResult> RunAsync(
        WorkspaceHookContext context,
        string hookName,
        string command,
        TimeSpan timeout,
        CancellationToken ct);

    Task<WorkspaceHookResult?> RunAsync(
        WorkspaceHookContext context,
        string hookName,
        WorkspaceHookDefinition? hook,
        CancellationToken ct);
}

public sealed record WorkspaceHookContext(
    string WorkspacePath,
    string? CardId = null,
    string? WorktreePath = null,
    IReadOnlyDictionary<string, string>? Environment = null);

public sealed record WorkspaceHookResult(
    string HookName,
    string Command,
    int ExitCode,
    bool TimedOut,
    string Stdout,
    string Stderr,
    TimeSpan Duration)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}
