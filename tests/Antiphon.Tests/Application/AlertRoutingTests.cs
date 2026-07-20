using Antiphon.Messaging;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Alert routing + the Q6 hard send window: at most one channel message per sink per window,
/// everything inside grouped into a deduped digest; severity thresholds respected per sink;
/// delivery uses the standard ChannelReply shape the gateway understands.
/// </summary>
[Category("Integration")]
[NotInParallel("AlertRouting")]
public class AlertRoutingTests
{
    [Test]
    public async Task Burst_collapses_to_one_grouped_digest_and_window_blocks_the_next_send()
    {
        var (sinkId, marker) = await CreateSinkAsync(AlertSeverity.Warning);
        try
        {
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var throttle = new AlertThrottle();
            var producer = new FakeAntiphonMessagingClient();
            var settings = Options.Create(new AlertsSettings { MinMinutesBetweenSends = 5 });

            // A burst: 3x the same crash + 1 launch failure, all routed into the throttle.
            await using (var db = CreateContext())
            {
                var router = new ChannelAlertRouter(
                    db, throttle, settings, NullLogger<ChannelAlertRouter>.Instance);
                foreach (var alert in new[]
                {
                    NewAlert(AlertSeverity.Warning, "supervisor", "Crash: agent supervision", "crash-1", "k1"),
                    NewAlert(AlertSeverity.Warning, "supervisor", "Crash: agent supervision", "crash-2", "k1"),
                    NewAlert(AlertSeverity.Warning, "supervisor", "Crash: agent supervision", "crash-3", "k1"),
                    NewAlert(AlertSeverity.Error, "launch", "Launch failed", "boom", "k2"),
                })
                {
                    db.Alerts.Add(alert);
                    await db.SaveChangesAsync();
                    await router.RouteAsync(alert.Id, CancellationToken.None);
                }
            }

            // First flush sends ONE message containing both groups (worst severity first, ×3 count).
            await using (var db = CreateContext())
            {
                var flusher = new AlertDigestFlusher(
                    db, throttle, producer, settings, clock, NullLogger<AlertDigestFlusher>.Instance);
                (await flusher.FlushDueAsync(CancellationToken.None)).ShouldBe(1);
            }

            producer.SentReplies.Count.ShouldBe(1);
            var text = producer.SentReplies[0].Text!;
            text.ShouldContain("Launch failed");
            text.ShouldContain("Crash: agent supervision ×3");
            text.IndexOf("Launch failed", StringComparison.Ordinal)
                .ShouldBeLessThan(text.IndexOf("Crash", StringComparison.Ordinal), "worst severity first");
            producer.SentReplies[0].ConversationId.ShouldBe(marker);

            // New alert inside the window: accumulates, does NOT send.
            await using (var db = CreateContext())
            {
                var router = new ChannelAlertRouter(
                    db, throttle, settings, NullLogger<ChannelAlertRouter>.Instance);
                var alert = NewAlert(AlertSeverity.Warning, "supervisor", "Crash: agent supervision", "crash-4", "k1");
                db.Alerts.Add(alert);
                await db.SaveChangesAsync();
                await router.RouteAsync(alert.Id, CancellationToken.None);

                var flusher = new AlertDigestFlusher(
                    db, throttle, producer, settings, clock, NullLogger<AlertDigestFlusher>.Instance);
                clock.Advance(TimeSpan.FromMinutes(2));
                (await flusher.FlushDueAsync(CancellationToken.None)).ShouldBe(0);

                // Window elapses -> second digest goes out.
                clock.Advance(TimeSpan.FromMinutes(4));
                (await flusher.FlushDueAsync(CancellationToken.None)).ShouldBe(1);
            }

            producer.SentReplies.Count.ShouldBe(2);
        }
        finally
        {
            await CleanupSinkAsync(sinkId);
        }
    }

    [Test]
    public async Task Severity_threshold_filters_per_sink_and_routed_alerts_are_marked()
    {
        var (sinkId, _) = await CreateSinkAsync(AlertSeverity.Error);
        try
        {
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var throttle = new AlertThrottle();
            var producer = new FakeAntiphonMessagingClient();
            var settings = Options.Create(new AlertsSettings { MinMinutesBetweenSends = 5 });

            Guid errorAlertId;
            await using (var db = CreateContext())
            {
                var router = new ChannelAlertRouter(
                    db, throttle, settings, NullLogger<ChannelAlertRouter>.Instance);

                var warning = NewAlert(AlertSeverity.Warning, "reconciler", "Below threshold", null, "w");
                var error = NewAlert(AlertSeverity.Error, "launch", "At threshold", null, "e");
                db.Alerts.AddRange(warning, error);
                await db.SaveChangesAsync();
                errorAlertId = error.Id;
                await router.RouteAsync(warning.Id, CancellationToken.None);
                await router.RouteAsync(error.Id, CancellationToken.None);

                var flusher = new AlertDigestFlusher(
                    db, throttle, producer, settings, clock, NullLogger<AlertDigestFlusher>.Instance);
                (await flusher.FlushDueAsync(CancellationToken.None)).ShouldBe(1);
            }

            producer.SentReplies.Count.ShouldBe(1);
            producer.SentReplies[0].Text!.ShouldContain("At threshold");
            producer.SentReplies[0].Text!.ShouldNotContain("Below threshold");

            await using (var verify = CreateContext())
            {
                var routed = await verify.Alerts.SingleAsync(a => a.Id == errorAlertId);
                routed.RoutedAt.ShouldNotBeNull();
                routed.SuppressedCount.ShouldBe(0);
            }
        }
        finally
        {
            await CleanupSinkAsync(sinkId);
        }
    }

    // ---------- helpers ----------

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Alert NewAlert(
        AlertSeverity severity, string source, string title, string? detail, string dedupKey) =>
        new()
        {
            Id = Guid.NewGuid(),
            Severity = severity,
            Source = source,
            Title = title,
            Detail = detail,
            DedupKey = dedupKey,
            CreatedAt = DateTime.UtcNow,
        };

    private static async Task<(Guid SinkId, string ExternalId)> CreateSinkAsync(AlertSeverity minSeverity)
    {
        var external = $"alert-sink-{Guid.NewGuid():N}";
        await using var db = CreateContext();
        var sink = new ChatChannel
        {
            Id = Guid.NewGuid(),
            Provider = "telegram",
            ExternalId = external,
            Kind = ChatChannelKind.Group,
            Title = "Antiphon Alerts (test)",
            Enabled = true,
            AlertMinSeverity = minSeverity,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.ChatChannels.Add(sink);
        await db.SaveChangesAsync();
        return (sink.Id, external);
    }

    private static async Task CleanupSinkAsync(Guid sinkId)
    {
        await using var db = CreateContext();
        await db.ChatChannels.Where(c => c.Id == sinkId).ExecuteDeleteAsync();
        await db.Alerts.Where(a => a.Source == "supervisor" && a.Title == "Crash: agent supervision"
            || a.Title == "Launch failed" || a.Title == "Below threshold" || a.Title == "At threshold")
            .ExecuteDeleteAsync();
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
