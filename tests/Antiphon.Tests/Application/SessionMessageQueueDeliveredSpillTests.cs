using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class SessionMessageQueueDeliveredSpillTests
{
    [Test]
    public async Task C1056_Screen_then_complete_prompt_releases_once()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var entry in new[] { "flush-session", "flush-if-idle", "turn-end" })
        foreach (var origin in new[] { QueuedMessageOrigin.Ui, QueuedMessageOrigin.Channel })
        {
            await using var f = await Fixture.CreateAsync(schema.ConnectionString);
            await f.ScreenAsync(origin);
            await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, f.H.Now);
            await f.RetainedAsync("ingestion-alone-retains");
            var transcriptCount = await f.TranscriptCountAsync();
            f.H.EventBus.Clear();
            var from = f.H.Now;
            var result = await f.FlushAsync(entry);
            await f.ReleasedAsync("release-complete / " + entry + "-releases", from);
            if (result is not null)
            {
                result.LateConfirmedMessageIds.ShouldContain(f.Before.Id, "turn-end-confirmed-ids");
                result.LateConfirmedChannelMessageIds.Contains(f.Before.Id)
                    .ShouldBe(origin == QueuedMessageOrigin.Channel, "turn-end-channel-confirmed-ids");
            }
            f.H.EventBus.PublishedEvents.Any(e => e.EventName == "SessionQueueChanged"
                && e.Payload is SessionQueueDto dto && dto.SessionId == f.H.SessionId)
                .ShouldBeTrue("receipt-queue-change-published");
            f.H.EventBus.PublishedEvents.ShouldNotContain(e => e.EventName == "SessionFinished",
                "receipt-no-finished-event");
            (await f.TranscriptCountAsync()).ShouldBe(transcriptCount, "receipt-no-synthetic-transcript");
            var releasedAt = (await f.RowAsync()).DeliveryVerdictAt;
            await f.FlushAsync("flush-if-idle");
            await f.FlushAsync("flush-session");
            (await f.RowAsync()).DeliveryVerdictAt.ShouldBe(releasedAt, "release-once");
            f.NoInput();
        }
        foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Grok })
        {
            await using var f = await Fixture.CreateAsync(schema.ConnectionString);
            await f.SetKindAsync(kind);
            await f.SeedAsync();
            await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, f.H.Now);
            var from = f.H.Now;
            await f.FlushAsync();
            await f.ReleasedAsync("provider-neutral-releases", from);
        }
    }

    [Test]
    public async Task C1056_Only_UserPrompt_can_release()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var f = await Fixture.CreateAsync(schema.ConnectionString);
        await f.ScreenAsync();
        foreach (var (kind, tool) in new[]
        {
            (TranscriptKinds.QueuedUserPrompt, (string?)null),
            (TranscriptKinds.AssistantText, (string?)null),
            (TranscriptKinds.ToolResult, "AskUserQuestion"),
            (TranscriptKinds.ToolResult, "completed-answer"),
            (TranscriptKinds.QueueEnqueue, (string?)null),
            (TranscriptKinds.QueueDequeue, (string?)null),
            (TranscriptKinds.QueueRemove, (string?)null),
        })
        {
            await f.PublishAsync(f.Wire, kind, f.H.Now, tool: tool);
            await f.FlushAsync();
            await f.RetainedAsync("non-user-retains");
        }
        f.H.Adapter.Emit(f.Wire);
        await f.FlushAsync();
        await f.RetainedAsync("non-user-retains");
        await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, f.H.Now);
        var from = f.H.Now;
        await f.FlushAsync();
        await f.ReleasedAsync("release-complete", from);
    }

    [Test]
    public async Task C1056_Complete_wire_is_required()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using (var f = await Fixture.CreateAsync(schema.ConnectionString))
        {
            await f.ScreenAsync();
            f.Wire.Length.ShouldBeGreaterThan(200);
            foreach (var partial in new string?[]
            {
                "[antiphon-task:c1056abc]", f.Wire[..200], f.Wire[^60..],
                f.Wire[..80] + f.Wire[^40..], f.Wire.TrimEnd()[..^1], "unrelated receipt", null, "",
            })
            {
                await f.PublishAsync(partial, TranscriptKinds.UserPrompt, f.H.Now);
                await f.FlushAsync();
                await f.RetainedAsync("partial-retains / partial-state-unchanged");
                Fixture.Identity(await f.RowAsync()).ShouldBe(Fixture.Identity(f.Before), "partial-state-unchanged");
            }
            // A single production ingestion batch includes both partial and complete candidates.
            await f.PublishBatchAsync([f.Wire[..200], f.Wire]);
            var from = f.H.Now;
            await f.FlushAsync();
            await f.ReleasedAsync("later-complete-releases", from);
        }
        foreach (var length in new[] { 11, 0, 12 })
        {
            await using var f = await Fixture.CreateAsync(schema.ConnectionString);
            await f.SeedAsync(new string('w', length));
            await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, f.H.Now);
            var from = f.H.Now;
            await f.FlushAsync();
            if (length < 12) await f.RetainedAsync("weak-wire-retains");
            else await f.ReleasedAsync("identifiable-wire-releases", from);
        }
        foreach (var form in new[] { "ansi-crlf", "elided", "framed" })
        {
            await using var f = await Fixture.CreateAsync(schema.ConnectionString);
            await f.ScreenAsync();
            var receipt = form switch
            {
                "ansi-crlf" => "\u001b[32m" + f.Wire.Replace("\n", "\r\n") + "\u001b[0m",
                "elided" => string.Concat(f.Wire.Where(c => !char.IsWhiteSpace(c))),
                _ => "provider framing\n" + f.Wire + "\nend framing",
            };
            await f.PublishAsync(receipt, TranscriptKinds.UserPrompt, f.H.Now);
            var from = f.H.Now;
            await f.FlushAsync();
            await f.ReleasedAsync("normalized-complete-releases", from);
        }
    }

    [Test]
    public async Task C1056_Receipt_session_must_match()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var local = await Fixture.CreateAsync(schema.ConnectionString);
        await using var foreign = await Fixture.CreateAsync(schema.ConnectionString);
        await local.ScreenAsync();
        await foreign.SeedAsync(local.Wire);
        await foreign.PublishAsync(local.Wire, TranscriptKinds.UserPrompt, foreign.H.Now);
        await local.FlushAsync();
        await local.RetainedAsync("foreign-receipt-retains");
        await foreign.RetainedAsync("foreign-obligation-untouched");
        await local.PublishAsync(local.Wire, TranscriptKinds.UserPrompt, local.H.Now);
        var from = local.H.Now;
        await local.FlushAsync();
        await local.ReleasedAsync("release-complete", from);
        await foreign.RetainedAsync("foreign-obligation-untouched");
    }

    [Test]
    public async Task C1056_Sequence_must_exceed_original_floor()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        const long baseline = 100;
        foreach (var sequence in new[] { baseline, baseline - 1, baseline + 1 })
        foreach (var time in new[] { "current", "null", "old" })
        {
            await using var f = await Fixture.CreateAsync(schema.ConnectionString);
            await f.SeedAsync(baseline: baseline);
            DateTime? timestamp = time == "null" ? null : time == "old" ? f.H.Now.AddDays(-1) : f.H.Now;
            var receipt = await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, timestamp, sequence);
            receipt.Sequence.ShouldBe(sequence, "stored-sequence-boundary");
            var from = f.H.Now;
            await f.FlushAsync();
            if (sequence <= baseline)
            {
                await f.RetainedAsync(sequence == baseline
                    ? "sequence-equal-retains / sequence-dominates-time" : "sequence-before-retains");
                await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, f.H.Now, baseline + 1);
                from = f.H.Now;
                await f.FlushAsync();
            }
            await f.ReleasedAsync("sequence-after-releases", from);
        }
    }

    [Test]
    public async Task C1056_Null_timestamp_cannot_release_unobservable_spill()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var f = await Fixture.CreateAsync(schema.ConnectionString);
        await f.ScreenAsync();
        f.Clock.Advance(TimeSpan.FromMinutes(2));
        var receipt = await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, null, 10000);
        receipt.Timestamp.ShouldBeNull();
        receipt.CreatedAt.ShouldBeGreaterThan(f.Before.LastDeliveryStartedAt!.Value);
        receipt.Sequence.ShouldBe(10000);
        await f.FlushAsync();
        await f.RetainedAsync("null-native-retains");
        await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, f.H.Now);
        var from = f.H.Now;
        await f.FlushAsync();
        await f.ReleasedAsync("release-complete", from);
    }

    [Test]
    public async Task C1056_Timestamp_uses_original_attempt_floor()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var tolerance in new[] { 0, 30, -30 })
        foreach (var laterGeneration in new[] { false, true })
        foreach (var delta in new[] { 0L, -10L, 10L })
        {
            await using var f = await Fixture.CreateAsync(schema.ConnectionString, tolerance);
            await f.ScreenAsync();
            var floor = f.Before.LastDeliveryStartedAt!.Value.AddSeconds(-Math.Max(0, tolerance));
            var timestamp = floor.AddTicks(delta);
            f.Clock.Advance(TimeSpan.FromMinutes(2));
            if (laterGeneration)
            {
                await using var db = f.Db();
                await db.AgentSessions.Where(s => s.Id == f.H.SessionId)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, f.H.Now));
            }
            await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, timestamp);
            var from = f.H.Now;
            await f.FlushAsync();
            if (delta < 0)
            {
                await f.RetainedAsync("timestamp-before-retains");
                await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, f.H.Now);
                from = f.H.Now;
                await f.FlushAsync();
            }
            else
            {
                var label = "original-floor-releases";
                if (delta == 0) label += " / timestamp-equal-releases";
                if (tolerance < 0) label += " / negative-tolerance-clamped";
                if (tolerance == 30)
                {
                    timestamp.ShouldBeLessThan(f.Before.LastDeliveryGeneration!.Value,
                        "the tolerance receipt really precedes the original generation");
                    label += " / tolerance-pre-generation-releases";
                }
                await f.ReleasedAsync(label, from);
            }
            await f.ReleasedAsync("release-complete", from);
        }
    }

    [Test]
    public async Task C1056_Unattempted_rows_are_not_reconciled()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var cases = new (string Label, Action<SessionQueuedMessage> Change)[]
        {
            ("zero-attempt-excluded", m => m.DeliveryAttempts = 0),
            ("non-sent-excluded", m => m.Status = QueuedMessageStatus.Pending),
            ("non-sent-excluded", m => m.Status = QueuedMessageStatus.Canceled),
            ("other-verdict-excluded", m => m.DeliveryVerdict = null),
            ("other-verdict-excluded", m => m.DeliveryVerdict = DeliveryVerdict.NoSubmitOutput),
            ("other-verdict-excluded", m => m.DeliveryVerdict = DeliveryVerdict.LateConfirmed),
            ("no-owned-spill-excluded", m => m.RemoteSpillBody = null),
            ("eligible-obligation-included", _ => { }),
        };
        foreach (var (label, change) in cases)
        {
            await using var f = await Fixture.CreateAsync(schema.ConnectionString);
            await f.SeedAsync(change: change);
            await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, f.H.Now);
            var eligible = label == "eligible-obligation-included";
            await using (var db = f.Db())
            {
                var ids = await db.SessionQueuedMessages.Where(QueueAttention.DeliveredSpillAwaitingReceipt)
                    .Select(m => m.Id).ToListAsync();
                ids.Contains(f.Before.Id).ShouldBe(eligible, label);
            }
            var from = f.H.Now;
            await f.FlushAsync(); // UserPrompt keeps ordinary Pending/interrupted input busy-gated.
            if (eligible) await f.ReleasedAsync("release-complete", from);
            else await f.RetainedAsync(label);
        }
    }

    [Test]
    public async Task C1056_Floorless_rows_are_not_reconciled()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        foreach (var floor in new[] { "none", "sequence", "time" })
        {
            await using var f = await Fixture.CreateAsync(schema.ConnectionString);
            await f.SeedAsync(change: m =>
            {
                // Synthetic historical obligation only: primary ScreenAsync never edits floors.
                m.LastDeliveryBaselineSequence = floor == "sequence" ? 0 : null;
                if (floor != "time") m.LastDeliveryStartedAt = null;
            });
            await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, f.H.Now);
            var from = f.H.Now;
            await f.FlushAsync();
            if (floor == "none") await f.RetainedAsync("floorless-retains");
            else await f.ReleasedAsync("single-floor-releases", from);
        }
    }

    private sealed class Fixture(BridgeQueueHarness harness, ScaledTimeProvider clock) : IAsyncDisposable
    {
        public BridgeQueueHarness H { get; } = harness;
        public ScaledTimeProvider Clock { get; } = clock;
        public SessionQueuedMessage Before { get; private set; } = null!;
        public string Wire => Before.Body;
        private readonly List<SessionRunnerTranscriptEvent> _entries = [];
        private readonly List<string> _captured = [];
        private string[] _inputs = [], _conditional = [], _prompts = [], _lifecycle = [], _submissions = [];

        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(H.ConnectionString));

        public static async Task<Fixture> CreateAsync(string connection, int tolerance = 30)
        {
            var clock = new ScaledTimeProvider(1);
            var h = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = connection,
                TimeProvider = clock,
                ConfigureServices = s => s.AddSingleton<RemoteSpillCourier>(),
                ConfigureDeliveryVerification = v => v.UnobservableBaselineConfirmClockToleranceSeconds = tolerance,
            });
            var f = new Fixture(h, clock);
            h.Adapter.OnSubmitted = text => { f._captured.Add(text); return Task.CompletedTask; };
            h.Adapter.SwallowSubmits = 0;
            h.Adapter.SubmitAck = "\n• Working (0s • esc to interrupt)";
            await using var db = f.Db();
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.AgentKind, AgentKind.Codex)
                .SetProperty(s => s.RunnerId, "c1056-fixture")
                .SetProperty(s => s.RunnerStoreId, Guid.NewGuid())
                .SetProperty(s => s.RunnerCwd, h.TempRoot));
            return f;
        }

        public async Task SetKindAsync(AgentKind kind)
        {
            await using var db = Db();
            await db.AgentSessions.Where(s => s.Id == H.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.AgentKind, kind));
        }

        public async Task ScreenAsync(QueuedMessageOrigin origin = QueuedMessageOrigin.Ui)
        {
            (await TranscriptCountAsync()).ShouldBe(0, "real-empty-transcript-before-input");
            const string relative = ".antiphon/task-c1056-brief.md";
            var body = "[antiphon-task:c1056abc] complete original spill " + new string('e', 3000);
            var pointer = "[antiphon-task:c1056abc] Read the full brief at " + relative
                + "\nPreserve the queue identity and the original attempt floor. The file contains the entire"
                + " task and its verification requirements; read it before making changes. Report the exact"
                + " checkpoint results and keep the full recipient submission evidence. End-of-wire-Z";
            H.Queue.StageRemoteSpill(H.SessionId, H.TempRoot, new PhoneHomeInputSpill(relative, body));
            await H.Queue.EnqueueAsync(H.SessionId, pointer, MessageSendMode.WhenIdle, CancellationToken.None, origin);
            await using var db = Db();
            Before = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == H.SessionId);
            _captured.ShouldHaveSingleItem("exactly-one-actual-submission");
            H.Adapter.SubmittedBodies.ShouldBe(_captured);
            Before.Body.ShouldBe(_captured.Single(), "actual-captured-wire");
            Before.Body.ShouldContain(Before.RemoteSpillRelativePath!);
            Before.LastDeliveryBaselineSequence.ShouldBeNull("honest-unobservable-baseline");
            Before.LastDeliveryStartedAt.ShouldNotBeNull();
            Before.LastDeliveryGeneration.ShouldNotBeNull();
            Before.DeliveryAttempts.ShouldBe(1);
            Before.Status.ShouldBe(QueuedMessageStatus.Sent);
            Before.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
            Before.RemoteSpillBody.ShouldBe(body, "screen-spill-retained");
            (await TranscriptCountAsync()).ShouldBe(0, "screen-has-no-receipt");
            SnapshotInput();
        }

        public async Task SeedAsync(string? body = null, long? baseline = null, Action<SessionQueuedMessage>? change = null)
        {
            var id = await H.SeedPendingMessageAsync(body ?? "historical-spill-wire-" + Guid.NewGuid(),
                deliveryAttempts: 1, baselineSequence: baseline, status: QueuedMessageStatus.Sent,
                deliveryVerdict: DeliveryVerdict.Delivered, lastDeliveryStartedAt: H.Now);
            await using (var db = Db())
            {
                var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
                row.RemoteSpillBody = "historical-owned-bytes-" + id;
                row.RemoteSpillRelativePath = TypedBodySpill.InboxRelativePath(id.ToString("D"));
                change?.Invoke(row);
                await db.SaveChangesAsync();
            }
            await using var fresh = Db();
            Before = await fresh.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
            SnapshotInput();
        }

        private void SnapshotInput()
        {
            _inputs = H.Adapter.Inputs.ToArray();
            _conditional = H.Adapter.ConditionalInputs.ToArray();
            _prompts = H.Adapter.Prompts.ToArray();
            _lifecycle = H.Adapter.Lifecycle.ToArray();
            _submissions = H.Adapter.SubmittedBodies.ToArray();
        }

        public void NoInput()
        {
            H.Adapter.Inputs.ShouldBe(_inputs, "receipt-zero-input");
            H.Adapter.ConditionalInputs.ToArray().ShouldBe(_conditional, "receipt-zero-conditional-input");
            H.Adapter.Prompts.ShouldBe(_prompts, "receipt-zero-prompts");
            H.Adapter.Lifecycle.ShouldBe(_lifecycle, "receipt-zero-lifecycle");
            H.Adapter.SubmittedBodies.ShouldBe(_submissions, "receipt-zero-submissions");
        }

        private SessionRunnerTranscriptEvent Entry(string? text, string kind, DateTime? timestamp,
            long? sequence = null, string? tool = null) => new(H.SessionId,
                sequence ?? (_entries.Count == 0 ? 1 : _entries.Max(e => e.Sequence) + 1),
                kind, Guid.NewGuid().ToString("N"), null,
                timestamp is { } time ? new DateTimeOffset(time) : null, "user", text,
                tool, tool == "AskUserQuestion" ? "{\"questions\":[]}" : null, null, false, null);

        public async Task<TranscriptEntry> PublishAsync(string? text, string kind, DateTime? timestamp,
            long? sequence = null, string? tool = null)
        {
            var entry = Entry(text, kind, timestamp, sequence, tool);
            _entries.Add(entry);
            H.Runner.SetTranscript(new(H.SessionId, _entries.ToArray(), _entries.Max(e => e.Sequence)));
            await H.Runtime.CatchUpTranscriptAsync(H.SessionId, CancellationToken.None);
            await using var db = Db();
            var saved = await db.TranscriptEntries.AsNoTracking().SingleAsync(e => e.AgentSessionId == H.SessionId
                && e.Uuid == entry.Uuid && e.Kind == kind);
            saved.Text.ShouldBe(text, "committed-actual-receipt-text");
            saved.Timestamp.ShouldBe(timestamp is { } t ? SessionGeneration.Normalize(t) : null,
                "committed-native-timestamp");
            return saved;
        }

        public async Task PublishBatchAsync(string[] texts)
        {
            foreach (var text in texts) _entries.Add(Entry(text, TranscriptKinds.UserPrompt, H.Now));
            H.Runner.SetTranscript(new(H.SessionId, _entries.ToArray(), _entries.Max(e => e.Sequence)));
            await H.Runtime.CatchUpTranscriptAsync(H.SessionId, CancellationToken.None);
            await using var db = Db();
            foreach (var entry in _entries.TakeLast(texts.Length))
                (await db.TranscriptEntries.AsNoTracking().SingleAsync(e => e.AgentSessionId == H.SessionId
                    && e.Uuid == entry.Uuid)).Text.ShouldBe(entry.Text, "batch-receipt-persisted");
        }

        public async Task<SessionQueueTurnEndResult?> FlushAsync(string entry = "flush-if-idle")
        {
            if (entry == "turn-end") return await H.Queue.OnTurnEndAsync(H.SessionId, CancellationToken.None);
            if (entry == "flush-session") await H.Queue.FlushSessionAsync(H.SessionId, CancellationToken.None);
            else await H.Queue.FlushIfIdleAsync(H.SessionId, CancellationToken.None);
            return null;
        }

        public async Task<SessionQueuedMessage> RowAsync()
        {
            await using var db = Db();
            return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == Before.Id);
        }

        public async Task<int> TranscriptCountAsync()
        {
            await using var db = Db();
            return await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == H.SessionId);
        }

        public static object?[] Identity(SessionQueuedMessage row) =>
        [row.Id, row.AgentSessionId, row.Body, row.RemoteSpillRelativePath, row.DeliveryAttempts,
            row.LastDeliveryStartedAt, row.LastDeliveryBaselineSequence, row.LastDeliveryGeneration,
            row.Status, row.SentAt, row.Sequence, row.CreatedAt, row.Origin];

        public async Task RetainedAsync(string label)
        {
            var row = await RowAsync();
            row.RemoteSpillBody.ShouldBe(Before.RemoteSpillBody, label);
            row.DeliveryVerdict.ShouldBe(Before.DeliveryVerdict, label);
            row.DeliveryVerdictAt.ShouldBe(Before.DeliveryVerdictAt, label);
            Identity(row).ShouldBe(Identity(Before), label);
            NoInput();
        }

        public async Task ReleasedAsync(string label, DateTime from)
        {
            var row = await RowAsync();
            row.RemoteSpillBody.ShouldBeNull(label);
            row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed, label);
            row.DeliveryVerdictAt.ShouldNotBeNull(label);
            row.DeliveryVerdictAt!.Value.ShouldBeGreaterThanOrEqualTo(SessionGeneration.Normalize(from), label);
            row.DeliveryVerdictAt.Value.ShouldBeLessThanOrEqualTo(H.Now, label);
            Identity(row).ShouldBe(Identity(Before), label);
            NoInput();
        }

        public ValueTask DisposeAsync() => H.DisposeAsync();
    }
}
