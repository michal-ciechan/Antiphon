using Antiphon.Agents.Pty;

namespace Antiphon.SessionRunner;

/// <summary>
/// Periodically prunes PTY-audit dumps (<c>%TEMP%\antiphon-pty-audits</c>) so they stay within the
/// configured age + directory-count caps even when the process runs for days. Runs once on startup and
/// then on a fixed interval; best-effort and never throws.
/// </summary>
public sealed class AuditCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PtySessionAudit.PruneOldAudits();

        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                PtySessionAudit.PruneOldAudits();
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
