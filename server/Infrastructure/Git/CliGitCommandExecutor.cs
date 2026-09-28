using System.Diagnostics;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>Runs Git with an argument vector and drains both redirected pipes.</summary>
public sealed class CliGitCommandExecutor : IGitCommandExecutor
{
    private readonly IReadOnlyDictionary<string, string> _environment;
    private readonly Action? _onLaunch;
    private readonly string _executable;

    public CliGitCommandExecutor(
        IReadOnlyDictionary<string, string>? environment = null, Action? onLaunch = null,
        string executable = "git")
    {
        _environment = environment ?? new Dictionary<string, string>();
        _onLaunch = onLaunch;
        _executable = executable;
    }

    public async Task<GitCommandResult> ExecuteAsync(
        string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        var start = new ProcessStartInfo(_executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var (key, value) in _environment) start.Environment[key] = value;
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start git process.");
        _onLaunch?.Invoke();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        return new GitCommandResult(process.ExitCode, await stdout, await stderr);
    }
}
