using Antiphon.Messaging;
using Antiphon.Messaging.Client;
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
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0036 S2: due-time send, modelled on <see cref="TrackerSyncNotifierTests"/>. Shared-Postgres
/// rules: every assertion is scoped to rows this test created. SendDueAsync is called with an
/// explicit channel id so a sibling suite's digest-enabled channel is never stamped.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class AwayDigestNotifierTests
{
    [Test]
    public void Defaults_are_the_two_confirmed_london_send_times()
    {
        var settings = new DigestSettings();
        settings.TimeZone.ShouldBe("Europe/London");
        settings.SendTimesLocal.ShouldBe(["08:00", "18:00"]);
    }

    [Test]
    public void Invalid_timezone_fails_startup_validation()
    {
        new DigestSettingsValidator().Validate(null, new DigestSettings { TimeZone = "not/a-timezone" })
            .Succeeded.ShouldBeFalse();
    }

    [Test]
    public async Task Sends_to_a_digest_enabled_channel_and_skips_one_that_is_not()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var enabled = await h.SeedChannelAsync("Family", digestEnabled: true);
            var optedOut = await h.SeedChannelAsync("Ops", digestEnabled: false);
            await h.SeedFinishedTaskAsync("shipped the digest");

            var sent = await h.Notifier.SendDueAsync(enabled.Id, sinceOverride: null, force: true, CancellationToken.None);
            var skipped = await h.Notifier.SendDueAsync(optedOut.Id, sinceOverride: null, force: true, CancellationToken.None);

            var ok = sent.ShouldHaveSingleItem();
            ok.Sent.ShouldBeTrue();
            ok.ChannelId.ShouldBe(enabled.Id);
            ok.Reason.ShouldBeNull();

            var miss = skipped.ShouldHaveSingleItem();
            miss.Sent.ShouldBeFalse();
            miss.Reason.ShouldBe("no_digest_channel");

            var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
            reply.ConversationId.ShouldBe(enabled.ExternalId);
            reply.Text.ShouldContain("shipped the digest");
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_disabled_channel_reports_channel_disabled_and_does_not_send()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var channel = await h.SeedChannelAsync("Family", digestEnabled: true, enabled: false);
            await h.SeedFinishedTaskAsync("would have been in the digest");

            var results = await h.Notifier.SendDueAsync(channel.Id, null, force: true, CancellationToken.None);

            var result = results.ShouldHaveSingleItem();
            result.Sent.ShouldBeFalse();
            result.Reason.ShouldBe("channel_disabled");
            result.ChannelId.ShouldBe(channel.Id);
            h.Messaging.SentReplies.ShouldBeEmpty();
            (await h.ReloadAsync(channel.Id)).DigestLastSentAt.ShouldBeNull();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_throwing_producer_does_not_stamp_digest_last_sent_at()
    {
        var producer = new ThrowingProducer();
        await using var h = await Harness.CreateAsync(producer);
        try
        {
            var channel = await h.SeedChannelAsync("Family", digestEnabled: true);
            await h.SeedFinishedTaskAsync("will not leave the box");

            var results = await h.Notifier.SendDueAsync(channel.Id, null, force: true, CancellationToken.None);

            var result = results.ShouldHaveSingleItem();
            result.Sent.ShouldBeFalse();
            result.Reason.ShouldBe("send_failed");
            producer.Attempts.ShouldBe(1);
            (await h.ReloadAsync(channel.Id)).DigestLastSentAt.ShouldBeNull();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task Due_time_after_spring_forward_uses_local_eight_not_utc_eight()
    {
        // 2026-03-29 Europe/London springs forward 01:00 GMT → 02:00 BST. 08:00 local is 07:00 UTC.
        // A clock that compared against 08:00 UTC would still say not-due at 07:05 UTC.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 29, 7, 5, 0, TimeSpan.Zero));
        await using var h = await Harness.CreateAsync(clock: clock, sendTimes: ["08:00"]);
        try
        {
            var channel = await h.SeedChannelAsync("Family", digestEnabled: true,
                lastSentAt: new DateTime(2026, 3, 28, 18, 0, 0, DateTimeKind.Utc));

            var results = await h.Notifier.SendDueAsync(channel.Id, null, force: false, CancellationToken.None);

            var result = results.ShouldHaveSingleItem();
            result.Reason.ShouldBeNull("07:05 UTC is 08:05 BST, so today's 08:00 local has passed");
            (await h.ReloadAsync(channel.Id)).DigestLastSentAt.ShouldBe(clock.GetUtcNow().UtcDateTime);
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task The_same_bst_morning_is_not_due_again_after_the_local_send()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 29, 7, 5, 0, TimeSpan.Zero));
        await using var h = await Harness.CreateAsync(clock: clock, sendTimes: ["08:00"]);
        try
        {
            var channel = await h.SeedChannelAsync("Family", digestEnabled: true,
                lastSentAt: new DateTime(2026, 3, 29, 7, 0, 0, DateTimeKind.Utc));

            var results = await h.Notifier.SendDueAsync(channel.Id, null, force: false, CancellationToken.None);

            results.ShouldHaveSingleItem().Reason.ShouldBe("not_due");
            (await h.ReloadAsync(channel.Id)).DigestLastSentAt
                .ShouldBe(new DateTime(2026, 3, 29, 7, 0, 0, DateTimeKind.Utc));
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task A_silent_send_carries_disable_notification_and_stamps()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var channel = await h.SeedChannelAsync("Family", digestEnabled: true);
            await h.SeedFinishedTaskAsync("the body");

            await h.Notifier.SendDueAsync(channel.Id, null, force: true, CancellationToken.None);

            var sent = h.Messaging.SentReplies.ShouldHaveSingleItem();
            sent.RawOverrides.ShouldNotBeNull();
            sent.RawOverrides!.Value.GetProperty("disable_notification").GetBoolean().ShouldBeTrue();
            (await h.ReloadAsync(channel.Id)).DigestLastSentAt.ShouldNotBeNull();
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task An_empty_idle_window_sends_nothing_but_still_stamps()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var channel = await h.SeedChannelAsync("Family", digestEnabled: true);

            var results = await h.Notifier.SendDueAsync(channel.Id, null, force: true, CancellationToken.None);

            var result = results.ShouldHaveSingleItem();
            result.Reason.ShouldBeNull();
            (await h.ReloadAsync(channel.Id)).DigestLastSentAt.ShouldNotBeNull(
                "an idle window is still consumed so the next one starts here");
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    [Test]
    public async Task First_window_heading_says_last_24h_when_the_channel_has_never_been_sent()
    {
        await using var h = await Harness.CreateAsync();
        try
        {
            var channel = await h.SeedChannelAsync("Family", digestEnabled: true);
            await h.SeedFinishedTaskAsync("first window body");

            await h.Notifier.SendDueAsync(channel.Id, null, force: true, CancellationToken.None);

            h.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldContain("last 24h");
        }
        finally
        {
            await h.CleanupAsync();
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly List<Guid> _channelIds = [];
        private readonly List<Guid> _taskIds = [];
        private readonly FakeTimeProvider _clock;

        private Harness(IAntiphonMessagingProducer? producer, FakeTimeProvider clock, DigestSettings settings)
        {
            _clock = clock;
            Db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            Messaging = producer as FakeAntiphonMessagingClient ?? new FakeAntiphonMessagingClient();
            var options = Options.Create(settings);
            var attention = new AttentionService(
                Db, new RefusingSessionRunnerClient(), Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()), clock, NullLogger<AttentionService>.Instance);
            var projection = new AwayDigestProjection(
                Db, attention, new SubscriptionUsageReader(Db, clock), Options.Create(new DelegationSettings()));
            var channels = new ChatChannelService(Db, clock, producer ?? Messaging);
            Notifier = new AwayDigestNotifier(
                Db, projection, channels, options, clock, NullLogger<AwayDigestNotifier>.Instance);
        }

        public static Task<Harness> CreateAsync(
            IAntiphonMessagingProducer? producer = null,
            FakeTimeProvider? clock = null,
            string[]? sendTimes = null)
        {
            clock ??= new FakeTimeProvider(new DateTimeOffset(2026, 8, 26, 17, 5, 0, TimeSpan.Zero));
            var settings = new DigestSettings
            {
                TimeZone = "Europe/London",
                SendTimesLocal = sendTimes is null ? ["08:00", "18:00"] : [.. sendTimes],
            };
            return Task.FromResult(new Harness(producer, clock, settings));
        }

        public AppDbContext Db { get; }
        public FakeAntiphonMessagingClient Messaging { get; }
        public AwayDigestNotifier Notifier { get; }

        public async Task<ChatChannel> SeedChannelAsync(
            string title, bool digestEnabled, bool enabled = true, DateTime? lastSentAt = null)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var channel = new ChatChannel
            {
                Id = Guid.NewGuid(),
                Provider = "telegram",
                ExternalId = $"-digest-{Guid.NewGuid():N}",
                Kind = ChatChannelKind.Direct,
                Title = title,
                Enabled = enabled,
                DigestEnabled = digestEnabled,
                DigestLastSentAt = lastSentAt,
                CreatedAt = now,
                UpdatedAt = now,
            };
            Db.ChatChannels.Add(channel);
            await Db.SaveChangesAsync();
            _channelIds.Add(channel.Id);
            return channel;
        }

        public async Task SeedFinishedTaskAsync(string title)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var id = Guid.NewGuid();
            Db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = title,
                Goal = "notifier window",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None,
                Result = "Done.",
                CreatedAt = now.AddHours(-2),
                DispatchedAt = now.AddHours(-2),
                CompletedAt = now.AddMinutes(-10),
            });
            await Db.SaveChangesAsync();
            _taskIds.Add(id);
        }

        public async Task<ChatChannel> ReloadAsync(Guid id)
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            return await db.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == id);
        }

        public async Task CleanupAsync()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            if (_taskIds.Count > 0)
                await db.AgentTasks.Where(t => _taskIds.Contains(t.Id)).ExecuteDeleteAsync();
            if (_channelIds.Count > 0)
                await db.ChatChannels.Where(c => _channelIds.Contains(c.Id)).ExecuteDeleteAsync();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class ThrowingProducer : IAntiphonMessagingProducer
    {
        public int Attempts { get; private set; }

        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("broker unavailable");
        }
    }
}
