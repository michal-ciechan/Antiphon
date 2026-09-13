using System.Diagnostics;
using System.Text;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Git;

public partial class GitService
{
    public async Task<(int ExitCode, string Stdout, string Stderr)> RunWorktreeBaseGitAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        WorktreeBaseGitSession session,
        CancellationToken ct)
    {
        session.Admit(args);
        using var deadline = session.Link(ct);
        IDisposable? lease = null;
        Process? process = null;
        try
        {
            if (_gate is not null)
                lease = await _gate.EnterAsync(deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            if (session.DeadlineReached)
                throw new WorktreeBaseBudgetExceededException("inspection_timeout");
            ct.ThrowIfCancellationRequested();
            session.Started(args);

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start git process.");
            try { process.StandardInput.Close(); } catch { /* best-effort */ }

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();
            var stdoutTask = ReadStreamAsync(process.StandardOutput, stdoutBuilder, deadline.Token);
            var stderrTask = ReadStreamAsync(process.StandardError, stderrBuilder, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(stdoutTask, stderrTask);
            return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new WorktreeBaseBudgetExceededException("inspection_timeout");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            process?.Dispose();
            lease?.Dispose();
        }
    }

    private static void TryKill(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            /* best-effort reap of the owned inspection process */
        }
    }
}
