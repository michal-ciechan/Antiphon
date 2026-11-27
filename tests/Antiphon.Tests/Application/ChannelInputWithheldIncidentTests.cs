using System.Data.Common;
using Npgsql;
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
public sealed partial class ChannelInputWithheldIncidentTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string WithheldBody = "queued-channel-body-9f3a";
    private const string WithholdReason = "adopted session";

    [Test]
    [Timeout(120_000)]
    public async Task AdoptedChannelPhotoRetriesOnceAfterVerifiedHistory(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await OpenProducerAsync(schema.ConnectionString, TimeProvider.System);
        await using var db = Db(schema.ConnectionString);
        await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.AlwaysOn, false), ct);
        await h.InsertStoreVisibleTurnAsync("prior prompt", "prior answer");
        var chat = await h.BindChannelAsync();
        h.Adapter.DialogAdopted = true;
        h.Adapter.OnSubmitted = body => h.InsertStoreVisibleTurnAsync(body, "fixture answer");
        using var bridge = Bridge(h);
        var inbound = Message(chat, "fixture-1904", "fixture caption") with
        {
            Attachments = [new Attachment { Kind = AttachmentKind.Image, ChannelRef = "test-photo",
                Name = "fixture.jpg", Content = [0xff, 0xd8, 0xff, 0xd9] }],
        };
        await bridge.HandleInboundAsync(inbound, ct);
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == h.SessionId, ct);
        row.LastWithholdReason.ShouldBe(DialogDeliveryAdmission.AdoptedSession);
        row.DeliveryAttempts.ShouldBe(0);
        h.Adapter.SentInput.ShouldBeEmpty();
        h.Adapter.AdoptedHistoryVerified = true;
        await bridge.DrainPendingAsync(ct);
        h.Adapter.SentInput.ShouldBeEmpty("the first retry waits for backoff");
        await db.SessionQueuedMessages.Where(m => m.Id == row.Id).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.FirstWithheldAt, h.Now.AddMinutes(-20))
            .SetProperty(m => m.LastWithheldAt, h.Now.AddMinutes(-20)), ct);
        await bridge.DrainPendingAsync(ct);
        await bridge.DrainPendingAsync(ct);
        var submitted = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        h.Adapter.Inputs.Count(input => input.Contains("fixture caption", StringComparison.Ordinal)).ShouldBe(1);
        PromptSubmissionMatch.IsCompleteIn(row.Body, submitted).ShouldBeTrue();
        submitted.ShouldContain("fixture caption");
        submitted.ShouldContain("[photo attached: ");
        (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == submitted, ct)).ShouldBe(1);
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, ct);
        await h.DrainOutboundAsync();
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, ct);
        await h.DrainOutboundAsync();
        var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
        reply.ConversationId.ShouldBe(chat);
        reply.Kind.ShouldBe(ChannelReplyKind.Answer);
        reply.Text.ShouldBe("fixture answer");
        reply.AnsweredMessageIds.ShouldBe([inbound.ChannelMessageId]);
        (await db.SessionQueuedMessages.SingleAsync(m => m.Id == row.Id, ct)).DeliveryAttempts.ShouldBe(1);
    }

    [Test]
    [Timeout(120_000)]
    public async Task UnansweredGroupInboundPushesAttentionAtTenMinutes(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var seed = await SeedAsync(h, h.Now, null, 0);
        await h.BindChannelAsync(seed.ConversationId);
        await using var db = Db(schema.ConnectionString);
        var chat = await db.ChatChannels.SingleAsync(c => c.ExternalId == seed.ConversationId, ct);
        chat.Kind = ChatChannelKind.Group;
        chat.DigestEnabled = true;
        chat.LastReplyAt = h.Now.AddMinutes(5); // unrelated startup output cannot pay this inbound
        var inbound = await db.ChannelInbounds.SingleAsync(i => i.Id == seed.InboundId, ct);
        inbound.ChatChannelId = chat.Id;
        await db.SaveChangesAsync(ct);
        clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        using var bridge = Bridge(h);
        await bridge.ScanChannelReplyAlertsAsync(ct);
        (await db.AgentIncidents.CountAsync(i => i.FailureReason == "ChannelNoOutboundReply", ct)).ShouldBe(0);
        clock.Advance(TimeSpan.FromSeconds(1));
        h.EventBus.ThrowOnceOnEvent = "AlertRaised";
        await Should.ThrowAsync<InvalidOperationException>(() => bridge.ScanChannelReplyAlertsAsync(ct));
        (await db.AgentIncidents.AsNoTracking().SingleAsync(i => i.FailureReason == "ChannelNoOutboundReply", ct))
            .HumanNotifiedAt.ShouldBeNull();
        await bridge.ScanChannelReplyAlertsAsync(ct);
        await bridge.ScanChannelReplyAlertsAsync(ct);
        var incident = await db.AgentIncidents.SingleAsync(i => i.FailureReason == "ChannelNoOutboundReply", ct);
        incident.HumanNotifiedAt.ShouldBe(h.Now);
        var push = h.EventBus.PublishedEvents.Single(e => e.EventName == "AlertRaised");
        var payload = JsonSerializer.SerializeToElement(push.Payload);
        payload.GetProperty("id").GetGuid().ShouldBe(incident.Id);
        payload.GetProperty("severity").GetString().ShouldBe("Critical");
        incident.Message.ShouldContain(seed.NativeMessageId);
        h.EventBus.PublishedEvents.Count(e => e.EventName == "AgentChanged"
            && JsonSerializer.Serialize(e.Payload).Contains(incident.Id.ToString())).ShouldBe(1);
        h.Messaging.SentReplies.ShouldBeEmpty();
    }

    [Test]
    [Timeout(120_000)]
    public async Task StartupAssistantCannotAnswerThePreviousGenerationsInbound(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await OpenAsync(schema.ConnectionString, TimeProvider.System);
        var chat = await h.BindChannelAsync();
        const string body = "synthetic correlated first turn";
        await h.SeedChannelCorrelationAsync(body, $"telegram:{chat}");
        await h.InsertTurnAsync(body, "synthetic first answer");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, ct);
        await h.DrainOutboundAsync();
        h.Messaging.SentReplies.Count.ShouldBe(1);
        var boundary = await h.InsertTranscriptEntryAsync(TranscriptKinds.SessionRestartBoundary);
        var stray = await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "synthetic startup output");
        await using var db = Db(schema.ConnectionString);
        (await TranscriptTurnWindow.FindActionBoundaryAsync(db, h.SessionId, stray, ct)).ShouldBeNull();
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, ct);
        await h.DrainOutboundAsync();
        h.Messaging.SentReplies.Count.ShouldBe(1);

        // Idle relaunches from older versions have no interrupted-turn boundary. Establish
        // the previous generation by the ingestion time of its timestamp-less entries.
        var restarted = h.Now.AddMinutes(-1);
        await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId && t.Sequence == boundary).ExecuteDeleteAsync(ct);
        await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId && t.Sequence < stray)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Timestamp, (DateTime?)null)
                .SetProperty(t => t.CreatedAt, restarted.AddMinutes(-1)), ct);
        await db.AgentSessions.Where(a => a.Id == h.SessionId).ExecuteUpdateAsync(s => s.SetProperty(a => a.StartedAt, restarted), ct);
        (await TranscriptTurnWindow.FindActionBoundaryAsync(db, h.SessionId, stray, ct)).ShouldBeNull();
    }

    [Test]
    [Timeout(120_000)]
    public async Task ConfirmedWithholdAppendsOneClosureWithPromptEvidence(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await OpenAsync(schema.ConnectionString, Clock());
        var seed = await SeedAsync(h, h.Now.AddHours(-1), h.Now.AddMinutes(-20), 1);
        using var bridge = Bridge(h);
        await bridge.ScanWithheldChannelIncidentsAsync(ct);
        await using var db = Db(schema.ConnectionString);
        var incident = await db.AgentIncidents.AsNoTracking().SingleAsync(i => i.FailureReason == "ChannelInputWithheld", ct);
        await db.SessionQueuedMessages.Where(m => m.Id == seed.QueueId).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Status, QueuedMessageStatus.Sent).SetProperty(m => m.DeliveryAttempts, 1)
            .SetProperty(m => m.LastDeliveryStartedAt, h.Now).SetProperty(m => m.LastDeliveryBaselineSequence, 0L)
            .SetProperty(m => m.DeliveryVerdict, DeliveryVerdict.Delivered), ct);
        await bridge.ScanChannelReplyAlertsAsync(ct);
        (await db.AgentIncidents.CountAsync(i => i.FailureReason == incident.Id.ToString(), ct)).ShouldBe(0);
        var seq = await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, WithheldBody, timestamp: h.Now);
        await bridge.ScanChannelReplyAlertsAsync(ct);
        await bridge.ScanChannelReplyAlertsAsync(ct);
        var closure = (await db.AgentIncidents.Where(i => i.FailureReason == incident.Id.ToString()).ToListAsync(ct)).ShouldHaveSingleItem();
        closure.Message.ShouldContain(seed.QueueId.ToString());
        closure.Message.ShouldContain($"promptSequence={seq}");
        closure.Message.ShouldNotContain(WithheldBody);
    }

    [Test]
    [Timeout(120_000)]
    public async Task PriorGenerationStandingResumeNoteIsClosedWithoutWriting(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await OpenProducerAsync(schema.ConnectionString, TimeProvider.System);
        await h.InsertStoreVisibleTurnAsync("fixture session", "fixture idle");
        await using var db = Db(schema.ConnectionString);
        var generation = await db.AgentSessions.Where(s => s.Id == h.SessionId).Select(s => s.StartedAt).SingleAsync(ct);
        var id = await h.SeedPendingMessageAsync("synthetic prior generation", origin: QueuedMessageOrigin.System);
        await db.SessionQueuedMessages.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.RuntimeActionKey, StandingResumeNote.Key(generation.AddMinutes(-1))), ct);
        await db.SessionQueuedMessages.Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CreatedAt, generation.AddMinutes(-1)), ct);
        await h.MarkWorkingAsync();
        await h.Queue.FlushStrandedQueuesAsync(ct);
        var closed = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id, ct);
        closed.Status.ShouldBe(QueuedMessageStatus.Canceled);
        closed.CanceledAt.ShouldNotBeNull();
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1591_AgedWithheldChannelRowRaisesOneIncident(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(h, acceptedAt: now.AddHours(-2), firstWithheldAt: now.AddMinutes(-10),
            withholdCount: 1);
        await Bridge(h).ScanWithheldChannelIncidentsAsync(ct);

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
    [Timeout(120_000)]
    public async Task C1591_RepeatScanStaysOneIncident(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        await SeedAsync(h, acceptedAt: now.AddHours(-2), firstWithheldAt: now.AddMinutes(-10), withholdCount: 1);
        var bridge = Bridge(h);
        await bridge.ScanWithheldChannelIncidentsAsync(ct);
        await bridge.ScanWithheldChannelIncidentsAsync(ct);
        await bridge.ScanWithheldChannelIncidentsAsync(ct); // Include the cursor wrap.

        await using var db = Db(schema.ConnectionString);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ChannelInputWithheld")).ShouldBe(1);
        (await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.AgentId == h.AgentId))
            .InputWithheldIncidentAt.ShouldNotBeNull();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1591_YoungWithholdStaysQuiet(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(h, acceptedAt: now.AddHours(-2), firstWithheldAt: now.AddMinutes(-9),
            withholdCount: 1);
        await Bridge(h).ScanWithheldChannelIncidentsAsync(ct);

        await using var db = Db(schema.ConnectionString);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ChannelInputWithheld")).ShouldBe(0);
        (await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == seed.InboundId))
            .InputWithheldIncidentAt.ShouldBeNull();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1591_WorkingUnchargedRowStaysQuiet(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(h, acceptedAt: now.AddMinutes(-30), firstWithheldAt: now.AddMinutes(-30),
            withholdCount: 0);
        await Bridge(h).ScanWithheldChannelIncidentsAsync(ct);

        await using var db = Db(schema.ConnectionString);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ChannelInputWithheld")).ShouldBe(0);
        (await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == seed.InboundId))
            .InputWithheldIncidentAt.ShouldBeNull();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1591_ScanWritesNoInput(CancellationToken ct)
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
        await gate.WaitAsync(ct);
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
    [Timeout(120_000)]
    public async Task C1591_WorkingSessionKeepsHistoricalWithholdQuiet(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenProducerAsync(schema.ConnectionString, clock);
        await h.InsertStoreVisibleTurnAsync("earlier prompt", "earlier reply");
        await ProduceWithholdAsync(h, clock, "busy body");
        await h.MarkWorkingAsync();
        clock.Advance(TimeSpan.FromMinutes(10));
        await Bridge(h, clock).ScanWithheldChannelIncidentsAsync(ct);

        await using var db = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(db, h.AgentId)).ShouldBe(0);
    }

    [Test]
    [Arguments("suspended")]
    [Arguments("continuity")]
    [Arguments("liveness")]
    [Arguments("compacting")]
    [Arguments("legacy-liveness")]
    [Arguments("herdr")]
    [Timeout(120_000)]
    public async Task C1591_CurrentHoldKeepsHistoricalWithholdQuiet(string hold, CancellationToken ct)
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
            state.LivenessLatchedAt = hold == "legacy-liveness" ? at.AddYears(-1)
                : hold == "liveness" ? at : null;
            state.ActiveCompactionRecoveryId = hold == "compacting" ? Guid.NewGuid() : null;
            state.HerdrFailureHeldAt = hold == "herdr" ? at : null;
            state.UpdatedAt = at;
            await db.SaveChangesAsync();
        }
        clock.Advance(TimeSpan.FromMinutes(10));
        await Bridge(h, clock).ScanWithheldChannelIncidentsAsync(ct);

        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(0);
    }

    [Test]
    [Arguments("continuity")]
    [Arguments("suspension")]
    [Arguments("liveness")]
    [Timeout(120_000)]
    public async Task C1591_ExistingHoldStampSuppressesSecondIncident(string stamp, CancellationToken ct)
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
        await Bridge(h, clock).ScanWithheldChannelIncidentsAsync(ct);

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
    public async Task C1591_OpenDispositionDoesNotIncident(string disposition, CancellationToken ct)
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
            await Bridge(h).ScanWithheldChannelIncidentsAsync(ct);
            started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
            await using (var mid = Db(schema.ConnectionString))
                (await WithheldIncidentsAsync(mid, h.AgentId)).ShouldBe(0);
            await tx.CommitAsync();
        }

        await Bridge(h).ScanWithheldChannelIncidentsAsync(ct);
        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(0);
        (await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == seed.InboundId))
            .InputWithheldIncidentAt.ShouldBeNull();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1591_BacklogPassIsBounded(CancellationToken ct)
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

        await using (var connection = new NpgsqlConnection(schema.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using (var analyze = new NpgsqlCommand("ANALYZE \"SessionQueuedMessages\"; ANALYZE \"ChannelInbounds\"", connection))
                await analyze.ExecuteNonQueryAsync(ct);
            foreach (var indexProof in new[] { false, true })
            {
                if (indexProof)
                {
                    await using var preferIndex = new NpgsqlCommand("SET enable_seqscan = off", connection);
                    await preferIndex.ExecuteNonQueryAsync(ct);
                }
                await using var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) "
                    + ChannelBridgeService.WithheldIncidentQueuePageSql + "\n"
                    + ChannelBridgeService.WithheldIncidentQueuePageEndSql, connection);
                explain.Parameters.AddWithValue("agedAt", now.AddMinutes(-10));
                explain.Parameters.AddWithValue("pageSize", 64);
                explain.CommandTimeout = 5;
                var plan = (string)(await explain.ExecuteScalarAsync(ct))!;
                Console.WriteLine($"C1591 backlog defaultPlanner={!indexProof}: {plan}");
                if (indexProof)
                    plan.ShouldContain("IX_SessionQueuedMessages_AgedChannelWithhold");
            }
        }

        var bridge = Bridge(h);
        commands.Armed = true;
        await bridge.ScanWithheldChannelIncidentsAsync(ct);
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
        Console.WriteLine($"C1591 backlog first-pass commands={commands.Count}");
        await bridge.ScanWithheldChannelIncidentsAsync(ct);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(128,
            "the cursor must advance past the already stamped oldest page");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1591_ProducerPathPagesOneAgedWithhold(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenProducerAsync(schema.ConnectionString, clock);
        await h.InsertStoreVisibleTurnAsync("earlier prompt", "earlier reply");
        var chat = await h.BindChannelAsync();
        await using (var db = Db(schema.ConnectionString))
        {
            await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.AlwaysOn, false), ct);
            await db.ChatChannels.Where(c => c.ExternalId == chat)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DigestEnabled, true));
        }
        const string body = "family aged withhold 9f3a";
        h.Adapter.DialogAdopted = true;
        var native = Guid.NewGuid().ToString("N");
        using var service = Bridge(h, clock);
        await service.StartAsync(ct);
        try
        {
            h.Messaging.InjectInbound(Message(chat, native, body));
            await WaitForAsync(async () =>
            {
                await using var db = Db(schema.ConnectionString);
                return await db.SessionQueuedMessages.AnyAsync(m => m.Origin == QueuedMessageOrigin.Channel
                    && m.AgentSessionId == h.SessionId && m.WithholdCount > 0);
            });
            h.Messaging.AcknowledgedCount.ShouldBe(1);
            clock.Advance(TimeSpan.FromMinutes(10));
            await WaitForAsync(async () =>
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                await using var db = Db(schema.ConnectionString);
                return await db.AgentIncidents.AnyAsync(i => i.AgentId == h.AgentId
                    && i.FailureReason == "ChannelInputWithheld" && i.HumanNotifiedAt != null, ct);
            });
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await service.StopAsync(stop.Token);
        }
        h.Adapter.SentInput.ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();

        await using var verify = Db(schema.ConnectionString);
        var incident = await verify.AgentIncidents.AsNoTracking().SingleAsync(i => i.AgentId == h.AgentId
            && i.FailureReason == "ChannelInputWithheld");
        incident.Kind.ShouldBe(AgentIncidentKind.ChannelReplyLost);
        incident.Severity.ShouldBe(AlertSeverity.Critical);
        incident.Message.ShouldContain(native);
        incident.Message.ShouldNotContain(body);
        var attention = new AttentionService(verify, h.Runner,
            h.Provider.GetRequiredService<IOptions<SupervisionSettings>>(),
            h.Provider.GetRequiredService<IOptions<DelegationSettings>>(),
            clock, NullLogger<AttentionService>.Instance);
        var rows = (await attention.GetAsync(ct, includeProgressProbe: false)).Items
            .Where(i => i.Kind == AttentionKind.RecentCriticalIncident && i.AgentId == h.AgentId
                && i.Evidence.Contains(native, StringComparison.Ordinal))
            .ToList();
        rows.Count.ShouldBe(1);
        rows[0].Evidence.ShouldContain(native);

        var digest = new DigestSettings
        {
            WakeOnIncidentKinds = [AgentIncidentKind.ChannelReplyLost],
            TimeZone = "Europe/London",
            PublicBaseUrl = "https://antiphon.example",
        };
        await using var pageDb = Db(schema.ConnectionString);
        incident.HumanNotifiedAt.ShouldNotBeNull();
        // Exercise the digest sweep first, with every other paging prerequisite satisfied.
        await pageDb.Agents.Where(a => a.Id == h.AgentId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AlwaysOn, true), ct);
        await pageDb.AgentIncidents.Where(i => i.Id == incident.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.HumanNotifiedAt, (DateTime?)null), ct);
        var notifier = new IncidentPageNotifier(pageDb,
            new ChatChannelService(pageDb, clock, h.Messaging), Options.Create(digest),
            clock, NullLogger<IncidentPageNotifier>.Instance);
        await notifier.SweepAsync(ct);
        await notifier.SweepAsync(ct);
        (await pageDb.AgentIncidents.AsNoTracking().SingleAsync(i => i.Id == incident.Id, ct))
            .HumanNotifiedAt.ShouldBeNull();
        h.Messaging.SentReplies.ShouldBeEmpty("an operational page must never be sent to the family chat");
        h.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "AgentChanged"
            && JsonSerializer.Serialize(e.Payload, (JsonSerializerOptions?)null).Contains(incident.Id.ToString()));
        var pushed = h.EventBus.PublishedEvents.Single(e => e.EventName == "AlertRaised");
        JsonSerializer.SerializeToElement(pushed.Payload).GetProperty("detail").GetString().ShouldBe(incident.Message);
        incident.Message.ShouldContain("/send-now");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1591_DeliveredUserPromptStaysQuiet(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenProducerAsync(schema.ConnectionString, TimeProvider.System);
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
            await h.Runtime.CatchUpTranscriptAsync(h.SessionId, ct);
            receipt.TrySetResult(submitted);
        };
        var chat = await h.BindChannelAsync();
        const string body = "delivered family body 9f3a";
        await Bridge(h, clock).HandleInboundAsync(Message(chat, Guid.NewGuid().ToString("N"), body), ct);
        var submitted = await receipt.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using (var db = Db(schema.ConnectionString))
        {
            var prompt = await db.TranscriptEntries.AsNoTracking().SingleAsync(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt && t.Sequence > floor && t.Text == submitted);
            prompt.Text.ShouldContain(body);
            prompt.Text.ShouldBe(submitted);
        }
        clock.Advance(TimeSpan.FromMinutes(10));
        await Bridge(h, clock).ScanWithheldChannelIncidentsAsync(ct);
        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(0);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1591_RestartAfterPruneDoesNotReraise(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        Guid agentId;
        Guid sessionId;
        SeedRow seed;
        await using (var h = await OpenAsync(schema.ConnectionString, clock))
        {
            agentId = h.AgentId;
            sessionId = h.SessionId;
            var now = clock.GetUtcNow().UtcDateTime;
            seed = await SeedAsync(h, now.AddHours(-2), now.AddMinutes(-10), 1);
            await Bridge(h).ScanWithheldChannelIncidentsAsync(ct);
            await using var db = Db(schema.ConnectionString);
            (await WithheldIncidentsAsync(db, agentId)).ShouldBe(1);
            await db.AgentIncidents.Where(i => i.AgentId == agentId).ExecuteDeleteAsync();
        }
        await using var restarted = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, TimeProvider = clock,
            PreserveDatabaseOnDispose = true, AttachAgentId = agentId, AttachSessionId = sessionId,
            Bridge = new ChannelBridgeSettings { Enabled = true, AgedHoldIncidentMinutes = 10 },
        });
        await Bridge(restarted).ScanWithheldChannelIncidentsAsync(ct);
        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, agentId)).ShouldBe(0);
        (await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == seed.InboundId))
            .InputWithheldIncidentAt.ShouldNotBeNull();
    }

    [Test]
    [Timeout(60_000)]
    public async Task C1591_NonShutdownCancellationContinues(CancellationToken ct)
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
        catch (Exception ex)
        {
            loopError = ex;
        }
        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(1);
        loopError.ShouldBeNull();
        logger.Lines.ShouldContain(line => line.Contains("retrying", StringComparison.OrdinalIgnoreCase));
        logger.Lines.ShouldContain(line => line.Contains("Raised 1 withheld", StringComparison.Ordinal));
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    [Timeout(120_000)]
    public async Task C1591_FailedBatchRollsBackAndConcurrentHostsStayOnce(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        var commands = new ScanCommands();
        await using var h = await OpenAsync(schema.ConnectionString, clock, o => o.AddInterceptors(commands));
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(h, now.AddHours(-2), now.AddMinutes(-10), 1);
        commands.Fault = sql => sql.Contains("INSERT INTO \"AgentIncidents\"", StringComparison.Ordinal)
            ? new InvalidOperationException("injected incident persistence failure") : null;
        commands.Armed = true;
        await Should.ThrowAsync<Exception>(() => Bridge(h).ScanWithheldChannelIncidentsAsync(ct));
        commands.Armed = false;
        await using (var db = Db(schema.ConnectionString))
        {
            (await WithheldIncidentsAsync(db, h.AgentId)).ShouldBe(0);
            (await db.ChannelInbounds.SingleAsync(i => i.Id == seed.InboundId)).InputWithheldIncidentAt.ShouldBeNull();
        }
        await Task.WhenAll(Bridge(h).ScanWithheldChannelIncidentsAsync(ct),
            Bridge(h).ScanWithheldChannelIncidentsAsync(ct));
        await using var verify = Db(schema.ConnectionString);
        (await WithheldIncidentsAsync(verify, h.AgentId)).ShouldBe(1);
        (await verify.ChannelInbounds.SingleAsync(i => i.Id == seed.InboundId)).InputWithheldIncidentAt.ShouldNotBeNull();
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(25);
        }
        throw new TimeoutException("Hosted bridge did not reach the expected durable state");
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

    [Test]
    public async Task Partial_operator_page_resumes_once_across_concurrent_scans(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await OpenAsync(schema.ConnectionString, Clock());
        await SeedAsync(h, h.Now.AddHours(-1), h.Now.AddMinutes(-20), 1);
        using var first = Bridge(h);
        using var second = Bridge(h);
        await first.ScanWithheldChannelIncidentsAsync(ct);
        h.EventBus.ThrowOnceOnEvent = "AgentChanged";
        await Should.ThrowAsync<InvalidOperationException>(() => first.ScanChannelReplyAlertsAsync(ct));
        await Task.WhenAll(first.ScanChannelReplyAlertsAsync(ct), second.ScanChannelReplyAlertsAsync(ct));
        h.EventBus.PublishedEvents.Count(e => e.EventName == "AlertRaised").ShouldBe(1);
        await using var db = Db(schema.ConnectionString);
        (await db.AgentIncidents.SingleAsync(i => i.FailureReason == "ChannelInputWithheld", ct))
            .HumanNotifiedAt.ShouldNotBeNull();
        h.Messaging.SentReplies.ShouldBeEmpty();
    }

    [Test]
    [Arguments(120)]
    [Arguments(-120)]
    public async Task Group_deadline_uses_elapsed_time_despite_a_wall_clock_step(int minutes, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var timer = Clock();
        var clock = new CorrectedClock(timer);
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        using var bridge = Bridge(h);
        var seed = await SeedUnansweredGroupAsync(h, ct);
        clock.Correction = TimeSpan.FromMinutes(minutes);
        timer.Advance(TimeSpan.FromMinutes(9));
        await bridge.ScanChannelReplyAlertsAsync(ct);
        await using var db = Db(schema.ConnectionString);
        (await db.AgentIncidents.AnyAsync(i => i.Id == seed.InboundId, ct)).ShouldBeFalse();
        timer.Advance(TimeSpan.FromMinutes(1));
        await bridge.ScanChannelReplyAlertsAsync(ct);
        (await db.AgentIncidents.AnyAsync(i => i.Id == seed.InboundId, ct)).ShouldBeTrue();
    }

    [Test]
    public async Task Broker_published_reply_without_provider_acceptance_remains_overdue(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = Clock();
        await using var h = await OpenAsync(schema.ConnectionString, clock);
        using var bridge = Bridge(h);
        var seed = await SeedUnansweredGroupAsync(h, ct);
        await using var db = Db(schema.ConnectionString);
        await db.SessionQueuedMessages.Where(q => q.Id == seed.QueueId)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.DirectReplyThroughSequence, 5L), ct);
        clock.Advance(TimeSpan.FromMinutes(10));
        await bridge.ScanChannelReplyAlertsAsync(ct);
        (await db.AgentIncidents.CountAsync(i => i.Id == seed.InboundId, ct)).ShouldBe(1);
        h.Messaging.SentReplies.ShouldBeEmpty();
    }

    private static async Task<SeedRow> SeedUnansweredGroupAsync(BridgeQueueHarness h, CancellationToken ct)
    {
        var seed = await SeedAsync(h, h.Now, null, 0);
        await h.BindChannelAsync(seed.ConversationId);
        await using var db = Db(h.ConnectionString);
        var chat = await db.ChatChannels.SingleAsync(c => c.ExternalId == seed.ConversationId, ct);
        chat.Kind = ChatChannelKind.Group;
        var inbound = await db.ChannelInbounds.SingleAsync(i => i.Id == seed.InboundId, ct);
        inbound.ChatChannelId = chat.Id;
        await db.SaveChangesAsync(ct);
        return seed;
    }

    private sealed class CorrectedClock(TimeProvider timer) : TimeProvider
    {
        public TimeSpan Correction { get; set; }
        public override DateTimeOffset GetUtcNow() => timer.GetUtcNow() + Correction;
        public override long GetTimestamp() => timer.GetTimestamp();
        public override long TimestampFrequency => timer.TimestampFrequency;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            timer.CreateTimer(callback, state, dueTime, period);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Withheld_closure_requires_the_owning_prompt(bool quoted, CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await OpenAsync(schema.ConnectionString, Clock());
        var first = await SeedAsync(h, h.Now.AddHours(-1), h.Now.AddMinutes(-20), 1);
        var second = await SeedAsync(h, h.Now.AddHours(-1), h.Now.AddMinutes(-20), 1);
        using var bridge = Bridge(h);
        await bridge.ScanWithheldChannelIncidentsAsync(ct);
        await using var db = Db(schema.ConnectionString);
        await db.SessionQueuedMessages.Where(m => m.Id == first.QueueId || m.Id == second.QueueId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, QueuedMessageStatus.Sent)
                .SetProperty(m => m.DeliveryAttempts, 1).SetProperty(m => m.LastDeliveryBaselineSequence, 0L)
                .SetProperty(m => m.LastDeliveryStartedAt, h.Now)
                .SetProperty(m => m.DeliveryVerdict, DeliveryVerdict.Delivered), ct);
        var body = quoted ? ChannelPromptCorrelation.Mark(first.QueueId, WithheldBody) : WithheldBody;
        if (quoted)
            await db.SessionQueuedMessages.Where(q => q.Id == first.QueueId)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.Body, body), ct);
        var owned = await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body, timestamp: h.Now);
        await db.SessionQueuedMessages.Where(q => q.Id == second.QueueId)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.LastDeliveryBaselineSequence, owned), ct);
        var disputedBody = quoted ? "Synthetic machine delivery quoting " + body : body;
        if (quoted)
        {
            var machine = await h.SeedChannelCorrelationAsync(disputedBody, "telegram:fixture");
            await db.SessionQueuedMessages.Where(q => q.Id == machine)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.Origin, QueuedMessageOrigin.Check), ct);
        }
        var disputed = await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, disputedBody, timestamp: h.Now);
        var candidates = await ChannelWithheldResolution.Candidates(db).ToListAsync(ct);
        candidates.Count.ShouldBe(2);
        var candidate = candidates.Single(c => c.QueueId == (quoted ? first.QueueId : second.QueueId));
        (await ChannelWithheldResolution.CloseWithEvidenceAsync(db, candidate, disputed, h.Now, ct)).ShouldBeNull();
        await bridge.ScanChannelReplyAlertsAsync(ct);
        var closed = (await db.AgentIncidents.Where(i => i.Kind == AgentIncidentKind.ChannelInputWithheldClosed)
            .ToListAsync(ct)).ShouldHaveSingleItem();
        closed.Message.ShouldContain(first.QueueId.ToString());
        closed.Message.ShouldContain($"promptSequence={owned},");
    }

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
        public List<LogLevel> Levels { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
            Levels.Add(logLevel);
        }

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
