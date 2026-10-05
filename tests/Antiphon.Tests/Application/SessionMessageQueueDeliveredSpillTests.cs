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
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
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
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
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
        await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
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
        await using (var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString))
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
                DeliveredSpillFixture.Identity(await f.RowAsync()).ShouldBe(DeliveredSpillFixture.Identity(f.Before), "partial-state-unchanged");
            }
            // A single production ingestion batch includes both partial and complete candidates.
            await f.PublishBatchAsync([f.Wire[..200], f.Wire]);
            var from = f.H.Now;
            await f.FlushAsync();
            await f.ReleasedAsync("later-complete-releases", from);
        }
        foreach (var length in new[] { 11, 0, 12 })
        {
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
            await f.SeedAsync(new string('w', length));
            await f.PublishAsync(f.Wire, TranscriptKinds.UserPrompt, f.H.Now);
            var from = f.H.Now;
            await f.FlushAsync();
            if (length < 12) await f.RetainedAsync("weak-wire-retains");
            else await f.ReleasedAsync("identifiable-wire-releases", from);
        }
        foreach (var form in new[] { "ansi-crlf", "elided", "framed" })
        {
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
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
        await using var local = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
        await using var foreign = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
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
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
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
        await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
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
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString, tolerance);
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
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
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
            await using var f = await DeliveredSpillFixture.CreateAsync(schema.ConnectionString);
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

}
