using Antiphon.Agents.Pty;

namespace Antiphon.SessionRunner;

/// <summary>
/// Periodically prunes PTY-audit dumps (<c>%TEMP%\antiphon-pty-audits</c>) so they stay within the
/// configured age + directory-count caps even when the process runs for days, plus pty-host state
/// (unreferenced shadow-copy version dirs, stale host logs). Runs once on startup and then on a
/// fixed interval; best-effort and never throws.
/// </summary>
public sealed class AuditCleanupService(SessionRunnerRuntime runtime) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RunPass();

        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                RunPass();
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private void RunPass()
    {
        PtySessionAudit.PruneOldAudits();
        runtime.CleanupPtyHostState();
    }
}
