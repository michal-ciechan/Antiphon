using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using World = Antiphon.Tests.Application.CodexCliRemoteDeliveryFixture.World;
using InjectedFault = Antiphon.Tests.Application.CodexCliRemoteDeliveryFixture.InjectedFault;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[NotInParallel(["MessageQueue", "AgentQueue"])]
public sealed class CodexCliObservationGapTests
{
    [Test]
    public async Task C1029_Per_kind_receipts()
    {
        var failures = new List<Exception>();
        foreach (var remote in new[] { false, true })
        foreach (var busy in new[] { false, true })
        {
          try
          {
            await using var w = await World.CreateAsync(remote, busy);
            w.Recipient.Ready = false;
            w.Recipient.AckRules = false;
            await w.ProduceAsync(AgentKind.Grok);
            var starting = w.StartAsync();
            try
            {
                await WaitAsync(() => w.Recipient.SnapshotReads >= 2 || starting.IsCompleted);
                if (starting.IsCompleted) await starting;
                w.Recipient.Terminals.Values.SelectMany(t => t.Inputs).ShouldBeEmpty("C1029-pc-262 input waits for provider ready");
                w.Recipient.Ready = true;
                await WaitAsync(() => w.Recipient.Terminals.Values.SelectMany(t => t.SubmittedBodies).Any(b => b.StartsWith("[antiphon-grok-rules:", StringComparison.Ordinal)) || starting.IsCompleted);
                if (starting.IsCompleted) await starting;
                await using (var db = w.Db())
                    (await db.SessionQueuedMessages.CountAsync(q => q.SourceTaskId == w.TaskId && q.Origin == QueuedMessageOrigin.Delegation))
                        .ShouldBe(0, "C1029-pc-263 no task queue before matching rules ACK");
                var rules = w.Terminal.SubmittedBodies.Single(b => b.StartsWith("[antiphon-grok-rules:", StringComparison.Ordinal));
                w.Recipient.AppendRulesAck(w.SessionId, rules);
                w.Recipient.AckRules = true;
                await starting;
                if (busy) w.Terminal.SubmittedBodies.Count(b => b.Contains(DelegationReportFormatter.TaskMarker(w.TaskId), StringComparison.Ordinal))
                    .ShouldBe(0, "C1029 Grok busy task remains pending");
                await w.ReadyToFlushAsync(); await w.FlushAsync();
                using (var scope = w.H.Provider.CreateScope())
                {
                    var recovery = scope.ServiceProvider.GetRequiredService<GrokRulesRefreshService>();
                    await recovery.RecoverSessionAsync(w.SessionId, CancellationToken.None);
                    await recovery.RecoverSessionAsync(w.SessionId, CancellationToken.None);
                }
                await using (var db = w.Db())
                {
                    (await db.SessionQueuedMessages.CountAsync(q => q.SourceTaskId == w.TaskId && q.Origin == QueuedMessageOrigin.Delegation))
                        .ShouldBe(1, "C1029-pc-273 durable launch brief exists once");
                    var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == w.SessionId);
                    session.GrokRulesState.ShouldBe(GrokRulesState.Ready);
                    session.GrokRulesReadyAt.ShouldNotBeNull();
                }
                await w.ReadyToFlushAsync(); await w.FlushAsync();
                await w.ReadyToFlushAsync(); await w.FlushAsync();
                await w.AssertReceiptAsync($"C1029 Grok remote={remote} busy={busy}");
                w.Terminal.SubmittedBodies.Count(b => b.StartsWith("[antiphon-grok-rules:", StringComparison.Ordinal)).ShouldBe(1);
            }
            finally
            {
                w.Recipient.Ready = true; w.Recipient.AckRules = true;
                if (w.Recipient.Terminals.TryGetValue(w.SessionId, out var terminal) && w.Recipient.Rules.ContainsKey(w.SessionId))
                {
                    var prompt = terminal.SubmittedBodies.FirstOrDefault(b => b.StartsWith("[antiphon-grok-rules:", StringComparison.Ordinal));
                    if (prompt is not null) w.Recipient.AppendRulesAck(w.SessionId, prompt);
                }
                await starting;
            }
          }
          catch (Exception ex) { failures.Add(new Exception($"C1029 Grok remote={remote} busy={busy}", ex)); }
        }
        foreach (var busy in new[] { false, true })
        foreach (var spill in new[] { false, true })
        {
          try
          {
            await using var w = await World.CreateAsync(true, busy);
            await w.ProduceAsync(AgentKind.ClaudeCode, spill);
            await w.StartAsync();
            if (busy) w.Terminal.SubmittedBodies.ShouldBeEmpty("C1029 remote Claude busy");
            await w.ReadyToFlushAsync(); await w.FlushAsync();
            if (!spill) System.Text.Encoding.UTF8.GetByteCount(w.Full).ShouldBeLessThanOrEqualTo(w.Limits.BriefInlineMaxBytes,
                "C1029 Claude inline witness must fit the unchanged selected ceiling");
            await w.AssertReceiptAsync($"C1029 Claude spill={spill} busy={busy}", spill);
            var row = await w.RowAsync();
            var wire = w.Wire(row);
            var bodyIndex = w.Terminal.Inputs.ToList().FindIndex(i => i.Contains(wire, StringComparison.Ordinal));
            bodyIndex.ShouldBeGreaterThanOrEqualTo(0);
            w.Terminal.Inputs[bodyIndex + 1].ShouldBe("\r", "C1029 separate Enter");
            if (!spill) w.Terminal.Inputs[bodyIndex].ShouldBe("\x1b[200~" + wire + "\x1b[201~", "C1029 LF bracketed paste");
          }
          catch (Exception ex) { failures.Add(new Exception($"C1029 Claude spill={spill} busy={busy}", ex)); }
        }
        if (failures.Count != 0) throw new AggregateException("C1029 kind vector failures", failures);
    }

    [Test]
    public async Task C1029_Enqueue_fault_and_retry_keep_identity()
    {
        foreach (var remote in new[] { false, true })
        foreach (var after in new[] { false, true })
        {
            await using var w = await World.CreateAsync(remote);
            w.Fault.Point = after ? "after-insert" : "before-insert";
            await w.ProduceAsync();
            w.Fault.Fired.ShouldBeTrue("C1029 actual queue save seam reached");
            var originalSession = w.SessionId;
            await using (var db = w.Db())
            {
                (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == w.TaskId)).Status
                    .ShouldNotBe(AgentTaskStatus.Working, "C1029-pc-191 enqueue failure is not Working");
                (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == originalSession && t.Kind == TranscriptKinds.UserPrompt))
                    .ShouldBe(0, "C1029 enqueue fault is not receipt");
                (await db.SessionQueuedMessages.CountAsync(q => q.Id == w.Fault.QueueId)).ShouldBe(after ? 1 : 0,
                    "C1029 fresh un-intercepted committed-row read");
            }
            if (after)
            {
                var original = await w.RowAsync();
                await w.StartAsync();
                await w.ReadyToFlushAsync(); await w.FlushAsync();
                (await w.RowAsync()).Id.ShouldBe(original.Id, "C1029-pc-192 recover original committed queue");
                await w.AssertReceiptAsync("C1029 after committed enqueue");
            }
            else
            {
                // The real producer persisted its claim but could not persist a handoff. The
                // launch is intentionally lost; watchdog and explicit Retry own the new attempt.
                w.Clock.Advance(TimeSpan.FromMinutes(w.H.Delegation.DeliveryFailTimeoutMinutes + 1));
                await w.RefreshHeartbeatAsync();
                using (var scope = w.H.Provider.CreateScope())
                {
                    await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().FailNeverStartedAsync(CancellationToken.None);
                    var retry = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(w.TaskId, CancellationToken.None);
                    retry.Status.ShouldBe(AgentTaskStatus.Queued, "C1029-pc-192 real same-task Retry");
                }
                w.DiscardUnstartedLaunch();
                await w.DispatchAsync(); await w.StartAsync();
                w.SessionId.ShouldNotBe(originalSession, "C1029-pc-192 new recipient identity");
                await w.ReadyToFlushAsync(); await w.FlushAsync();
                await w.AssertReceiptAsync("C1029 Retry selected new recipient");
                await using var db = w.Db();
                (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == w.TaskId && e.Type == AgentTaskEventType.Retried)).ShouldBe(1);
                (await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == originalSession && e.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
            }
        }
    }

    [Test]
    public async Task C1029_Old_generation_does_not_confirm()
    {
        foreach (var remote in new[] { false, true })
        {
            await using var w = await World.CreateAsync(remote, busy: true);
            await w.ProduceAsync(); await w.StartAsync();
            await w.ReadyToFlushAsync(); await w.FlushAsync();
            await w.AssertReceiptAsync("C1029 real prior generation receipt");
            var priorSession = w.SessionId;
            var priorWire = w.Wire(await w.RowAsync());
            DateTime priorGeneration;
            await using (var priorDb = w.Db())
                priorGeneration = (await priorDb.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == priorSession)).StartedAt;
            w.Clock.Advance(TimeSpan.FromSeconds(1));
            using (var scope = w.H.Provider.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(w.TaskId, CancellationToken.None);
            w.AssertNoProbes("old-generation Retry");
            await w.DispatchAsync(); await w.StartAsync();
            w.SessionId.ShouldNotBe(priorSession, "C1029 real Retry selected a new generation");
            await using (var currentDb = w.Db())
            {
                (await currentDb.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == w.SessionId)).StartedAt.ShouldNotBe(priorGeneration);
                (await currentDb.TranscriptEntries.CountAsync(e => e.AgentSessionId == priorSession &&
                    e.Kind == TranscriptKinds.UserPrompt && e.Text == priorWire)).ShouldBe(1, "C1029 actual prior whole W remains stored");
            }
            // Both histories are real. Keep the previous record below the new floor;
            // the separate at-floor same-session adversary isolates the sequence guard.
            await w.ReadyToFlushAsync();
            string? held = null;
            w.Recipient.RecordPrompt = async (_, text) =>
            {
                held = text;
                var row = await w.RowAsync();
                var floor = row.LastDeliveryBaselineSequence.ShouldNotBeNull("C1029 original observable floor");
                await using var db = w.Db();
                var old = await db.TranscriptEntries.SingleAsync(e => e.AgentSessionId == w.SessionId && e.Sequence == floor);
                old.Kind = TranscriptKinds.UserPrompt; old.Text = held;
                await db.SaveChangesAsync();
            };
            await w.ReadyToFlushAsync(); await w.FlushAsync();
            held.ShouldBe(w.Wire(await w.RowAsync()));
            var retained = await w.RowAsync();
            retained.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed, "C1029-pc-223 old floor cannot confirm");
            if (remote) retained.RemoteSpillBody.ShouldBe(w.Full);
            w.Recipient.RecordPrompt = null;
            w.Recipient.Append(w.SessionId, TranscriptKinds.UserPrompt, held);
            await w.ReadyToFlushAsync(); await w.FlushAsync();
            w.Terminal.SubmittedBodies.Count.ShouldBe(1, "C1029 old-floor late receipt avoids duplicate input");
            await w.AssertReceiptAsync("C1029 old-floor then actual-current receipt");
            w.Recipient.Terminals[priorSession].SubmittedBodies.Count(b => b == priorWire).ShouldBe(1,
                "C1029 Retry never submits current work to the prior recipient");
        }
    }

    [Test]
    public async Task C1029_Unobservable_screen_retains_spill()
    {
        await using var w = await World.CreateAsync(true);
        await w.ProduceAsync();
        string? held = null;
        w.Recipient.WorkingScreen = true;
        w.Recipient.BeforeBody = async (_, input) =>
        {
            if (input == "\r") return;
            (await w.RowAsync()).LastDeliveryBaselineSequence.ShouldBeNull("C1029-pc-218 genuinely null baseline before body");
        };
        w.Recipient.RecordPrompt = (_, text) => { held = text; return Task.CompletedTask; };
        await w.StartAsync();
        var row = await w.RowAsync();
        row.LastDeliveryBaselineSequence.ShouldBeNull();
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered, "C1029 degraded Delivered path");
        row.RemoteSpillBody.ShouldBe(w.Full, "C1029-pc-218 screen verdict retains E");
        // Persisted incident is the durable evidence of the Screen branch; queue snapshots do
        // not preserve LastDelivery across a separate GetQueue call.
        await using (var db = w.Db())
            (await db.AgentIncidents.CountAsync(i => i.SessionId == w.SessionId && i.Kind == AgentIncidentKind.DeliveryUnverified)).ShouldBe(1);
        await ReleaseActualAsync(w, held!);
        (await w.RowAsync()).DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        await w.AssertReceiptAsync("C1029 null-baseline delayed actual prompt");
    }

    [Test]
    public async Task C1029_Unobservable_timestamp_floor_is_original()
    {
        var failures = new List<Exception>();
        foreach (var offset in new long?[] { -1, null, 0, 1 })
        {
          try
          {
            await using var w = await World.CreateAsync(true);
            await w.ProduceAsync();
            string? held = null;
            w.Recipient.RecordPrompt = (_, text) => { held = text; return Task.CompletedTask; };
            w.Recipient.WorkingScreen = true;
            await w.StartAsync();
            var original = await w.RowAsync();
            original.LastDeliveryBaselineSequence.ShouldBeNull();
            var start = original.LastDeliveryStartedAt.ShouldNotBeNull();
            var at = offset is null ? (DateTime?)null : start.AddSeconds(-30).AddTicks(offset.Value);
            // Only a negative adversary is synthetic. Equality/plus-one publish the actual
            // captured recipient submission with its source timestamp at the boundary.
            w.Recipient.AppendAt(w.SessionId, TranscriptKinds.UserPrompt, held, at is null ? null : new DateTimeOffset(at.Value, TimeSpan.Zero));
            w.Clock.Advance(TimeSpan.FromMinutes(1));
            await w.RecreateAsync(); await w.FlushAsync();
            var recovered = await w.RowAsync();
            recovered.LastDeliveryStartedAt.ShouldBe(start, "C1029-pc-246 original attempt floor survives recreation");
            if (offset is -1 or null)
            {
                recovered.RemoteSpillBody.ShouldBe(w.Full, "C1029-pc-246 old/null Timestamp cannot release E");
                recovered.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed);
                await ReleaseActualAsync(w, held!);
            }
            else recovered.RemoteSpillBody.ShouldBeNull("C1029 equality and plus-one source timestamp qualify");
            w.Terminal.SubmittedBodies.Count.ShouldBe(1);
            await w.AssertReceiptAsync("C1029 timestamp boundary " + offset);
          }
          catch (Exception ex) { failures.Add(new Exception("C1029 timestamp offset=" + (offset?.ToString() ?? "null"), ex)); }
        }
        if (failures.Count != 0) throw new AggregateException("C1029 timestamp vector failures", failures);
    }

    [Test]
    public async Task C1029_Durable_spill_survives_recreated_graph()
    {
        foreach (var busy in new[] { false, true })
        {
            // Hold startup delivery using real busy state in both variants. The selected
            // recipient becomes eligible before or after graph loss, respectively.
            await using var w = await World.CreateAsync(true, busy: true);
            await w.ProduceAsync(); await w.StartAsync();
            var original = await w.RowAsync();
            original.Status.ShouldBe(QueuedMessageStatus.Pending);
            original.RemoteSpillBody.ShouldBe(w.Full);
            w.Terminal.SubmittedBodies.ShouldBeEmpty();
            if (!busy) await w.ReadyToFlushAsync();
            await w.RecreateAsync();
            var recovered = await w.RowAsync();
            recovered.Id.ShouldBe(original.Id); recovered.RemoteSpillRelativePath.ShouldBe(original.RemoteSpillRelativePath);
            recovered.RemoteSpillBody.ShouldBe(w.Full); recovered.LastDeliveryGeneration.ShouldBe(original.LastDeliveryGeneration);
            recovered.LastDeliveryBaselineSequence.ShouldBe(original.LastDeliveryBaselineSequence);
            var path = w.SpillPath(recovered);
            if (File.Exists(path)) File.Delete(path);
            bool? filePresentAtInput = null;
            byte[]? bytesAtInput = null;
            w.Recipient.BeforeBody = async (id, input) =>
            {
                if (!input.Contains(DelegationReportFormatter.TaskMarker(w.TaskId), StringComparison.Ordinal)) return;
                id.ShouldBe(w.SessionId);
                filePresentAtInput = File.Exists(path);
                if (filePresentAtInput == true) bytesAtInput = await File.ReadAllBytesAsync(path);
            };
            if (busy) { await w.FlushAsync(); w.Terminal.SubmittedBodies.ShouldBeEmpty(); await w.ReadyToFlushAsync(); }
            await w.FlushAsync();
            filePresentAtInput.ShouldBe(true, "C1029-pc-219 durable lookup writes E before pointer input");
            bytesAtInput.ShouldBe(System.Text.Encoding.UTF8.GetBytes(w.Full));
            (await w.RowAsync()).Id.ShouldBe(original.Id);
            await w.AssertReceiptAsync("C1029-pc-219 fresh graph original queue");
        }
    }

    [Test]
    public async Task C1029_Post_input_crash_late_confirms_once()
    {
        foreach (var remote in new[] { false, true })
        foreach (var busy in new[] { false, true })
        {
            await using var w = await World.CreateAsync(remote, busy: true);
            await w.ProduceAsync(); await w.StartAsync();
            var original = await w.RowAsync();
            original.Status.ShouldBe(QueuedMessageStatus.Pending, $"C1029 crash setup remote={remote} busy={busy}");
            original.DeliveryAttempts.ShouldBe(0);
            w.Fault.Point = "after-input";
            await w.ReadyToFlushAsync();
            await Should.ThrowAsync<InjectedFault>(() => w.FlushAsync());
            w.Fault.Fired.ShouldBeTrue();
            w.Terminal.SubmittedBodies.Single().ShouldBe(w.Wire(original), "C1029-pc-220 actual terminal submit before loss");
            var attempted = await w.RowAsync();
            attempted.DeliveryAttempts.ShouldBe(1);
            if (remote) attempted.RemoteSpillBody.ShouldBe(w.Full);
            // Existing recovery admission is 3s confirmation + 3s grace + 30s tolerance.
            w.Clock.Advance(TimeSpan.FromSeconds(37));
            await w.RecreateAsync(); await w.FlushAsync();
            var settled = await w.RowAsync();
            settled.Id.ShouldBe(original.Id);
            settled.LastDeliveryStartedAt.ShouldBe(attempted.LastDeliveryStartedAt);
            settled.LastDeliveryBaselineSequence.ShouldBe(attempted.LastDeliveryBaselineSequence);
            settled.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed, "C1029-pc-220 actual transcript settles original attempt");
            await w.AssertReceiptAsync("C1029-pc-220 one actual submit across graphs");
        }
    }

    private static async Task ReleaseActualAsync(World w, string held)
    {
        held.ShouldBe(w.Wire(await w.RowAsync()));
        w.Recipient.RecordPrompt = null;
        w.Recipient.Append(w.SessionId, TranscriptKinds.UserPrompt, held);
        await w.ReadyToFlushAsync(); await w.FlushAsync();
    }
    private static async Task WaitAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition()) await Task.Delay(20, deadline.Token);
    }
}
