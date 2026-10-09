using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1165 (Review 47bfbbef F-3): the producer-to-recipient witness for the CARD-1156 standing
/// boot row. Every prompt record here is written by a REAL delivery: the real
/// <see cref="SessionMessageQueueService"/> of a <see cref="BridgeQueueHarness"/> attached to a
/// <see cref="StandingBootWatchFixture"/> session (taskless, AlwaysOn, isolated PostgreSQL, no seeded
/// prompt) types the body into the harness's <c>FakeAgentProtocolAdapter</c>, whose provider
/// stand-in records what a silent model's transcript holds: a <c>UserPrompt</c> when the recipient
/// is idle, a <c>QueuedUserPrompt</c> when it is mid-turn, and no reply. The standing row is then
/// read through the real <see cref="AttentionService.GetAsync"/> at fake read times measured from the
/// stored prompt, so its age, thresholds and qualifying-reply absence come from that transcript.
/// The queue runs on the system clock (a fake one would hang its delivery waits, CARD-0222).
/// </summary>
[Category("Integration")]
[NotInParallel]
public class StandingBootQueueWitnessTests
{
    /// <summary>Delivery verdicts the standing row never makes (it cannot verify delivery).</summary>
    private static readonly string[] DeliveryClaims =
        ["deliver", "not the problem", "received", "accepted", "reached the", "confirmed"];

    /// <summary>
    /// Real producer to real recipient, ending in exactly one complete matching <c>UserPrompt</c>,
    /// then the row built from that transcript.
    /// eligible: an idle recipient takes a WhenIdle enqueue inline.
    /// crash-after-enqueue: the row commits without typing, the process dies, and a restarted queue's
    /// stranded flush types it once.
    /// enqueue-failure: the first enqueue's save faults (no row, no input), the producer retries and
    /// the retry is the only delivery.
    /// busy-rules-turn: a Grok recipient is mid rules turn, delivered by the real queue and judged by
    /// the real <see cref="GrokRulesRefreshService"/>; the user body waits Pending, the rules turn ends,
    /// the turn-end flush types it, and the rules turn's model rows do not count as a reply
    /// (control: the same rows without the rules-turn marker resolve the episode).
    /// In every path the queued-only state before the prompt record shows no row, the row is Warning
    /// at the boot due and Error at the operator due with the stored prompt's sequence, time and age,
    /// and a later model reply resolves it.
    /// </summary>
    [Test]
    [Arguments("eligible")]
    [Arguments("crash-after-enqueue")]
    [Arguments("enqueue-failure")]
    [Arguments("busy-rules-turn")]
    public async Task C1165_Real_queue_prompt_reaches_the_standing_row(string path)
    {
        var grok = path == "busy-rules-turn";
        await using var f = await StandingBootWatchFixture.CreateAsync(new StandingBootWatchOptions
        {
            SeedPrompt = false,
            QueuedMessage = false,
            Arm = false,
            Kind = grok ? AgentKind.Grok : AgentKind.ClaudeCode,
        });
        var body = $"c1165 {path}: summarize the standing rules in one line.";
        var fault = new QueueInsertFault { Armed = path == "enqueue-failure" };
        Guid? rulesRowId = null;
        if (grok)
            rulesRowId = await StartRulesTurnAsync(f);

        var first = await OpenQueueAsync(f, fault, grok);
        var firstDisposed = false;
        try
        {
            var adapters = new List<FakeAdapterView> { new(first) };
            if (grok)
            {
                // The real queue types the launch rules prompt first; the model starts reading (no reply yet).
                await first.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
                first.Adapter.SubmittedBodies.ShouldHaveSingleItem("the rules prompt is typed first")
                    .ShouldStartWith(GrokRulesRefreshService.Header(rulesRowId!.Value));
                await AddModelRowAsync(f, TranscriptKinds.Thinking, "reading the standing rules file");
                (await f.WorkingAsync()).ShouldBeTrue("control: the recipient is busy on its rules turn");
            }

            switch (path)
            {
                case "eligible":
                    (await f.WorkingAsync()).ShouldBeFalse("control: the recipient is idle");
                    await first.Queue.EnqueueAsync(f.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);
                    break;
                case "crash-after-enqueue":
                    await first.Queue.EnqueueAsync(f.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
                        deliverIfIdle: false);
                    await AssertQueuedOnlyAsync(f, body, first, path);
                    break;
                case "enqueue-failure":
                    (await Should.ThrowAsync<InvalidOperationException>(() => first.Queue.EnqueueAsync(
                            f.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None)))
                        .Message.ShouldBe(QueueInsertFault.Message);
                    fault.Fired.ShouldBeTrue("control: the enqueue save actually faulted");
                    (await QueueRowsAsync(f)).ShouldBeEmpty("a failed enqueue commits no row");
                    (await PromptRecordsAsync(f)).ShouldBeEmpty("a failed enqueue types nothing");
                    (await StandingRowsAsync(f, DateTime.UtcNow.AddMinutes(30))).ShouldBeEmpty(
                        "no prompt record, no standing row");
                    await first.Queue.EnqueueAsync(f.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);
                    break;
                case "busy-rules-turn":
                    await first.Queue.EnqueueAsync(f.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);
                    await AssertQueuedOnlyAsync(f, body, first, path);
                    await AddRulesAckAsync(f, rulesRowId!.Value);
                    await first.Queue.OnTurnEndAsync(f.SessionId, CancellationToken.None);
                    break;
            }

            if (path == "crash-after-enqueue")
            {
                // The process dies after the enqueue committed and before anything was typed.
                await first.DisposeAsync();
                firstDisposed = true;
                await using var restarted = await OpenQueueAsync(f, null, grok: false);
                adapters.Add(new(restarted));
                (await restarted.Queue.FlushStrandedQueuesAsync(CancellationToken.None))
                    .ShouldBeGreaterThan(0, "the restarted queue's stranded flush delivers the committed row");
            }

            // Exactly one complete matching UserPrompt, typed once, by the real queue.
            var row = (await QueueRowsAsync(f)).Where(m => m.RulesRefreshKey == null)
                .ShouldHaveSingleItem($"{path}: one queue row for the body");
            row.Status.ShouldBe(QueuedMessageStatus.Sent, path);
            row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered, path);
            adapters.Sum(a => a.Count(body)).ShouldBe(1, $"{path}: the body is typed once across every process");
            var prompts = (await PromptRecordsAsync(f)).Where(t => t.Text?.StartsWith("[antiphon-grok-rules:") != true).ToList();
            var prompt = prompts.ShouldHaveSingleItem($"{path}: one prompt record for the body");
            prompt.Kind.ShouldBe(TranscriptKinds.UserPrompt, path);
            PromptSubmissionMatch.IsCompleteIn(body, prompt.Text).ShouldBeTrue($"{path}: complete matching UserPrompt");
            var promptAt = prompt.Timestamp ?? prompt.CreatedAt;

            long? rulesPrompt = null;
            if (grok)
            {
                var rules = (await QueueRowsAsync(f)).Single(m => m.Id == rulesRowId);
                rules.RulesAcknowledgedAt.ShouldNotBeNull("control: the real rules judge acknowledged the turn");
                rules.RulesTurnEndSequence.ShouldNotBeNull("control: the rules turn has its end");
                rulesPrompt = rules.RulesPromptSequence.ShouldNotBeNull("control: the rules turn has its prompt");
                (await f.SessionAsync()).GrokRulesState.ShouldBe(GrokRulesState.Ready, "control");
            }

            // The row the projection builds from this transcript, at the boot due and the operator due.
            var warning = (await StandingRowsAsync(f, promptAt.AddMinutes(8)))
                .ShouldHaveSingleItem($"{path}: Warning at the boot due");
            warning.Severity.ShouldBe(AlertSeverity.Warning, path);
            warning.SinceUtc.ShouldBe(promptAt, path);
            warning.Headline.ShouldBe("Standing boot stall detected: no model reply 8m00s after the prompt.", path);
            warning.Evidence.Split('\n').ShouldContain(
                $"Prompt #{prompt.Sequence} (UserPrompt) at {promptAt:u}, 8m00s ago; no qualifying model reply since.",
                warning.Evidence);
            var error = (await StandingRowsAsync(f, promptAt.AddMinutes(20)))
                .ShouldHaveSingleItem($"{path}: Error at the operator due");
            error.Severity.ShouldBe(AlertSeverity.Error, path);
            error.Headline.ShouldBe("Standing boot stall needs an operator decision: no model reply 20m00s after the prompt.", path);
            if (path == "eligible")
            {
                (await StandingRowsAsync(f, promptAt.AddMinutes(8).AddTicks(-10)))
                    .ShouldBeEmpty("one microsecond before the boot due no stage is due");
            }

            if (grok)
            {
                // Control: the same transcript without the rules-turn marker answers the prompt.
                await SetRulesPromptAsync(f, rulesRowId!.Value, null);
                (await StandingRowsAsync(f, promptAt.AddMinutes(8)))
                    .ShouldBeEmpty("control: unmarked, the rules turn's model rows are a qualifying reply");
                await SetRulesPromptAsync(f, rulesRowId.Value, rulesPrompt);
                (await StandingRowsAsync(f, promptAt.AddMinutes(8))).ShouldHaveSingleItem("control: marker restored");
            }

            await AddModelRowAsync(f, TranscriptKinds.AssistantText, "the standing rules say: report, do not recover");
            (await StandingRowsAsync(f, promptAt.AddMinutes(20)))
                .ShouldBeEmpty($"{path}: a model reply after the prompt resolves the episode");
            f.AssertNothingDestructive();
        }
        finally
        {
            if (!firstDisposed)
                await first.DisposeAsync();
        }
    }

    /// <summary>
    /// A busy recipient and the queued-only label. The real queue types prompt A into an idle
    /// standing session whose model stays silent, so the recipient is mid-turn. Body B, enqueued
    /// WhenIdle, waits Pending and untyped: the row still describes A. The operator then sends B now;
    /// the real queue types it into the busy turn, the provider records it only as a
    /// <c>QueuedUserPrompt</c> (no complete matching <c>UserPrompt</c> of B exists), and the queue
    /// row reads Delivered. The standing row is about B's queued record: it says "Queued prompt
    /// record", gives B's sequence, time and age, and makes no delivery claim either way.
    /// </summary>
    [Test]
    public async Task C1165_Prompt_typed_into_a_busy_turn_is_labelled_queued_not_delivered()
    {
        await using var f = await StandingBootWatchFixture.CreateAsync(new StandingBootWatchOptions
        {
            SeedPrompt = false, QueuedMessage = false, Arm = false,
        });
        await using var queue = await OpenQueueAsync(f, null, grok: false);
        const string a = "c1165 busy: first, summarize the standing rules.";
        const string b = "c1165 busy: then list what you would check next.";
        await queue.Queue.EnqueueAsync(f.SessionId, a, MessageSendMode.WhenIdle, CancellationToken.None);
        var first = (await PromptRecordsAsync(f)).ShouldHaveSingleItem("A is the one prompt record");
        first.Kind.ShouldBe(TranscriptKinds.UserPrompt);
        PromptSubmissionMatch.IsCompleteIn(a, first.Text).ShouldBeTrue("A arrived whole");
        (await f.WorkingAsync()).ShouldBeTrue("control: the silent model leaves the recipient mid-turn");

        await queue.Queue.EnqueueAsync(f.SessionId, b, MessageSendMode.WhenIdle, CancellationToken.None);
        var pending = (await QueueRowsAsync(f)).Single(m => m.Body == b);
        pending.Status.ShouldBe(QueuedMessageStatus.Pending, "a busy recipient holds B in the queue");
        pending.DeliveryAttempts.ShouldBe(0);
        queue.Adapter.SubmittedBodies.ShouldBe([a], "B is not typed into the busy turn");
        var aAt = first.Timestamp ?? first.CreatedAt;
        (await StandingRowsAsync(f, aAt.AddMinutes(8))).ShouldHaveSingleItem("the row is still A's").Evidence
            .Split('\n').ShouldContain($"Prompt #{first.Sequence} (UserPrompt) at {aAt:u}, 8m00s ago; no qualifying model reply since.");

        await queue.Queue.SendNowAsync(f.SessionId, pending.Id, CancellationToken.None);
        queue.Adapter.SubmittedBodies.ShouldBe([a, b], "send-now types B into the busy turn");
        var sent = (await QueueRowsAsync(f)).Single(m => m.Id == pending.Id);
        sent.Status.ShouldBe(QueuedMessageStatus.Sent);
        sent.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered, "the queue's own verdict accepts the queued record");
        var records = await PromptRecordsAsync(f);
        records.Count(t => t.Kind == TranscriptKinds.UserPrompt && PromptSubmissionMatch.IsCompleteIn(b, t.Text))
            .ShouldBe(0, "no complete matching UserPrompt of B");
        var queued = records.Where(t => t.Kind == TranscriptKinds.QueuedUserPrompt)
            .ShouldHaveSingleItem("B is recorded only as queued");
        queued.Text.ShouldBe(b);
        var bAt = queued.Timestamp ?? queued.CreatedAt;

        var row = (await StandingRowsAsync(f, bAt.AddMinutes(8))).ShouldHaveSingleItem("B's queued record is the latest prompt");
        row.Severity.ShouldBe(AlertSeverity.Warning);
        row.SinceUtc.ShouldBe(bAt);
        var lines = row.Evidence.Split('\n');
        lines.ShouldContain($"Queued prompt record #{queued.Sequence} at {bAt:u}, 8m00s ago; no reply observed.", row.Evidence);
        lines.ShouldNotContain(l => l.StartsWith("Prompt #", StringComparison.Ordinal), row.Evidence);
        foreach (var claim in DeliveryClaims)
        {
            (row.Headline + "\n" + row.Evidence).ShouldNotContain(claim, Case.Insensitive,
                "a queued record is labelled queued, never delivered or denied");
        }

        f.AssertNothingDestructive();
    }

    private static async Task SetRulesPromptAsync(StandingBootWatchFixture f, Guid rulesRowId, long? sequence)
    {
        await using var db = f.Read();
        (await db.SessionQueuedMessages.Where(m => m.Id == rulesRowId)
                .ExecuteUpdateAsync(u => u.SetProperty(m => m.RulesPromptSequence, sequence)))
            .ShouldBe(1);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private sealed class FakeAdapterView(BridgeQueueHarness harness)
    {
        private readonly IReadOnlyList<string> _bodies = harness.Adapter.SubmittedBodies;

        public int Count(string body) => _bodies.Count(b => b == body);
    }

    /// <summary>
    /// A queue on the fixture's database and session. Its fake provider records a silent model: the
    /// typed body as a <c>UserPrompt</c> (a <c>QueuedUserPrompt</c> when the recipient is mid-turn,
    /// as Claude records input typed into a busy turn), stamped now, and nothing after it.
    /// </summary>
    private static async Task<BridgeQueueHarness> OpenQueueAsync(
        StandingBootWatchFixture f, IInterceptor? fault, bool grok)
    {
        var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            ConnectionString = f.ConnectionString,
            PreserveDatabaseOnDispose = true,
            AttachSessionId = f.SessionId,
            AttachAgentId = f.AgentId,
            ConfigureDbContext = fault is null ? null : o => o.AddInterceptors(fault),
            ConfigureServices = grok
                ? services =>
                {
                    services.AddSingleton(Options.Create(new GrokRulesSettings()));
                    services.AddSingleton<GrokRulesRefreshService>();
                }
                : null,
        });
        harness.Adapter.OnSubmitted = async submitted =>
        {
            var busy = await f.WorkingAsync();
            var at = DateTime.UtcNow;
            await BridgeQueueHarness.InsertEntryAsync(f.SessionId,
                busy ? TranscriptKinds.QueuedUserPrompt : TranscriptKinds.UserPrompt, submitted,
                timestamp: at, connectionString: f.ConnectionString, createdAtUtc: at);
        };
        return harness;
    }

    /// <summary>
    /// Before any prompt record exists the body is only a Pending queue row: nothing typed, no
    /// transcript record of it, and no standing row at any read time.
    /// </summary>
    private static async Task AssertQueuedOnlyAsync(
        StandingBootWatchFixture f, string body, BridgeQueueHarness queue, string path)
    {
        var pending = (await QueueRowsAsync(f)).Where(m => m.RulesRefreshKey == null)
            .ShouldHaveSingleItem($"{path}: the body is queued");
        pending.Status.ShouldBe(QueuedMessageStatus.Pending, path);
        pending.DeliveryAttempts.ShouldBe(0, $"{path}: not typed yet");
        queue.Adapter.SubmittedBodies.ShouldNotContain(body, $"{path}: not typed yet");
        (await PromptRecordsAsync(f)).ShouldNotContain(t => t.Text == body, $"{path}: no prompt record of the body");
        (await StandingRowsAsync(f, DateTime.UtcNow.AddMinutes(30))).ShouldBeEmpty(
            $"{path}: a queued-only body is no prompt record, so no standing row");
    }

    /// <summary>
    /// Arms the real rules transport on the fixture's Grok session: a receipt, Pending state and the
    /// launch rules row the real <see cref="GrokRulesRefreshService"/> judges (Pending, never typed).
    /// </summary>
    private static async Task<Guid> StartRulesTurnAsync(StandingBootWatchFixture f)
    {
        var payload = new GrokRulesPayload("standing rules for the c1165 witness\n", 1, Guid.NewGuid());
        var bytes = Encoding.UTF8.GetBytes(payload.Content);
        var receipt = new GrokRulesReceipt($"C:\\remote\\instructions\\grok\\{f.SessionId:N}\\rules.md",
            GrokRulesTransport.Hash(bytes), bytes.Length, 1, payload.Generation);
        await using var db = f.Read();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId);
        session.GrokRulesGeneration = receipt.Generation;
        session.GrokRulesExpectedSha256 = receipt.Sha256;
        session.GrokRulesExpectedByteCount = receipt.ByteCount;
        session.GrokRulesReceiptJson = JsonSerializer.Serialize(receipt);
        session.GrokRulesState = GrokRulesState.Pending;
        var id = Guid.NewGuid();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = id, AgentSessionId = f.SessionId, Sequence = 1, Origin = QueuedMessageOrigin.System,
            CreatedAt = DateTime.UtcNow, Body = GrokRulesRefreshService.Prompt(id, receipt),
            RulesRefreshKey = $"launch:{receipt.Generation:N}", RulesReceiptJson = session.GrokRulesReceiptJson,
            RulesChainId = id, Status = QueuedMessageStatus.Pending,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>The model's rules-turn answer: the standalone acknowledgement line, then its turn end.</summary>
    private static async Task AddRulesAckAsync(StandingBootWatchFixture f, Guid rulesRowId)
    {
        await using var db = f.Read();
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == f.SessionId);
        var receipt = GrokRulesRefreshService.Receipt(session)!;
        await AddModelRowAsync(f, TranscriptKinds.AssistantText,
            $"ANTIPHON_RULES_ACK id={rulesRowId:N} generation={receipt.Generation:N} sha256={receipt.Sha256}");
        var at = DateTime.UtcNow;
        await BridgeQueueHarness.InsertEntryAsync(f.SessionId, TranscriptKinds.TurnEnd, stopReason: "end_turn",
            timestamp: at, connectionString: f.ConnectionString, createdAtUtc: at);
    }

    private static Task AddModelRowAsync(StandingBootWatchFixture f, string kind, string text)
    {
        var at = DateTime.UtcNow;
        return BridgeQueueHarness.InsertEntryAsync(f.SessionId, kind, text,
            timestamp: at, connectionString: f.ConnectionString, createdAtUtc: at);
    }

    private static async Task<List<SessionQueuedMessage>> QueueRowsAsync(StandingBootWatchFixture f)
    {
        await using var db = f.Read();
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == f.SessionId).OrderBy(m => m.Sequence).ToListAsync();
    }

    private static async Task<List<TranscriptEntry>> PromptRecordsAsync(StandingBootWatchFixture f)
    {
        await using var db = f.Read();
        return await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == f.SessionId
                && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.QueuedUserPrompt))
            .OrderBy(t => t.Sequence).ToListAsync();
    }

    /// <summary>
    /// The real <see cref="AttentionService.GetAsync"/> read at <paramref name="at"/> on the fixture's
    /// database: the standing rows about the fixture's session. Asserts the read staged nothing.
    /// </summary>
    private static async Task<List<AttentionItemDto>> StandingRowsAsync(StandingBootWatchFixture f, DateTime at)
    {
        await using var db = f.Read();
        var result = await AttentionServiceTests.BuildService(
                f.Runner, timeProvider: new FakeTimeProvider(new DateTimeOffset(at, TimeSpan.Zero)),
                db: db, logger: f.Logger<AttentionService>())
            .GetAsync(CancellationToken.None);
        db.ChangeTracker.HasChanges().ShouldBeFalse("nothing is staged during a GET");
        return result.Items
            .Where(i => i.Kind == AttentionKind.LivenessProbeFailed && i.SessionId == f.SessionId)
            .ToList();
    }

    /// <summary>One-shot fault on the first save that adds a queue row: the producer's enqueue fails.</summary>
    private sealed class QueueInsertFault : SaveChangesInterceptor
    {
        public const string Message = "c1165: injected enqueue save fault";

        public bool Armed { get; init; }
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && !Fired && eventData.Context is { } context
                && context.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Added))
            {
                Fired = true;
                throw new InvalidOperationException(Message);
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
