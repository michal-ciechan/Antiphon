using Antiphon.Messaging;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
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
/// CARD-0245 S2b — persist a gateway inbound-unconsumed event before a catalog row exists,
/// raise one Critical attention item, and ignore a replay.
/// </summary>
[Category("Integration")]
public sealed class ChannelIngressIncidentTests
{
    [Test]
    public async Task Event_before_catalog_is_one_deduped_critical_attention_item()
    {
        await using var scenario = new Scenario();
        var evt = scenario.Event();

        (await scenario.PersistAsync(evt)).ShouldBeTrue();
        (await scenario.PersistAsync(evt)).ShouldBeFalse();

        var rows = await scenario.IncidentsAsync();
        rows.ShouldHaveSingleItem();
        rows[0].OriginalMessageId.ShouldBe(evt.OriginalMessageId);
        rows[0].Acknowledged.ShouldBeTrue();

        var item = (await scenario.AttentionAsync()).Single(i => i.Kind == AttentionKind.InboundUnconsumed
            && i.Evidence.Contains(evt.OriginalMessageId, StringComparison.Ordinal));
        item.Severity.ShouldBe(AlertSeverity.Critical);
        item.AgentId.ShouldBeNull();
        scenario.Alerts.Count.ShouldBe(1);
        scenario.Alerts[0].Severity.ShouldBe(AlertSeverity.Critical);
    }

    [Test]
    public async Task Replay_after_channel_catalog_still_one_row_and_joins_the_agent()
    {
        await using var scenario = new Scenario();
        var evt = scenario.Event();
        (await scenario.PersistAsync(evt)).ShouldBeTrue();
        var agentId = await scenario.AddBoundChannelAsync(evt.Provider, evt.ConversationId);

        (await scenario.PersistAsync(evt)).ShouldBeFalse();
        (await scenario.IncidentsAsync()).Count.ShouldBe(1);

        var item = (await scenario.AttentionAsync()).Single(i => i.Kind == AttentionKind.InboundUnconsumed
            && i.Evidence.Contains(evt.OriginalMessageId, StringComparison.Ordinal));
        item.AgentId.ShouldBe(agentId);
        item.Actions.ShouldBe([AttentionAction.OpenAgent]);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly List<Guid> _incidents = [];
        private readonly List<Guid> _agents = [];
        private readonly List<Guid> _channels = [];
        public List<AlertRaise> Alerts { get; } = [];

        public InboundUnconsumedEvent Event() => new()
        {
            Provider = "slack",
            ConversationId = $"C-{Guid.NewGuid():N}"[..12],
            OriginalMessageId = $"msg-{Guid.NewGuid():N}"[..16],
            FirstSeenAt = DateTimeOffset.UtcNow.AddMinutes(-12),
            Topic = "channels.inbound",
            Partition = 0,
            Offset = Random.Shared.NextInt64(1_000_000, 9_000_000),
            DetectedAt = DateTimeOffset.UtcNow,
            Acknowledged = true,
            AppHostHealth = "fail: timeout",
        };

        public async Task<bool> PersistAsync(InboundUnconsumedEvent evt)
        {
            await using var db = CreateContext();
            var sut = new ChannelIngressIncidentService(
                db,
                new RecordingAlerts(Alerts),
                TimeProvider.System,
                NullLogger<ChannelIngressIncidentService>.Instance);
            var created = await sut.PersistAsync(evt, CancellationToken.None);
            if (created)
            {
                var id = await db.ChannelIngressIncidents.AsNoTracking()
                    .Where(i => i.Topic == evt.Topic && i.Partition == evt.Partition && i.Offset == evt.Offset)
                    .Select(i => i.Id)
                    .SingleAsync();
                _incidents.Add(id);
            }
            return created;
        }

        public async Task<List<ChannelIngressIncident>> IncidentsAsync()
        {
            await using var db = CreateContext();
            return await db.ChannelIngressIncidents.AsNoTracking()
                .Where(i => _incidents.Contains(i.Id))
                .ToListAsync();
        }

        public async Task<Guid> AddBoundChannelAsync(string provider, string conversationId)
        {
            var agentId = Guid.NewGuid();
            var name = $"ing-{agentId:N}"[..16];
            var channelId = Guid.NewGuid();
            await using var db = CreateContext();
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = name,
                Slug = name,
                WorkingDirectory = Path.GetTempPath(),
                Details = "CARD-0245 S2b",
                Status = AgentStatus.Idle,
                ModelLevel = AgentModelLevel.Medium,
                AlwaysOn = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            db.ChatChannels.Add(new ChatChannel
            {
                Id = channelId,
                Provider = provider,
                ExternalId = conversationId,
                Kind = ChatChannelKind.Group,
                AgentId = agentId,
                Enabled = true,
                Title = "family",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            _agents.Add(agentId);
            _channels.Add(channelId);
            return agentId;
        }

        public async Task<List<AttentionItemDto>> AttentionAsync()
        {
            await using var db = CreateContext();
            var sut = new AttentionService(
                db,
                new RefusingSessionRunnerClient(),
                Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()),
                TimeProvider.System,
                NullLogger<AttentionService>.Instance);
            return (await sut.GetAsync(CancellationToken.None)).Items.ToList();
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.ChannelIngressIncidents.Where(i => _incidents.Contains(i.Id)).ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => _channels.Contains(c.Id)).ExecuteDeleteAsync();
            await db.Agents.Where(a => _agents.Contains(a.Id)).ExecuteDeleteAsync();
        }

        private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
    }

    private sealed class RecordingAlerts(List<AlertRaise> sink) : IAlertService
    {
        public Task RaiseAsync(AlertRaise alert, CancellationToken ct)
        {
            sink.Add(alert);
            return Task.CompletedTask;
        }
    }
}
