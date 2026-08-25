using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0205. An alert is a REPORT, and the one failure mode a report may never have is dying of
/// the size of what it reports.
///
/// <para>Measured shape, from this machine's own server log: between 2026-08-21 and 2026-08-25
/// every <c>Alerts</c> insert the reconciler attempted failed with
/// <c>22001: value too long for type character varying(4000)</c> — 22 327 of them — because the
/// orphaned-session alert joined every unknown session id into its detail and CARD-0204's leak had
/// grown that list to 190 guids (7.4 KB). <see cref="AlertService"/> catches and logs, so the rows
/// simply never existed.</para>
///
/// <para>And the alert that mattered was not even the oversized one. The pty-host census detector —
/// the thing built to catch exactly the leak that was under way — composes a bounded 617-character
/// detail and failed anyway, 110 times, because it shares the reconciler's scoped
/// <see cref="AppDbContext"/>: EF leaves a failed insert <c>Added</c> in the change tracker, so the
/// census alert's own <c>SaveChanges</c> re-submitted the poison row and died with it. That half is
/// pinned here; the end-to-end divergence is pinned in
/// <c>SessionReconciliationServiceTests.A_forced_divergence_persists_its_alert_rows</c>.</para>
///
/// <para>These tests go through the real <see cref="AlertService"/> against real PostgreSQL. Every
/// existing suite records into an in-memory fake, which is precisely why four days of this were
/// invisible: nothing in the test set had ever asked a column to hold the text. Each test carries
/// its own dedup key and every query is scoped to it (CLAUDE.md: the integration suite shares one
/// database, so a count over an unscoped query is also an assertion about everyone else's rows).
/// </para>
/// </summary>
[Category("Integration")]
public class AlertPersistenceTests
{
    /// <summary>
    /// The generic backstop: whatever a caller composes, the row lands. Clipped is a report;
    /// rejected is silence.
    /// </summary>
    [Test]
    public async Task An_oversized_detail_is_clipped_to_fit_rather_than_dropped()
    {
        var key = NewKey();
        try
        {
            await using var db = NewContext();
            const string head = "the problem this alert exists to report";

            await NewAlertService(db).RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Critical, "reconciler", new string('T', 900),
                    Detail: head + new string('x', 20_000),
                    DedupKey: key),
                CancellationToken.None);

            await using var verify = NewContext();
            var alert = await verify.Alerts.SingleAsync(a => a.DedupKey == key);
            alert.Detail.ShouldNotBeNull();
            alert.Detail!.Length.ShouldBe(Alert.DetailMaxLength);
            alert.Detail.ShouldStartWith(
                head, customMessage: "the head of a report is the part worth keeping");
            alert.Detail.ShouldEndWith(
                "…", customMessage: "a reader must tell a clipped report from a short one");
            // Title has its own, smaller ceiling; all four bounded columns are enforced, not just
            // the one that happened to overflow. (DedupKey is this test's scope, so it is short.)
            alert.Title.Length.ShouldBe(Alert.TitleMaxLength);
        }
        finally
        {
            await CleanupAsync(key);
        }
    }

    /// <summary>A detail that already fits is left exactly as composed — no ellipsis, no trim.</summary>
    [Test]
    public async Task A_detail_that_fits_is_stored_verbatim()
    {
        var key = NewKey();
        try
        {
            await using var db = NewContext();
            var detail = new string('x', Alert.DetailMaxLength);

            await NewAlertService(db).RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Warning, "reconciler", "census", Detail: detail, DedupKey: key),
                CancellationToken.None);

            await using var verify = NewContext();
            (await verify.Alerts.SingleAsync(a => a.DedupKey == key)).Detail.ShouldBe(detail);
        }
        finally
        {
            await CleanupAsync(key);
        }
    }

    /// <summary>
    /// A dedup key past its own ceiling is clipped rather than dropped — the same rule as the
    /// detail, on the column the routing throttle groups by.
    /// </summary>
    [Test]
    public async Task An_oversized_dedup_key_is_clipped_rather_than_dropped()
    {
        var key = NewKey();
        var oversized = key + new string('k', 900);
        try
        {
            await using var db = NewContext();

            await NewAlertService(db).RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Warning, "reconciler", "census", Detail: "short", DedupKey: oversized),
                CancellationToken.None);

            await using var verify = NewContext();
            var alert = await verify.Alerts.SingleAsync(a => a.DedupKey.StartsWith(key));
            alert.DedupKey.Length.ShouldBe(Alert.DedupKeyMaxLength);
        }
        finally
        {
            await CleanupPrefixAsync(key);
        }
    }

    /// <summary>
    /// The collateral-damage half, isolated: someone else's bad row is staged on the shared context
    /// before we ever get there. The alert is not ours to lose over it — it goes out on a context of
    /// its own. This is what killed the census alert 110 times.
    /// </summary>
    [Test]
    public async Task A_poisoned_caller_context_does_not_silence_the_alert()
    {
        var key = NewKey();
        var poisonKey = $"{key}-poison";
        try
        {
            await using var db = NewContext();

            // Staged straight onto the entity, past every clip: the caller's problem, not the alert's.
            db.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid(),
                Severity = AlertSeverity.Warning,
                Source = "someone-else",
                Title = "staged by the caller and too long to save",
                Detail = new string('x', Alert.DetailMaxLength + 1),
                DedupKey = poisonKey,
                CreatedAt = DateTime.UtcNow,
            });

            await NewAlertService(db).RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Critical, "reconciler",
                    $"{AgentIncidentKind.PtyHostCensusDiverged}: session census",
                    Detail: "Session census diverged.",
                    DedupKey: key),
                CancellationToken.None);

            await using var verify = NewContext();
            (await verify.Alerts.AnyAsync(a => a.DedupKey == poisonKey))
                .ShouldBeFalse("the poison row is the one that cannot save");
            (await verify.Alerts.SingleAsync(a => a.DedupKey == key)).Severity
                .ShouldBe(AlertSeverity.Critical);

            // And our row is not left tracked on the caller's context, where it would re-fail every
            // later save in that scope — the mechanism that turned one bad alert into a silent
            // detector for four days.
            db.ChangeTracker.Entries<Alert>()
                .Count(e => e.State == EntityState.Added && e.Entity.DedupKey == key)
                .ShouldBe(0);
        }
        finally
        {
            await CleanupPrefixAsync(key);
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// One service provider for the whole class, over the SHARED test connection string. It exists
    /// only to give <see cref="AlertService"/>'s out-of-band retry a context of its own; building
    /// one per test (or per isolated schema) would leave a pooled Npgsql data source behind for
    /// every distinct connection string, and this assembly already runs close to the server's
    /// connection ceiling.
    /// </summary>
    private static readonly ServiceProvider Provider = new ServiceCollection()
        .AddDbContext<AppDbContext>(options => options.UseNpgsql(
            TestDbFixture.ConnectionString,
            npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }))
        .BuildServiceProvider();

    private static string NewKey() => $"card-0205-{Guid.NewGuid():N}";

    private static AppDbContext NewContext() => new(TestDbFixture.CreateDbContextOptions());

    private static AlertService NewAlertService(AppDbContext db) =>
        new(
            db,
            new MockEventBus(),
            new NullAlertRouter(),
            Provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AlertService>.Instance);

    private static async Task CleanupAsync(string key)
    {
        await using var db = NewContext();
        await db.Alerts.Where(a => a.DedupKey == key).ExecuteDeleteAsync();
    }

    private static async Task CleanupPrefixAsync(string key)
    {
        await using var db = NewContext();
        await db.Alerts.Where(a => a.DedupKey.StartsWith(key)).ExecuteDeleteAsync();
    }
}
