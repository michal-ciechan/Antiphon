using System.Diagnostics;
using System.Text;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Infrastructure.WorkspaceHooks;

public sealed class WorkspaceHookRunner : IWorkspaceHookRunner
{
    private const int MaxCapturedOutputChars = 64 * 1024;
    private static readonly TimeSpan KillGracePeriod = TimeSpan.FromSeconds(5);

    private readonly ILogger<WorkspaceHookRunner> _logger;

    public WorkspaceHookRunner(ILogger<WorkspaceHookRunner> logger)
    {
        _logger = logger;
    }

    public async Task<WorkspaceHookResult?> RunAsync(
        WorkspaceHookContext context,
        string hookName,
        WorkspaceHookDefinition? hook,
        CancellationToken ct)
    {
        if (hook is null)
            return null;

        return await RunAsync(context, hookName, hook.Command, hook.Timeout, ct);
    }

    public async Task<WorkspaceHookResult> RunAsync(
        WorkspaceHookContext context,
        string hookName,
        string command,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var workspacePath = ResolveWorkspacePath(context.WorkspacePath);
        if (string.IsNullOrWhiteSpace(hookName))
            throw new ValidationException(nameof(hookName), "Hook name must not be empty.");

        if (string.IsNullOrWhiteSpace(command))
            throw new ValidationException(nameof(command), "Hook command must not be empty.");

        if (timeout <= TimeSpan.Zero)
            throw new ValidationException(nameof(timeout), "Hook timeout must be positive.");

        var psi = BuildProcessStartInfo(workspacePath, command);
        ApplyEnvironment(psi, context, hookName, workspacePath);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new LimitedOutput(MaxCapturedOutputChars);
        var stderr = new LimitedOutput(MaxCapturedOutputChars);

        process.OutputDataReceived += (_, e) => stdout.AppendLine(e.Data);
        process.ErrorDataReceived += (_, e) => stderr.AppendLine(e.Data);

        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
            throw new InvalidOperationException("Failed to start workspace hook process.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _logger.LogInformation(
            "Running workspace hook {HookName} in {WorkspacePath}",
            hookName,
            workspacePath);

        var timedOut = false;
        var exited = await WaitForExitWithinAsync(process, timeout, ct);
        if (!exited)
        {
            timedOut = true;
            TryKillProcessTree(process);
            exited = await WaitForExitWithinAsync(process, KillGracePeriod, CancellationToken.None);
        }

        if (exited)
        {
            try
            {
                process.WaitForExit();
            }
            catch (InvalidOperationException)
            {
                // Process was already released after a timeout race.
            }
        }

        stopwatch.Stop();
        var exitCode = exited ? process.ExitCode : -1;
        var result = new WorkspaceHookResult(
            hookName,
            command,
            exitCode,
            timedOut,
            stdout.ToString(),
            stderr.ToString(),
            stopwatch.Elapsed);

        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Workspace hook {HookName} finished unsuccessfully. ExitCode={ExitCode}, TimedOut={TimedOut}",
                hookName,
                result.ExitCode,
                result.TimedOut);
        }

        return result;
    }

    private static ProcessStartInfo BuildProcessStartInfo(string workspacePath, string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh",
            WorkingDirectory = workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(BuildPowerShellCommand(command));
        }
        else
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        return psi;
    }

    private static string BuildPowerShellCommand(string command) =>
        $"& {{ {command} }}; if ($LASTEXITCODE -ne $null) {{ exit $LASTEXITCODE }}";

    private static void ApplyEnvironment(
        ProcessStartInfo psi,
        WorkspaceHookContext context,
        string hookName,
        string workspacePath)
    {
        if (context.Environment is not null)
        {
            foreach (var (key, value) in context.Environment)
                psi.Environment[key] = value;
        }

        psi.Environment["ANTIPHON_HOOK_NAME"] = hookName;
        psi.Environment["ANTIPHON_WORKSPACE_PATH"] = workspacePath;
        psi.Environment["ANTIPHON_CARD_ID"] = context.CardId ?? string.Empty;
        psi.Environment["ANTIPHON_WORKTREE_PATH"] = string.IsNullOrWhiteSpace(context.WorktreePath)
            ? workspacePath
            : Path.GetFullPath(context.WorktreePath);
    }

    private static string ResolveWorkspacePath(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            throw new ValidationException(nameof(workspacePath), "Workspace path must not be empty.");

        var fullPath = Path.GetFullPath(workspacePath);
        if (!Directory.Exists(fullPath))
            throw new NotFoundException("Workspace", fullPath);

        return fullPath;
    }

    private static async Task<bool> WaitForExitWithinAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup; callers receive the timeout/cancellation result.
        }
    }

    private sealed class LimitedOutput
    {
        private readonly int _maxLength;
        private readonly StringBuilder _builder = new();
        private bool _truncated;

        public LimitedOutput(int maxLength)
        {
            _maxLength = maxLength;
        }

        public void AppendLine(string? value)
        {
            if (value is null)
                return;

            lock (_builder)
            {
                if (_truncated)
                    return;

                var remaining = _maxLength - _builder.Length;
                if (remaining <= 0)
                {
                    MarkTruncated();
                    return;
                }

                var line = value + Environment.NewLine;
                if (line.Length <= remaining)
                {
                    _builder.Append(line);
                    return;
                }

                _builder.Append(line.AsSpan(0, remaining));
                MarkTruncated();
            }
        }

        public override string ToString()
        {
            lock (_builder)
                return _builder.ToString();
        }

        private void MarkTruncated()
        {
            _truncated = true;
            _builder.AppendLine();
            _builder.AppendLine("[output truncated]");
        }
    }
}
