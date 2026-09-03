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
/// CARD-0338 S3: one pager ping per Critical ChannelReplyLost on an AlwaysOn channel-bound agent.
/// Isolated schema because SweepAsync walks every DigestEnabled channel.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class IncidentPageNotifierTests
{
    [Test]
    public async Task A_critical_lost_reply_on_an_always_on_channel_bound_agent_is_pinged_once()
    {
        await using var h = await Harness.CreateAsync();
        var digest = await h.SeedDigestChannelAsync();
        var agent = await h.SeedAlwaysOnBoundAgentAsync("PM-Orchestrator-Grok");
        var incident = await h.SeedIncidentAsync(agent.Id, AgentIncidentKind.ChannelReplyLost,
            AlertSeverity.Critical, "TurnIncomplete",
            "A reply this agent owed slack:D0B1VUH2EAK was never sent: 1 message(s) went unanswered because a matching prompt was recorded but no turn completed within 30 minutes.");

        await h.Notifier.SweepAsync(CancellationToken.None);
        await h.Notifier.SweepAsync(CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1);
        var ping = h.Messaging.SentReplies[0];
        ping.ConversationId.ShouldBe(digest.ExternalId);
        ping.Text.ShouldNotBeNull();
        ping.Text.ShouldContain("🔕 PM-Orchestrator-Grok");
        ping.Text.ShouldContain("slack \"D0B1VUH2EAK\"");
        ping.Text.ShouldContain("no turn completed within 30 minutes");
        (await h.IncidentAsync(incident)).HumanNotifiedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task A_non_always_on_agent_is_not_pinged()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedDigestChannelAsync();
        var agent = await h.SeedAlwaysOnBoundAgentAsync("Worker", alwaysOn: false);
        var incident = await h.SeedIncidentAsync(agent.Id, AgentIncidentKind.ChannelReplyLost,
            AlertSeverity.Critical, "TurnIncomplete", "A reply this agent owed telegram:1 was never sent.");

        await h.Notifier.SweepAsync(CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        (await h.IncidentAsync(incident)).HumanNotifiedAt.ShouldBeNull();
    }

    [Test]
    public async Task ProviderCapacity_is_not_pinged()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedDigestChannelAsync();
        var agent = await h.SeedAlwaysOnBoundAgentAsync("Grok");
        var incident = await h.SeedIncidentAsync(agent.Id, AgentIncidentKind.ChannelReplyLost,
            AlertSeverity.Critical, IncidentPageNotifier.ProviderCapacityReason,
            "A reply this agent owed slack:D1 was never sent because the provider refused the request.");

        await h.Notifier.SweepAsync(CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        (await h.IncidentAsync(incident)).HumanNotifiedAt.ShouldBeNull();
    }

    [Test]
    public async Task A_warning_row_is_not_pinged()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedDigestChannelAsync();
        var agent = await h.SeedAlwaysOnBoundAgentAsync("Grok");
        var incident = await h.SeedIncidentAsync(agent.Id, AgentIncidentKind.ChannelReplyLost,
            AlertSeverity.Warning, "TurnIncomplete", "A reply this agent owed slack:D1 was never sent.");

        await h.Notifier.SweepAsync(CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        (await h.IncidentAsync(incident)).HumanNotifiedAt.ShouldBeNull();
    }

    [Test]
    public async Task A_row_older_than_24_hours_is_not_pinged()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedDigestChannelAsync();
        var agent = await h.SeedAlwaysOnBoundAgentAsync("Grok");
        var incident = await h.SeedIncidentAsync(agent.Id, AgentIncidentKind.ChannelReplyLost,
            AlertSeverity.Critical, "TurnIncomplete", "A reply this agent owed slack:D1 was never sent.",
            createdAt: h.Clock.GetUtcNow().UtcDateTime.AddHours(-25));

        await h.Notifier.SweepAsync(CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        (await h.IncidentAsync(incident)).HumanNotifiedAt.ShouldBeNull();
    }

    [Test]
    public async Task A_throwing_send_leaves_the_stamp_null_so_the_next_sweep_retries()
    {
        var producer = new ThrowingProducer();
        await using var h = await Harness.CreateAsync(producer);
        await h.SeedDigestChannelAsync();
        var agent = await h.SeedAlwaysOnBoundAgentAsync("Grok");
        var incident = await h.SeedIncidentAsync(agent.Id, AgentIncidentKind.ChannelReplyLost,
            AlertSeverity.Critical, "TurnIncomplete", "A reply this agent owed slack:D1 was never sent.");

        await h.Notifier.SweepAsync(CancellationToken.None);

        producer.Attempts.ShouldBe(1);
        (await h.IncidentAsync(incident)).HumanNotifiedAt.ShouldBeNull();
        h.Messaging.SentReplies.ShouldBeEmpty();

        h.ReplaceProducer(new FakeAntiphonMessagingClient());
        await h.Notifier.SweepAsync(CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1);
        (await h.IncidentAsync(incident)).HumanNotifiedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task No_digest_enabled_channel_sends_nothing_and_stamps_nothing()
    {
        await using var h = await Harness.CreateAsync();
        var agent = await h.SeedAlwaysOnBoundAgentAsync("Grok");
        var incident = await h.SeedIncidentAsync(agent.Id, AgentIncidentKind.ChannelReplyLost,
            AlertSeverity.Critical, "TurnIncomplete", "A reply this agent owed slack:D1 was never sent.");

        await h.Notifier.SweepAsync(CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        (await h.IncidentAsync(incident)).HumanNotifiedAt.ShouldBeNull();
    }

    [Test]
    public async Task An_unbound_always_on_agent_is_not_pinged()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedDigestChannelAsync();
        var agent = await h.SeedAlwaysOnBoundAgentAsync("Grok", bindChannel: false);
        var incident = await h.SeedIncidentAsync(agent.Id, AgentIncidentKind.ChannelReplyLost,
            AlertSeverity.Critical, "Unroutable", "A reply this agent owed slack:D1 was never sent.");

        await h.Notifier.SweepAsync(CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        (await h.IncidentAsync(incident)).HumanNotifiedAt.ShouldBeNull();
    }

    [Test]
    public async Task Unroutable_is_paged()
    {
        await using var h = await Harness.CreateAsync();
        await h.SeedDigestChannelAsync();
        var agent = await h.SeedAlwaysOnBoundAgentAsync("Grok");
        var incident = await h.SeedIncidentAsync(agent.Id, AgentIncidentKind.ChannelReplyLost,
            AlertSeverity.Critical, "Unroutable",
            "A reply this agent owed slack:D1 was never sent: 1 message(s) went unanswered because the stored conversation key names no routable target.");

        await h.Notifier.SweepAsync(CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1);
        h.Messaging.SentReplies[0].Text.ShouldNotBeNull();
        h.Messaging.SentReplies[0].Text.ShouldContain("the conversation could not be routed");
        (await h.IncidentAsync(incident)).HumanNotifiedAt.ShouldNotBeNull();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly DigestSettings _settings;
        private ChatChannelService _channels;

        private Harness(
            IsolatedTestSchema schema,
            AppDbContext db,
            FakeTimeProvider clock,
            IAntiphonMessagingProducer producer,
            FakeAntiphonMessagingClient messaging,
            DigestSettings settings)
        {
            _schema = schema;
            _settings = settings;
            Db = db;
            Clock = clock;
            Messaging = messaging;
            _channels = new ChatChannelService(db, clock, producer);
            Notifier = BuildNotifier();
        }

        public static async Task<Harness> CreateAsync(IAntiphonMessagingProducer? producer = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 3, 15, 16, 0, TimeSpan.Zero));
            var messaging = producer as FakeAntiphonMessagingClient ?? new FakeAntiphonMessagingClient();
            var settings = new DigestSettings
            {
                WakeOnIncidentKinds = [AgentIncidentKind.ChannelReplyLost],
                TimeZone = "Europe/London",
                PublicBaseUrl = "https://antiphon.example",
            };
            return new Harness(schema, db, clock, producer ?? messaging, messaging, settings);
        }

        public AppDbContext Db { get; }
        public FakeTimeProvider Clock { get; }
        public FakeAntiphonMessagingClient Messaging { get; private set; }
        public IncidentPageNotifier Notifier { get; private set; }

        public void ReplaceProducer(FakeAntiphonMessagingClient producer)
        {
            Messaging = producer;
            _channels = new ChatChannelService(Db, Clock, producer);
            Notifier = BuildNotifier();
        }

        public async Task<ChatChannel> SeedDigestChannelAsync()
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            var channel = new ChatChannel
            {
                Id = Guid.NewGuid(),
                Provider = "telegram",
                ExternalId = $"-digest-{Guid.NewGuid():N}",
                Kind = ChatChannelKind.Direct,
                Title = "Ops",
                Enabled = true,
                DigestEnabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            };
            Db.ChatChannels.Add(channel);
            await Db.SaveChangesAsync();
            return channel;
        }

        public async Task<Agent> SeedAlwaysOnBoundAgentAsync(
            string name, bool alwaysOn = true, bool bindChannel = true)
        {
            var now = Clock.GetUtcNow().UtcDateTime;
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Name = name,
                Slug = $"agent-{Guid.NewGuid():N}"[..24],
                WorkingDirectory = Path.GetTempPath(),
                Details = "channel-bound orchestrator",
                Status = AgentStatus.Running,
                ModelLevel = AgentModelLevel.High,
                AlwaysOn = alwaysOn,
                CreatedAt = now.AddHours(-1),
                UpdatedAt = now.AddHours(-1),
            };
            Db.Agents.Add(agent);
            if (bindChannel)
            {
                Db.ChatChannels.Add(new ChatChannel
                {
                    Id = Guid.NewGuid(),
                    Provider = "slack",
                    ExternalId = $"D-{agent.Id:N}"[..16],
                    Kind = ChatChannelKind.Direct,
                    Title = name,
                    AgentId = agent.Id,
                    Enabled = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            await Db.SaveChangesAsync();
            return agent;
        }

        public async Task<Guid> SeedIncidentAsync(
            Guid agentId,
            AgentIncidentKind kind,
            AlertSeverity severity,
            string? failureReason,
            string message,
            DateTime? createdAt = null)
        {
            var id = Guid.NewGuid();
            Db.AgentIncidents.Add(new AgentIncident
            {
                Id = id,
                AgentId = agentId,
                Kind = kind,
                Severity = severity,
                FailureReason = failureReason,
                Message = message,
                CreatedAt = createdAt ?? Clock.GetUtcNow().UtcDateTime,
            });
            await Db.SaveChangesAsync();
            return id;
        }

        public async Task<AgentIncident> IncidentAsync(Guid id)
        {
            await using var db = NewDb();
            return await db.AgentIncidents.AsNoTracking().SingleAsync(i => i.Id == id);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _schema.DisposeAsync();
        }

        private IncidentPageNotifier BuildNotifier() =>
            new(Db, _channels, Options.Create(_settings), Clock, NullLogger<IncidentPageNotifier>.Instance);

        private AppDbContext NewDb() =>
            new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
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
