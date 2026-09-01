namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0298: in-process Hangfire worker and dashboard. The server is the test-safety gate —
/// <c>AddHangfire</c> is always registered, but <see cref="ServerEnabled"/> controls
/// <c>AddHangfireServer</c> so a <c>WebApplicationFactory&lt;Program&gt;</c> never starts a worker.
/// </summary>
public sealed class HangfireSettings
{
    /// <summary>
    /// When false, no Hangfire worker is started. Storage and the dashboard remain registered.
    /// Default true for a real server; test hosts force false.
    /// </summary>
    public bool ServerEnabled { get; set; } = true;

    /// <summary>
    /// In-memory job/history expiration. The package default is three hours, which would drop a
    /// daily census's last run before the next fire. Eight days keeps yesterday plus a week of
    /// burn-in. Storage is process-lifetime only; a restart still loses history.
    /// </summary>
    public int HistoryRetentionDays { get; set; } = 8;
}
