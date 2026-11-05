using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1591 S1. An aged queued channel withhold raises one Critical
/// <see cref="AgentIncidentKind.ChannelReplyLost"/> with reason ChannelInputWithheld.
/// The scan does not take the session lock and does not type.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
public sealed class ChannelInputWithheldIncidentTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string WithheldBody = "queued-channel-body-9f3a";
    private const string WithholdReason = "adopted session";

    [Test]
    public async Task C1591_AgedWithheldChannelRowRaisesOneIncident()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(h, acceptedAt: now.AddHours(-2), firstWithheldAt: now.AddMinutes(-10),
            withholdCount: 1);
        await Bridge(h).ScanWithheldChannelIncidentsAsync(Ct);

        await using var db = Db(schema.ConnectionString);
        var incidents = await db.AgentIncidents.AsNoTracking()
            .Where(i => i.AgentId == h.AgentId && i.FailureReason == "ChannelInputWithheld").ToListAsync();
        incidents.Count.ShouldBe(1);
        var incident = incidents[0];
        incident.Kind.ShouldBe(AgentIncidentKind.ChannelReplyLost);
        incident.Severity.ShouldBe(AlertSeverity.Critical);
        incident.Message.ShouldContain("telegram");
        incident.Message.ShouldContain(seed.ConversationId);
        incident.Message.ShouldContain(seed.NativeMessageId);
        incident.Message.ShouldContain(WithholdReason);
        incident.Message.ShouldNotContain(WithheldBody);
        (await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == seed.InboundId))
            .InputWithheldIncidentAt.ShouldNotBeNull();
        var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == seed.QueueId);
        queued.DeliveryAttempts.ShouldBe(0);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task C1591_RepeatScanStaysOneIncident()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        await SeedAsync(h, acceptedAt: now.AddHours(-2), firstWithheldAt: now.AddMinutes(-10), withholdCount: 1);
        var bridge = Bridge(h);
        await bridge.ScanWithheldChannelIncidentsAsync(Ct);
        await bridge.ScanWithheldChannelIncidentsAsync(Ct);

        await using var db = Db(schema.ConnectionString);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ChannelInputWithheld")).ShouldBe(1);
        (await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.AgentId == h.AgentId))
            .InputWithheldIncidentAt.ShouldNotBeNull();
    }

    [Test]
    public async Task C1591_YoungWithholdStaysQuiet()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(h, acceptedAt: now.AddHours(-2), firstWithheldAt: now.AddMinutes(-9),
            withholdCount: 1);
        await Bridge(h).ScanWithheldChannelIncidentsAsync(Ct);

        await using var db = Db(schema.ConnectionString);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ChannelInputWithheld")).ShouldBe(0);
        (await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == seed.InboundId))
            .InputWithheldIncidentAt.ShouldBeNull();
    }

    [Test]
    public async Task C1591_WorkingUnchargedRowStaysQuiet()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(h, acceptedAt: now.AddMinutes(-30), firstWithheldAt: now.AddMinutes(-30),
            withholdCount: 0);
        await Bridge(h).ScanWithheldChannelIncidentsAsync(Ct);

        await using var db = Db(schema.ConnectionString);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ChannelInputWithheld")).ShouldBe(0);
        (await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == seed.InboundId))
            .InputWithheldIncidentAt.ShouldBeNull();
    }

    [Test]
    public async Task C1591_ScanWritesNoInput()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(h, acceptedAt: now.AddHours(-2), firstWithheldAt: now.AddMinutes(-10),
            withholdCount: 1);
        var bridge = Bridge(h);
        await bridge.RunWithheldIncidentLoopAsync(new CancellationToken(canceled: true));
        var gate = h.Queue.GetLock(h.SessionId);
        await gate.WaitAsync(Ct);
        try
        {
            using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await bridge.ScanWithheldChannelIncidentsAsync(bound.Token);
        }
        finally
        {
            gate.Release();
        }

        h.Adapter.SentInput.ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
        await using var db = Db(schema.ConnectionString);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.Kind == AgentIncidentKind.ChannelReplyLost
            && i.FailureReason == "ChannelInputWithheld"
            && i.Severity == AlertSeverity.Critical)).ShouldBe(1);
        var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == seed.QueueId);
        queued.DeliveryAttempts.ShouldBe(0);
        queued.LastDeliveryStartedAt.ShouldBeNull();
        queued.Status.ShouldBe(QueuedMessageStatus.Pending);
        queued.Body.ShouldBe(WithheldBody);
        (await db.AgentIncidents.AsNoTracking().SingleAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ChannelInputWithheld")).Message.ShouldNotContain(WithheldBody);
    }

    private static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

    private static Task<BridgeQueueHarness> OpenAsync(string connectionString, TimeProvider clock) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connectionString,
            TimeProvider = clock,
            PreserveDatabaseOnDispose = true,
            Bridge = new ChannelBridgeSettings { Enabled = true, AgedHoldIncidentMinutes = 10 },
        });

    private static ChannelBridgeService Bridge(BridgeQueueHarness h) => new(
        h.Messaging, h.Queue, h.Provider.GetRequiredService<ChannelInboundDebouncer>(), h.EventBus,
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(), h.Clock,
        NullLogger<ChannelBridgeService>.Instance,
        h.Provider.GetRequiredService<ChannelInboundWakeSignal>());

    private static AppDbContext Db(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private sealed record SeedRow(Guid QueueId, Guid InboundId, string ConversationId, string NativeMessageId);

    private static async Task<SeedRow> SeedAsync(BridgeQueueHarness h, DateTime acceptedAt,
        DateTime? firstWithheldAt, int withholdCount)
    {
        var queueId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var native = Guid.NewGuid().ToString("N");
        var conversation = "family-" + Guid.NewGuid().ToString("N");
        await using var db = Db(h.ConnectionString);
        var sequence = (await db.SessionQueuedMessages.Where(m => m.AgentSessionId == h.SessionId)
            .MaxAsync(m => (long?)m.Sequence) ?? 0) + 1;
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = queueId,
            AgentSessionId = h.SessionId,
            Body = WithheldBody,
            Status = QueuedMessageStatus.Pending,
            Sequence = sequence,
            Origin = QueuedMessageOrigin.Channel,
            ConversationKey = "telegram:" + conversation,
            CreatedAt = acceptedAt,
            DeliveryAttempts = 0,
            LastDeliveryStartedAt = null,
            WithholdCount = withholdCount,
            FirstWithheldAt = firstWithheldAt,
            LastWithheldAt = firstWithheldAt,
            LastWithholdReason = withholdCount > 0 ? WithholdReason : null,
        });
        db.ChannelInbounds.Add(new ChannelInbound
        {
            Id = inboundId,
            Provider = "telegram",
            ConversationId = conversation,
            NativeMessageId = native,
            AgentId = h.AgentId,
            QueueMessageId = queueId,
            AcceptedAt = acceptedAt,
        });
        await db.SaveChangesAsync();
        return new SeedRow(queueId, inboundId, conversation, native);
    }
}
