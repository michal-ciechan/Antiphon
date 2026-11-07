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
    /// burn-in. In-memory storage is process-lifetime only; a restart still loses that history.
    /// </summary>
    public int HistoryRetentionDays { get; set; } = 8;

    /// <summary>CARD-1743. Unset configuration stays on <see cref="InMemoryStorageName"/>.</summary>
    public const string InMemoryStorageName = "InMemory";

    /// <summary>CARD-1743. Opt-in durable storage. The desktop does not set this.</summary>
    public const string PostgreSqlStorageName = "PostgreSql";

    /// <summary>
    /// CARD-1743. <see cref="InMemoryStorageName"/> is today's desktop behaviour.
    /// <see cref="PostgreSqlStorageName"/> keeps recurring jobs across a process restart.
    /// </summary>
    public string Storage { get; set; } = InMemoryStorageName;

    /// <summary>
    /// Schema Hangfire owns when <see cref="Storage"/> is <see cref="PostgreSqlStorageName"/>.
    /// Not the EF <c>public</c> schema and not an EF migration.
    /// </summary>
    public string SchemaName { get; set; } = "hangfire";

    public bool UsesPostgreSql =>
        string.Equals(Storage, PostgreSqlStorageName, StringComparison.Ordinal);
}
