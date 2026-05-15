using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed class WorkspaceHookService
{
    private readonly IWorkspaceHookRunner _runner;
    private readonly ILogger<WorkspaceHookService> _logger;

    public WorkspaceHookService(
        IWorkspaceHookRunner runner,
        ILogger<WorkspaceHookService> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public Task<WorkspaceHookResult?> RunAfterCreateAsync(
        WorkspaceHookContext context,
        WorkflowHooks hooks,
        CancellationToken ct) =>
        RunAbortableAsync(context, hooks, "after_create", ct);

    public Task<WorkspaceHookResult?> RunBeforeRunAsync(
        WorkspaceHookContext context,
        WorkflowHooks hooks,
        CancellationToken ct) =>
        RunAbortableAsync(context, hooks, "before_run", ct);

    public Task<WorkspaceHookResult?> RunBeforeRemoveAsync(
        WorkspaceHookContext context,
        WorkflowHooks hooks,
        CancellationToken ct) =>
        RunAbortableAsync(context, hooks, "before_remove", ct);

    public async Task<WorkspaceHookResult?> RunAfterRunAsync(
        WorkspaceHookContext context,
        WorkflowHooks hooks,
        CancellationToken ct)
    {
        var result = await RunOptionalAsync(context, hooks, "after_run", ct);
        if (result is { Succeeded: false })
        {
            _logger.LogWarning(
                "Workspace after_run hook failed but will not abort. ExitCode={ExitCode}, TimedOut={TimedOut}",
                result.ExitCode,
                result.TimedOut);
        }

        return result;
    }

    private async Task<WorkspaceHookResult?> RunAbortableAsync(
        WorkspaceHookContext context,
        WorkflowHooks hooks,
        string hookName,
        CancellationToken ct)
    {
        var result = await RunOptionalAsync(context, hooks, hookName, ct);
        if (result is null || result.Succeeded)
            return result;

        throw new ConflictException(
            $"Workspace hook '{hookName}' failed with exit code {result.ExitCode}.");
    }

    private Task<WorkspaceHookResult?> RunOptionalAsync(
        WorkspaceHookContext context,
        WorkflowHooks hooks,
        string hookName,
        CancellationToken ct)
    {
        var hook = hooks.GetByName(hookName);
        return _runner.RunAsync(context, hookName, hook, ct);
    }
}
