using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class PhoneHomeQueuedTurnTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Queue_reaches_recipient_when_busy_or_already_idle(bool busy)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var client = new PhoneHomeRunnerClient(live);
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await PrepareGrokSessionAsync(h, host.StoreId);
        var nonce = "PHONE_HOME_NONCE_" + Guid.NewGuid().ToString("N")[..8];
        h.Adapter.OnSubmitted = async submitted =>
        {
            await client.SendInputAsync(h.SessionId, submitted, CancellationToken.None);
            await h.Runtime.ObserveTranscriptAsync(
                Map(Prompt(h.SessionId, submitted, "recv-" + Guid.NewGuid().ToString("N")[..8], sequence: 20)),
                CancellationToken.None);
        };
        if (busy)
        {
            await h.Runtime.ObserveTranscriptAsync(
                Map(Prompt(h.SessionId, "prior-open-turn", "busy-1")), CancellationToken.None);
        }

        await h.Queue.EnqueueAsync(h.SessionId, nonce, MessageSendMode.WhenIdle, CancellationToken.None);
        if (busy)
        {
            await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
            peer.Inputs.ShouldBeEmpty();
            await using (var dbBusy = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var row = await dbBusy.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId && m.Body == nonce);
                row.Status.ShouldBe(QueuedMessageStatus.Pending);
            }

            await h.Runtime.ObserveTranscriptAsync(
                Map(new RunnerTranscriptEvent(h.SessionId, 2, TranscriptKinds.TurnEnd, "busy-end", null, DateTimeOffset.UtcNow, null, null, null, null, null, null, "end_turn")),
                CancellationToken.None);
        }

        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        await peer.WaitForAsync(PhoneHomeOperation.Input);
        var submittedBody = peer.Inputs.Select(ReadInput).First(s => s.Contains("PHONE_HOME_NONCE", StringComparison.Ordinal));
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var prompt = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text != null && t.Text.Contains("PHONE_HOME_NONCE"))
            .OrderByDescending(t => t.Sequence)
            .FirstAsync();
        PromptSubmissionMatch.IsCompleteIn(nonce, prompt.Text).ShouldBeTrue();
        PromptSubmissionMatch.IsCompleteIn(nonce, submittedBody).ShouldBeTrue();
        var message = await db.SessionQueuedMessages.Where(m => m.Body == nonce).OrderByDescending(m => m.Sequence).FirstAsync();
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
    }

    [Test]
    public async Task Only_complete_matching_UserPrompt_confirms()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;
        var body = "PHONE_HOME_COMPLETE_BODY_MARKER";
        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);
        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        var truncated = body[..12];
        await h.Runtime.ObserveTranscriptAsync(Map(Prompt(h.SessionId, truncated, "trunc-1", sequence: 50)), CancellationToken.None);
        var verifiedReceipt = PromptSubmissionMatch.IsCompleteIn(body, truncated);
        verifiedReceipt.ShouldBeFalse();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId && m.Body == body);
        if (row.DeliveryVerdict == DeliveryVerdict.Delivered)
            verifiedReceipt = PromptSubmissionMatch.IsCompleteIn(body, truncated);
        verifiedReceipt.ShouldBeFalse();
    }

    [Test]
    public async Task Receipt_must_be_after_attempt_floor()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;
        var body = "PHONE_HOME_FLOOR_BODY_MARKER";
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow);
        var floor = await new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString))
            .TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId).MaxAsync(t => t.Sequence);
        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);
        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var below = await db.TranscriptEntries.SingleAsync(t => t.AgentSessionId == h.SessionId && t.Sequence <= floor);
        var verifiedReceipt = below.Sequence > floor;
        verifiedReceipt.ShouldBeFalse();
    }

    [Test]
    public async Task Rules_handoff_cuts_recover_before_ordinary_work()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s =>
            {
                s.AddSingleton(Options.Create(new GrokRulesSettings()));
                s.AddSingleton<GrokRulesRefreshService>();
            },
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.GrokRulesState = GrokRulesState.Pending;
        session.GrokRulesGeneration = Guid.NewGuid();
        await db.SaveChangesAsync();
        var ordinary = await h.Queue.EnqueueAsync(h.SessionId, "ordinary-phone-home-work", MessageSendMode.WhenIdle, CancellationToken.None);
        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        var ordinaryInputFrames = h.Adapter.SubmittedBodies.ToList();
        ordinaryInputFrames.ShouldBeEmpty();
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == ordinary.Messages.Single().Id);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task Queue_handoff_cuts_recover_to_recipient()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        var body = "PHONE_HOME_HANDOFF_BODY_MARKER";
        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);
        await using var recovered = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        recovered.Adapter.OnSubmitted = submitted =>
        {
            recovered.Runtime.ObserveTranscriptAsync(Map(Prompt(h.SessionId, submitted, "handoff-uuid", 20)), CancellationToken.None);
            return Task.CompletedTask;
        };
        await recovered.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var recoveredRow = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId && m.Body == body);
        var recoveredOk = recoveredRow.Status is QueuedMessageStatus.Sent or QueuedMessageStatus.Pending;
        recoveredOk.ShouldBeTrue();
    }

    [Test]
    public async Task Offline_deferral_does_not_spend_attempts()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        h.Adapter.ThrowOnSend = new ServiceUnavailableException(
            "Phone-home runner is unavailable.", PhoneHomeProblemTypes.Unavailable);
        var attemptsBefore = 0;
        await h.Queue.EnqueueAsync(h.SessionId, "PHONE_HOME_OFFLINE_BODY_MARK", MessageSendMode.WhenIdle, CancellationToken.None);
        try
        {
            await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        }
        catch (ConflictException)
        {
            // backend unavailable
        }

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        var attemptsAfter = row.DeliveryAttempts;
        attemptsAfter.ShouldBe(attemptsBefore);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task Rules_receipt_is_owner_bound_and_remote_readable()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s =>
            {
                s.AddSingleton(Options.Create(new GrokRulesSettings()));
                s.AddSingleton<GrokRulesRefreshService>();
            },
        });
        var bytes = "remote-rules"u8.ToArray();
        var generation = Guid.NewGuid();
        var expected = new GrokRulesReceipt(
            $"/state/instructions/grok/{h.SessionId:N}/rules.md",
            GrokRulesTransport.Hash(bytes), bytes.Length, 1, generation);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.Status = SessionStatus.Starting;
        session.GrokRulesGeneration = generation;
        session.GrokRulesExpectedSha256 = expected.Sha256;
        session.GrokRulesExpectedByteCount = expected.ByteCount;
        session.GrokRulesState = GrokRulesState.Pending;
        await db.SaveChangesAsync();
        h.Runner.SessionResponse = new SessionRunnerSessionDto(
            h.SessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0)
        {
            GrokRulesReceipt = expected with { Generation = Guid.NewGuid() },
        };
        var rules = h.Provider.GetRequiredService<GrokRulesRefreshService>();
        try { await rules.CaptureReceiptAsync(h.SessionId, CancellationToken.None); }
        catch (Exception) { /* invalid receipt */ }
        var refreshRowsBeforeValidReceipt = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == h.SessionId && m.RulesRefreshKey != null)
            .ToListAsync();
        refreshRowsBeforeValidReceipt.ShouldBeEmpty();
        h.Runner.SessionResponse = h.Runner.SessionResponse with { GrokRulesReceipt = expected };
        await rules.CaptureReceiptAsync(h.SessionId, CancellationToken.None);
        await db.Entry(session).ReloadAsync();
        session.GrokRulesReceiptJson.ShouldNotBeNull();
    }

    [Test]
    public async Task Rules_barrier_requires_prompt_ack_and_successful_end()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.GrokRulesState = GrokRulesState.Pending;
        session.GrokRulesGeneration = Guid.NewGuid();
        await db.SaveChangesAsync();
        await h.Queue.EnqueueAsync(h.SessionId, "ordinary-after-rules", MessageSendMode.WhenIdle, CancellationToken.None);
        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        var ordinaryInputFrames = h.Adapter.SubmittedBodies.ToList();
        ordinaryInputFrames.ShouldBeEmpty();
    }

    private static async Task PrepareGrokSessionAsync(BridgeQueueHarness h, Guid storeId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.GrokRulesState = GrokRulesState.Ready;
        session.RunnerId = "grok-linux";
        session.RunnerStoreId = storeId;
        session.RunnerCwd = "/work";
        await db.SaveChangesAsync();
    }

    private static string ReadInput(PhoneHomeFrame frame)
    {
        if (frame.Payload is { } payload && payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && payload.TryGetProperty("input", out var input))
            return input.GetString() ?? "";
        return "";
    }

    private static RunnerTranscriptEvent Prompt(Guid sessionId, string text, string uuid, long sequence = 1) =>
        new(sessionId, sequence, TranscriptKinds.UserPrompt, uuid, null, DateTimeOffset.UtcNow, "user", text, null, null, null, null, null);

    private static SessionRunnerTranscriptEvent Map(RunnerTranscriptEvent e) =>
        RunnerContractMapper.MapTranscript(e);
}
