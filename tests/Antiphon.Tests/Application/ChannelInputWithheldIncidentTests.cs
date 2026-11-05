using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    [Test]
    public async Task C1591_WorkingSessionKeepsHistoricalWithholdQuiet()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenProducerAsync(schema.ConnectionString, clock);
        await h.InsertStoreVisibleTurnAsync("earlier prompt", "earlier reply");
        await ProduceWithholdAsync(h, clock, "busy body");
        await h.MarkWorkingAsync();
        clock.Advance(TimeSpan.FromMinutes(10));
        await Bridge(h, clock).ScanWithheldChannelIncidentsAsync(Ct);

        await using var db = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(db, h.AgentId)).ShouldBe(0);
    }

    [Test]
    [Arguments("suspended")]
    [Arguments("continuity")]
    [Arguments("liveness")]
    [Arguments("compacting")]
    public async Task C1591_CurrentHoldKeepsHistoricalWithholdQuiet(string hold)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenProducerAsync(schema.ConnectionString, clock);
        await h.InsertStoreVisibleTurnAsync("earlier prompt", "earlier reply");
        await ProduceWithholdAsync(h, clock, "held body " + hold);
        await using (var db = Db(schema.ConnectionString))
        {
            var state = await db.AgentSupervisionStates.SingleOrDefaultAsync(s => s.AgentId == h.AgentId);
            if (state is null)
            {
                state = new AgentSupervisionState { AgentId = h.AgentId };
                db.AgentSupervisionStates.Add(state);
            }
            var at = clock.GetUtcNow().UtcDateTime;
            state.Suspended = hold == "suspended";
            state.ContinuityHeldAt = hold == "continuity" ? at : null;
            state.LivenessLatchedAt = hold == "liveness" ? at : null;
            state.ActiveCompactionRecoveryId = hold == "compacting" ? Guid.NewGuid() : null;
            state.UpdatedAt = at;
            await db.SaveChangesAsync();
        }
        clock.Advance(TimeSpan.FromMinutes(10));
        await Bridge(h, clock).ScanWithheldChannelIncidentsAsync(Ct);

        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(0);
    }

    [Test]
    [Arguments("continuity")]
    [Arguments("suspension")]
    [Arguments("liveness")]
    public async Task C1591_ExistingHoldStampSuppressesSecondIncident(string stamp)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenProducerAsync(schema.ConnectionString, clock);
        await h.InsertStoreVisibleTurnAsync("earlier prompt", "earlier reply");
        var seed = await ProduceWithholdAsync(h, clock, "stamped body " + stamp);
        var at = clock.GetUtcNow().UtcDateTime;
        await using (var db = Db(schema.ConnectionString))
        {
            await db.ChannelInbounds.Where(i => i.Id == seed.InboundId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ContinuityHoldIncidentAt, stamp == "continuity" ? at : null)
                .SetProperty(i => i.SuspensionHoldIncidentAt, stamp == "suspension" ? at : null)
                .SetProperty(i => i.LivenessHoldIncidentAt, stamp == "liveness" ? at : null));
        }
        clock.Advance(TimeSpan.FromMinutes(10));
        await Bridge(h, clock).ScanWithheldChannelIncidentsAsync(Ct);

        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(0);
        (await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == seed.InboundId))
            .InputWithheldIncidentAt.ShouldBeNull();
    }

    [Test]
    [Arguments("deliver")]
    [Arguments("cancel")]
    [Arguments("transfer")]
    [Timeout(60_000)]
    public async Task C1591_OpenDispositionDoesNotIncident(string disposition)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(h, acceptedAt: now.AddHours(-2), firstWithheldAt: now.AddMinutes(-10),
            withholdCount: 1);
        Guid? transferredTo = null;
        if (disposition == "transfer")
        {
            transferredTo = Guid.NewGuid();
            await using var setup = Db(schema.ConnectionString);
            setup.AgentSessions.Add(new AgentSession
            {
                Id = transferredTo.Value,
                AgentKind = AgentKind.ClaudeCode,
                DefinitionName = "fake",
                Status = SessionStatus.Running,
                Cwd = h.TempRoot,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            await setup.SaveChangesAsync();
        }

        await using var hold = Db(schema.ConnectionString);
        await using (var tx = await hold.Database.BeginTransactionAsync())
        {
            if (disposition == "deliver")
            {
                await hold.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "SessionQueuedMessages"
                    SET "Status" = {(int)QueuedMessageStatus.Sent}, "DeliveryAttempts" = 1, "LastDeliveryStartedAt" = {now}
                    WHERE "Id" = {seed.QueueId}
                    """);
            }
            else if (disposition == "cancel")
            {
                await hold.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "SessionQueuedMessages"
                    SET "Status" = {(int)QueuedMessageStatus.Canceled}, "CanceledAt" = {now}
                    WHERE "Id" = {seed.QueueId}
                    """);
            }
            else
            {
                await hold.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "SessionQueuedMessages"
                    SET "AgentSessionId" = {transferredTo}, "WithholdCount" = 0,
                        "FirstWithheldAt" = NULL, "LastWithheldAt" = NULL, "LastWithholdReason" = NULL
                    WHERE "Id" = {seed.QueueId}
                    """);
            }

            var started = Stopwatch.StartNew();
            await Bridge(h).ScanWithheldChannelIncidentsAsync(Ct);
            started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
            await using (var mid = Db(schema.ConnectionString))
                (await WithheldIncidentsAsync(mid, h.AgentId)).ShouldBe(0);
            await tx.CommitAsync();
        }

        await Bridge(h).ScanWithheldChannelIncidentsAsync(Ct);
        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(0);
        (await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == seed.InboundId))
            .InputWithheldIncidentAt.ShouldBeNull();
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1591_BacklogPassIsBounded()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var commands = new ScanCommands();
        await using var h = await OpenAsync(schema.ConnectionString, clock, o => o.AddInterceptors(commands));
        var now = clock.GetUtcNow().UtcDateTime;
        var oldest = new List<Guid>();
        await using (var db = Db(schema.ConnectionString))
        {
            var sequence = (await db.SessionQueuedMessages.Where(m => m.AgentSessionId == h.SessionId)
                .MaxAsync(m => (long?)m.Sequence) ?? 0) + 1;
            for (var i = 0; i < 340; i++)
            {
                var queueId = Guid.NewGuid();
                var inboundId = Guid.NewGuid();
                if (i < 64) oldest.Add(inboundId);
                var withheldAt = now.AddMinutes(-10).AddSeconds(-340 + i);
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = queueId,
                    AgentSessionId = h.SessionId,
                    Body = "backlog-" + i,
                    Status = QueuedMessageStatus.Pending,
                    Sequence = sequence++,
                    Origin = QueuedMessageOrigin.Channel,
                    CreatedAt = now.AddHours(-3),
                    DeliveryAttempts = 0,
                    WithholdCount = 1,
                    FirstWithheldAt = withheldAt,
                    LastWithheldAt = withheldAt,
                    LastWithholdReason = WithholdReason,
                });
                db.ChannelInbounds.Add(new ChannelInbound
                {
                    Id = inboundId,
                    Provider = "telegram",
                    ConversationId = "family-backlog",
                    NativeMessageId = "backlog-" + i.ToString("D4"),
                    AgentId = h.AgentId,
                    QueueMessageId = queueId,
                    AcceptedAt = now.AddHours(-3),
                    EnvelopeJson = "{\"marker\":\"envelope-body-should-not-be-read\"}",
                });
            }
            for (var i = 0; i < 40; i++)
            {
                var queueId = Guid.NewGuid();
                var withheldAt = now.AddHours(-5);
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = queueId,
                    AgentSessionId = h.SessionId,
                    Body = "historical-sent-" + i,
                    Status = QueuedMessageStatus.Sent,
                    Sequence = sequence++,
                    Origin = QueuedMessageOrigin.Channel,
                    CreatedAt = now.AddHours(-6),
                    SentAt = now.AddHours(-5),
                    DeliveryAttempts = 1,
                    LastDeliveryStartedAt = now.AddHours(-5),
                    WithholdCount = 1,
                    FirstWithheldAt = withheldAt,
                    LastWithheldAt = withheldAt,
                    LastWithholdReason = WithholdReason,
                });
                db.ChannelInbounds.Add(new ChannelInbound
                {
                    Id = Guid.NewGuid(),
                    Provider = "telegram",
                    ConversationId = "family-history",
                    NativeMessageId = "history-" + i.ToString("D4"),
                    AgentId = h.AgentId,
                    QueueMessageId = queueId,
                    AcceptedAt = now.AddHours(-6),
                });
            }
            await db.SaveChangesAsync();
        }

        commands.Armed = true;
        await Bridge(h).ScanWithheldChannelIncidentsAsync(Ct);
        commands.Armed = false;

        await using var verify = Db(schema.ConnectionString);
        var incidents = await verify.AgentIncidents.AsNoTracking()
            .Where(i => i.AgentId == h.AgentId && i.FailureReason == "ChannelInputWithheld")
            .Select(i => i.Message)
            .ToListAsync();
        incidents.Count.ShouldBe(64);
        incidents.ShouldAllBe(m => !m.Contains("envelope-body-should-not-be-read", StringComparison.Ordinal));
        var stamped = await verify.ChannelInbounds.AsNoTracking()
            .Where(i => i.InputWithheldIncidentAt != null)
            .Select(i => i.Id)
            .ToListAsync();
        stamped.Count.ShouldBe(64);
        stamped.OrderBy(id => id).ShouldBe(oldest.OrderBy(id => id), ignoreOrder: true);
        var index = await verify.Database.SqlQueryRaw<int>(
            """SELECT COUNT(*)::int AS "Value" FROM pg_indexes WHERE indexname = 'IX_SessionQueuedMessages_AgedChannelWithhold'""")
            .SingleAsync();
        (index == 1 && commands.Count <= 16).ShouldBeTrue(
            $"index={index} commands={commands.Count} sql={string.Join(" | ", commands.Texts.Take(8))}");
        commands.Texts.ShouldNotContain(sql => sql.Contains("EnvelopeJson", StringComparison.Ordinal));
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1591_ProducerPathPagesOneAgedWithhold()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenProducerAsync(schema.ConnectionString, clock);
        await h.InsertStoreVisibleTurnAsync("earlier prompt", "earlier reply");
        var chat = await h.BindChannelAsync();
        await using (var db = Db(schema.ConnectionString))
        {
            await db.ChatChannels.Where(c => c.ExternalId == chat)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DigestEnabled, true));
        }
        const string body = "family aged withhold 9f3a";
        var seed = await ProduceWithholdAsync(h, clock, body, chat);
        clock.Advance(TimeSpan.FromMinutes(10));
        await Bridge(h, clock).ScanWithheldChannelIncidentsAsync(Ct);

        await using var verify = Db(schema.ConnectionString);
        var incident = await verify.AgentIncidents.AsNoTracking().SingleAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ChannelInputWithheld");
        incident.Kind.ShouldBe(AgentIncidentKind.ChannelReplyLost);
        incident.Severity.ShouldBe(AlertSeverity.Critical);
        incident.Message.ShouldContain(seed.NativeMessageId);
        incident.Message.ShouldNotContain(body);
        var attention = new AttentionService(verify, h.Runner,
            h.Provider.GetRequiredService<IOptions<SupervisionSettings>>(),
            h.Provider.GetRequiredService<IOptions<DelegationSettings>>(),
            clock, NullLogger<AttentionService>.Instance);
        var rows = (await attention.GetAsync(Ct, includeProgressProbe: false)).Items
            .Where(i => i.Kind == AttentionKind.RecentCriticalIncident && i.AgentId == h.AgentId
                && i.Evidence.Contains("ChannelInputWithheld", StringComparison.Ordinal))
            .ToList();
        rows.Count.ShouldBe(1);
        rows[0].Evidence.ShouldContain(seed.NativeMessageId);

        var digest = new DigestSettings
        {
            WakeOnIncidentKinds = [AgentIncidentKind.ChannelReplyLost],
            TimeZone = "Europe/London",
            PublicBaseUrl = "https://antiphon.example",
        };
        await using var pageDb = Db(schema.ConnectionString);
        var notifier = new IncidentPageNotifier(pageDb,
            new ChatChannelService(pageDb, clock, h.Messaging), Options.Create(digest),
            clock, NullLogger<IncidentPageNotifier>.Instance);
        await notifier.SweepAsync(Ct);
        await notifier.SweepAsync(Ct);
        (await pageDb.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ChannelInputWithheld" && i.HumanNotifiedAt != null)).ShouldBe(1);
        h.Messaging.SentReplies.Count.ShouldBe(1);
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1591_DeliveredUserPromptStaysQuiet()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenProducerAsync(schema.ConnectionString, clock);
        await h.InsertStoreVisibleTurnAsync("earlier prompt", "earlier reply");
        h.Adapter.DialogAdopted = false;
        var floor = 0L;
        await using (var before = Db(schema.ConnectionString))
        {
            floor = await before.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId)
                .MaxAsync(t => (long?)t.Sequence) ?? 0;
        }
        var receipt = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Adapter.OnSubmitted = async submitted =>
        {
            await using var db = Db(schema.ConnectionString);
            var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId)
                .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
            var record = new SessionRunnerTranscriptEvent(h.SessionId, sequence, TranscriptKinds.UserPrompt,
                Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow, "user", submitted,
                null, null, null, null, null);
            h.Runner.SetTranscript(new SessionRunnerTranscriptDto(h.SessionId, [record], sequence));
            await h.Runtime.SyncTranscriptAsync(h.SessionId, Ct);
            receipt.TrySetResult(submitted);
        };
        var chat = await h.BindChannelAsync();
        const string body = "delivered family body 9f3a";
        await Bridge(h, clock).HandleInboundAsync(Message(chat, Guid.NewGuid().ToString("N"), body), Ct);
        var submitted = await receipt.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using (var db = Db(schema.ConnectionString))
        {
            var prompt = await db.TranscriptEntries.AsNoTracking().SingleAsync(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Sequence > floor && t.Text == submitted);
            prompt.Text.ShouldContain(body);
            prompt.Text.ShouldBe(submitted);
        }
        clock.Advance(TimeSpan.FromMinutes(10));
        await Bridge(h, clock).ScanWithheldChannelIncidentsAsync(Ct);
        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(0);
    }

    [Test]
    public async Task C1591_RestartAfterPruneDoesNotReraise()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(h, acceptedAt: now.AddHours(-2), firstWithheldAt: now.AddMinutes(-10),
            withholdCount: 1);
        await Bridge(h).ScanWithheldChannelIncidentsAsync(Ct);
        await using (var db = Db(schema.ConnectionString))
        {
            (await WithheldIncidentsAsync(db, h.AgentId)).ShouldBe(1);
            await db.AgentIncidents.Where(i => i.AgentId == h.AgentId).ExecuteDeleteAsync();
        }
        await Bridge(h).ScanWithheldChannelIncidentsAsync(Ct);
        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(0);
        (await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == seed.InboundId))
            .InputWithheldIncidentAt.ShouldNotBeNull();
    }

    [Test]
    [Timeout(60_000)]
    public async Task C1591_NonShutdownCancellationContinues()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var commands = new ScanCommands();
        await using var h = await OpenAsync(schema.ConnectionString, clock, o => o.AddInterceptors(commands));
        var now = clock.GetUtcNow().UtcDateTime;
        await SeedAsync(h, acceptedAt: now.AddHours(-2), firstWithheldAt: now.AddMinutes(-10), withholdCount: 1);
        var logger = new CaptureLogger();
        var bridge = Bridge(h, clock, logger);
        var throws = 0;
        commands.Fault = _ =>
        {
            if (throws++ > 0) return null;
            return new OperationCanceledException("withheld scan");
        };
        commands.Armed = true;
        using var cts = new CancellationTokenSource();
        var loop = bridge.RunWithheldIncidentLoopAsync(cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && !loop.IsCompleted)
        {
            await using var probe = Db(schema.ConnectionString);
            if (await WithheldIncidentsAsync(probe, h.AgentId) == 1)
                break;
            clock.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(50);
        }
        cts.Cancel();
        Exception? loopError = null;
        try
        {
            await loop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loopError = ex;
        }
        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(1);
        loopError.ShouldBeNull();
        logger.Lines.ShouldContain(line => line.Contains("retrying", StringComparison.OrdinalIgnoreCase));
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    private static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

    private static Task<BridgeQueueHarness> OpenAsync(string connectionString, TimeProvider clock,
        Action<DbContextOptionsBuilder>? configureDb = null) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connectionString,
            TimeProvider = clock,
            PreserveDatabaseOnDispose = true,
            Bridge = new ChannelBridgeSettings { Enabled = true, AgedHoldIncidentMinutes = 10 },
            ConfigureDbContext = configureDb,
        });

    private static Task<BridgeQueueHarness> OpenProducerAsync(string connectionString, TimeProvider clock) =>
        ClaudeDialogFixture.CreateBridgeAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = connectionString,
            TimeProvider = clock,
            PreserveDatabaseOnDispose = true,
            Bridge = new ChannelBridgeSettings
            {
                Enabled = true,
                AgedHoldIncidentMinutes = 10,
                DebounceWindowMs = 0,
                DebounceMaxMs = 2000,
                AgentStartTimeoutSeconds = 5,
                AgentReadyDelaySeconds = 0,
            },
        });

    private static ChannelBridgeService Bridge(BridgeQueueHarness h, TimeProvider? clock = null,
        ILogger<ChannelBridgeService>? logger = null) => new(
        h.Messaging, h.Queue, h.Provider.GetRequiredService<ChannelInboundDebouncer>(), h.EventBus,
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(), clock ?? h.Clock,
        logger ?? NullLogger<ChannelBridgeService>.Instance,
        h.Provider.GetRequiredService<ChannelInboundWakeSignal>());

    private static Task<int> WithheldIncidentsAsync(AppDbContext db, Guid agentId) =>
        db.AgentIncidents.CountAsync(i => i.AgentId == agentId && i.FailureReason == "ChannelInputWithheld");

    private static async Task<SeedRow> ProduceWithholdAsync(BridgeQueueHarness h, TimeProvider clock, string body,
        string? chat = null)
    {
        h.Adapter.DialogAdopted = true;
        chat ??= await h.BindChannelAsync();
        var native = Guid.NewGuid().ToString("N");
        await Bridge(h, clock).HandleInboundAsync(Message(chat, native, body), Ct);
        await using var db = Db(h.ConnectionString);
        var inbound = await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == native);
        var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == inbound.QueueMessageId);
        queued.WithholdCount.ShouldBeGreaterThan(0);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending);
        queued.Origin.ShouldBe(QueuedMessageOrigin.Channel);
        queued.DeliveryAttempts.ShouldBe(0);
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        h.Adapter.SentInput.ShouldBeEmpty();
        return new SeedRow(queued.Id, inbound.Id, inbound.ConversationId, native);
    }

    private static ChannelMessage Message(string chat, string native, string body) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Channel = "telegram",
        ChannelMessageId = native,
        Conversation = new Conversation { Id = chat, Kind = ConversationKind.Group, Title = "Family" },
        Author = new Participant { Id = "alice", DisplayName = "alice" },
        Timestamp = DateTimeOffset.UtcNow,
        Text = body,
        ReplyHandle = chat,
        Raw = JsonDocument.Parse("{\"from\":\"native\"}").RootElement.Clone(),
    };

    private sealed class CaptureLogger : ILogger<ChannelBridgeService>
    {
        public List<string> Lines { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class ScanCommands : DbCommandInterceptor
    {
        public bool Armed;
        public int Count;
        public List<string> Texts { get; } = [];
        public Func<string, Exception?>? Fault;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Note(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Note(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Note(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Note(DbCommand command)
        {
            if (!Armed) return;
            Count++;
            Texts.Add(command.CommandText);
            var error = Fault?.Invoke(command.CommandText);
            if (error is not null)
                throw error;
        }
    }

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
