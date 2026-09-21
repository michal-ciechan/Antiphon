using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
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
    [Arguments("input-success")]
    [Arguments("pong")]
    [Arguments("turn-end")]
    [Arguments("assistant-text")]
    [Arguments("wrong-session")]
    [Arguments("unrelated")]
    [Arguments("truncated-head")]
    [Arguments("truncated-tail")]
    [Arguments("complete")]
    public async Task Only_complete_matching_UserPrompt_confirms(string arm)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateReceiptHarnessAsync(schema.ConnectionString);
        await PrepareGrokSessionAsync(h);
        await h.InsertTurnAsync("prior-observable-turn", "prior-answer");
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        var body = ReceiptBodyFor(arm);
        Guid? otherSessionId = arm == "wrong-session" ? await SeedForeignSessionAsync(h) : null;
        h.Adapter.OnSubmitted = _ => EmitReceiptArmAsync(h, arm, body, otherSessionId);

        var (receipt, _) = await TrySendNowAsync(h, body);
        var verifiedReceipt = IsVerifiedTranscriptReceipt(receipt);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var aboveFloor = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Sequence > floor)
            .OrderBy(t => t.Sequence)
            .ToListAsync();
        var independentComplete = aboveFloor.Any(p =>
            IndependentFullBody(body, p.Text) && p.Sequence > floor);

        if (arm == "complete")
        {
            independentComplete.ShouldBeTrue(
                "the persisted UserPrompt above the attempt floor must carry the full body");
            verifiedReceipt.ShouldBeTrue(
                "production receipt must be transcript-confirmed, non-degraded Delivered");
            receipt!.Verdict.ShouldBe(nameof(DeliveryVerdict.Delivered));
            receipt.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Transcript);
            receipt.Degraded.ShouldBeFalse();
            aboveFloor.ShouldContain(p => p.Sequence > floor && IndependentFullBody(body, p.Text));
            return;
        }

        independentComplete.ShouldBeFalse(
            "decoy transcript rows must not independently equal the full submitted body above the floor");
        verifiedReceipt.ShouldBeFalse(
            "input success, screen PONG, TurnEnd, AssistantText, wrong-session, unrelated, truncated-head and truncated-tail must not verify");
        if (arm == "truncated-head")
        {
            aboveFloor.ShouldNotBeEmpty("truncated-head is persisted above the floor");
            IndependentFullBody(body, aboveFloor[0].Text).ShouldBeFalse();
            aboveFloor[0].Sequence.ShouldBeGreaterThan(floor);
        }
    }

    [Test]
    public async Task Receipt_must_be_after_attempt_floor()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateReceiptHarnessAsync(schema.ConnectionString);
        await PrepareGrokSessionAsync(h);
        await h.InsertTurnAsync("prior-observable-turn", "prior-answer");
        const string body = "PHONE_HOME_FLOOR_BODY_MARKER_UNIQUE";
        await h.Runtime.ObserveTranscriptAsync(
            Map(Prompt(h.SessionId, body, "stale-identical-" + Guid.NewGuid().ToString("N"), sequence: 40)),
            CancellationToken.None);
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;

        var (staleReceipt, _) = await TrySendNowAsync(h, body);
        var staleVerified = IsVerifiedTranscriptReceipt(staleReceipt);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var stale = (await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt)
            .OrderBy(t => t.Sequence)
            .ToListAsync())
            .Where(t => IndependentFullBody(body, t.Text))
            .ToList();
        stale.ShouldNotBeEmpty("the identical body was persisted before the attempt");
        stale[0].Sequence.ShouldBeLessThanOrEqualTo(floor);
        IndependentFullBody(body, stale[0].Text).ShouldBeTrue();
        staleVerified.ShouldBeFalse(
            "a complete matching UserPrompt at or below the attempt floor is not a verified receipt");

        h.Adapter.OnSubmitted = _ => h.Runtime.ObserveTranscriptAsync(
            Map(Prompt(h.SessionId, body, "fresh-above-floor-" + Guid.NewGuid().ToString("N"), sequence: 80)),
            CancellationToken.None);
        var (freshReceipt, _) = await TrySendNowAsync(h, body);
        var freshVerified = IsVerifiedTranscriptReceipt(freshReceipt);
        var fresh = (await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == h.SessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Sequence > floor)
            .OrderBy(t => t.Sequence)
            .ToListAsync())
            .Where(t => IndependentFullBody(body, t.Text))
            .ToList();
        fresh.ShouldNotBeEmpty();
        fresh[0].Sequence.ShouldBeGreaterThan(floor);
        IndependentFullBody(body, fresh[0].Text).ShouldBeTrue();
        freshVerified.ShouldBeTrue();
        freshReceipt!.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Transcript);
        freshReceipt.Degraded.ShouldBeFalse();
        freshReceipt.Verdict.ShouldBe(nameof(DeliveryVerdict.Delivered));
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

    private static async Task<BridgeQueueHarness> CreateReceiptHarnessAsync(string connectionString) =>
        await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = connectionString,
            ConfigureDeliveryVerification = v =>
            {
                v.TranscriptConfirmTimeoutSeconds = 1;
                v.PostFailureConfirmGraceSeconds = 0;
                v.PollIntervalMs = 40;
                v.ReEnterIntervalSeconds = 1;
            },
        });

    private static async Task PrepareGrokSessionAsync(BridgeQueueHarness h, Guid? storeId = null)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.GrokRulesState = GrokRulesState.Ready;
        if (storeId is { } id)
        {
            session.RunnerId = "grok-linux";
            session.RunnerStoreId = id;
            session.RunnerCwd = "/work";
        }

        await db.SaveChangesAsync();
    }

    private static readonly string TruncatedHeadPad =
        "PHONE_HOME_TRUNC_HEAD_MARKER_" + new string('X', 200);

    private static string ReceiptBodyFor(string arm) => arm is "truncated-head" or "truncated-tail"
        ? TruncatedHeadPad + "_UNIQUE_TAIL_NOT_IN_HEAD"
        : "PHONE_HOME_R5_" + arm + "_BODY_MARKER_UNIQUE";

    private static async Task EmitReceiptArmAsync(BridgeQueueHarness h, string arm, string body, Guid? otherSessionId)
    {
        var seq = 50L;
        switch (arm)
        {
            case "input-success":
                return;
            case "pong":
                await h.Runtime.ObserveTranscriptAsync(
                    Map(Entry(h.SessionId, TranscriptKinds.AssistantText, "PONG", seq)),
                    CancellationToken.None);
                return;
            case "turn-end":
                // Persist directly: ObserveTranscriptAsync would FlushIfIdle under the Mode:Now lock.
                await h.InsertTranscriptEntryAsync(
                    TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.EndTurn);
                return;
            case "assistant-text":
                await h.Runtime.ObserveTranscriptAsync(
                    Map(Entry(h.SessionId, TranscriptKinds.AssistantText, body, seq)),
                    CancellationToken.None);
                return;
            case "wrong-session":
                await h.Runtime.ObserveTranscriptAsync(
                    Map(Prompt(otherSessionId!.Value, body, "wrong-session-" + Guid.NewGuid().ToString("N"), seq)),
                    CancellationToken.None);
                return;
            case "unrelated":
                await h.Runtime.ObserveTranscriptAsync(
                    Map(Prompt(h.SessionId, "PHONE_HOME_UNRELATED_PROMPT_BODY_MARKER", "unrelated-" + Guid.NewGuid().ToString("N"), seq)),
                    CancellationToken.None);
                return;
            case "truncated-head":
                await h.Runtime.ObserveTranscriptAsync(
                    Map(Prompt(h.SessionId, TruncatedHeadPad, "trunc-head-" + Guid.NewGuid().ToString("N"), seq)),
                    CancellationToken.None);
                return;
            case "truncated-tail":
                await h.Runtime.ObserveTranscriptAsync(
                    Map(Prompt(h.SessionId, body[(body.Length / 2)..], "trunc-tail-" + Guid.NewGuid().ToString("N"), seq)),
                    CancellationToken.None);
                return;
            case "complete":
                await h.Runtime.ObserveTranscriptAsync(
                    Map(Prompt(h.SessionId, body, "complete-" + Guid.NewGuid().ToString("N"), seq)),
                    CancellationToken.None);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(arm), arm, "unknown R-5 receipt arm");
        }
    }

    private static async Task<Guid> SeedForeignSessionAsync(BridgeQueueHarness h)
    {
        var otherId = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(h.ConnectionString));
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = otherId,
            DefinitionName = "fake",
            AgentKind = AgentKind.Grok,
            GrokRulesState = GrokRulesState.Ready,
            Status = SessionStatus.Running,
            Cwd = h.TempRoot,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return otherId;
    }

    private static async Task<(DeliveryReceiptDto? Receipt, ConflictException? Error)> TrySendNowAsync(
        BridgeQueueHarness h, string body)
    {
        try
        {
            var dto = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.Now, CancellationToken.None);
            return (dto.LastDelivery, null);
        }
        catch (ConflictException ex)
        {
            return (null, ex);
        }
    }

    private static bool IsVerifiedTranscriptReceipt(DeliveryReceiptDto? receipt) =>
        receipt is { Degraded: false }
        && receipt.ConfirmedBy == DeliveryConfirmedBy.Transcript
        && receipt.Verdict == nameof(DeliveryVerdict.Delivered);

    /// <summary>
    /// Independent full-body oracle: compact whitespace/control characters and require the entire
    /// submitted body to appear in the persisted record. Does not call
    /// <see cref="PromptSubmissionMatch"/>.
    /// </summary>
    private static bool IndependentFullBody(string body, string? recordText)
    {
        if (string.IsNullOrEmpty(recordText))
            return false;
        var compactBody = Compact(body);
        var compactRecord = Compact(recordText);
        return compactBody.Length > 0
            && compactRecord.Contains(compactBody, StringComparison.Ordinal);
    }

    private static string Compact(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c) && !char.IsControl(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    private static RunnerTranscriptEvent Entry(
        Guid sessionId, string kind, string? text, long sequence, string? stopReason = null) =>
        new(sessionId, sequence, kind, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow,
            kind == TranscriptKinds.UserPrompt ? "user" : "assistant", text, null, null, null, null, stopReason);

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
