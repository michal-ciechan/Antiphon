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
        IDisposable? lease = null;
        try
        {
            if (_gate is not null)
                lease = await _gate.EnterAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (session.DeadlineReached)
                throw new WorktreeBaseBudgetExceededException("inspection_timeout");
            session.Started(args);

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start git process.");
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();
            var stdoutTask = ReadStreamAsync(process.StandardOutput, stdoutBuilder, ct);
            var stderrTask = ReadStreamAsync(process.StandardError, stderrBuilder, ct);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                throw;
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
        }
        finally
        {
            lease?.Dispose();
        }
    }
}
