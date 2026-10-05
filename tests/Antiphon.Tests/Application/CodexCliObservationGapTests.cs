using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.CodexCliRemoteDeliveryFixture;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CodexCliObservationGapTests
{
    private const string Body = "C959 delivery α\nsecond line\nEND-C959";

    [Test]
    public async Task C1029_Per_kind_receipts()
    {
        foreach (var remote in new[] { false, true })
        foreach (var busy in new[] { false, true })
        {
            await using var w = await World.CreateAsync(busy: busy, kind: AgentKind.Grok, remote: remote);
            w.Recipient.Ready = false;
            w.Recipient.Ack = false;
            var id = await w.CreateTaskAsync(Body);
            await w.DispatchAsync();
            var task = await TaskAsync(w, id);
            var session = task.AgentSessionId!.Value;
            var observed = ObserveFile(w, task);
            w.Launch();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try
            {
                try { await UntilAsync(() => w.Recipient.SnapshotReads >= 2, deadline.Token); }
                catch
                {
                    await using var diagnostic = w.Context();
                    var failed = await diagnostic.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == session);
                    Console.WriteLine($"C1029 GROK launch status={failed.Status} failure={failed.FailureReason} rules={failed.GrokRulesFailure}");
                    throw;
                }
                w.Recipient.Terminals[session].Inputs.ShouldBeEmpty("C1029-pc-262 no input before provider readiness");
                await w.FlushAsync(session);
                w.Recipient.Terminals[session].Inputs.ShouldBeEmpty("C1029-pc-262 queue also respects readiness");
                w.Recipient.Ready = true;
                await UntilAsync(() => w.Recipient.Terminals[session].SubmittedBodies.Count == 1, deadline.Token);
                w.Recipient.Terminals[session].SubmittedBodies[0].ShouldStartWith("[antiphon-grok-rules:");
                await using (var db = w.Context())
                {
                    (await db.SessionQueuedMessages.CountAsync(q => q.SourceTaskId == id || q.ExecutionTaskId == id))
                        .ShouldBe(0, "C1029-pc-263 no task handoff before rules ACK");
                    (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == session)).GrokRulesState.ShouldBe(GrokRulesState.Pending);
                }
                w.Recipient.ReleaseAcks();
                await w.JoinLaunchAsync();
                var row = await QueueAsync(w, id);
                if (busy)
                {
                    TaskBodies(w, session).ShouldBeEmpty("C1029 busy Grok holds the task separately from rules");
                    row.Status.ShouldBe(QueuedMessageStatus.Pending);
                    await w.EligibleAsync(session);
                    await w.FlushAsync(session);
                }
                await AssertReceiptAsync(w, task, observed, "grok/remote=" + remote + "/busy=" + busy);
                using var scope = w.Harness.Provider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<GrokRulesRefreshService>().RecoverSessionAsync(session, CancellationToken.None);
                (await QueueAsync(w, id)).Id.ShouldBe(row.Id, "C1029-pc-273 recovery keeps original durable task handoff");
                TaskBodies(w, session).Count.ShouldBe(1, "C1029-pc-273 no duplicate task submission");
            }
            finally { w.Recipient.Ready = true; w.Recipient.ReleaseAcks(); await w.JoinLaunchAsync(); }
        }
        foreach (var busy in new[] { false, true })
        foreach (var spill in new[] { false, true })
        {
            await using var w = await World.CreateAsync(busy: busy, kind: AgentKind.ClaudeCode);
            var id = await w.CreateTaskAsync(Body + (spill ? "\n" + new string('x', 4000) : ""));
            await w.DispatchAsync();
            var task = await TaskAsync(w, id);
            var observed = ObserveFile(w, task);
            Console.WriteLine($"C1029 CLAUDE requestedSpill={spill} EBytes={Encoding.UTF8.GetByteCount(w.Freeze.Full[id])} inlineCeiling={Limits(w, task).BriefInlineMaxBytes}");
            w.Launch(); await w.JoinLaunchAsync();
            if (busy)
            {
                TaskBodies(w, task.AgentSessionId!.Value).ShouldBeEmpty("C1029 busy Claude has no task submit");
                await w.EligibleAsync(task.AgentSessionId.Value); await w.FlushAsync(task.AgentSessionId.Value);
            }
            await AssertReceiptAsync(w, task, observed, "claude/busy=" + busy + "/spill=" + spill);
            (await QueueAsync(w, id)).RemoteSpillRelativePath.ShouldBe(spill ? TypedBodySpill.InboxRelativePath((await QueueAsync(w, id)).Id.ToString("D")) : null);
        }
    }

    [Test]
    public async Task C1029_Enqueue_fault_and_retry_keep_identity()
    {
        foreach (var remote in new[] { false, true })
        foreach (var after in new[] { false, true })
        {
            await using var w = await World.CreateAsync(busy: true, remote: remote);
            var id = await w.CreateTaskAsync(Body);
            w.Fault.TaskId = id; w.Fault.AfterSave = after;
            await w.DispatchAsync();
            w.Fault.Hits.ShouldBe(1, "C1029 exact queue-save seam reached");
            var original = await TaskAsync(w, id);
            var oldSession = original.AgentSessionId!.Value;
            original.Status.ShouldNotBe(AgentTaskStatus.Working, "C959-pc-191 enqueue is not receipt");
            if (after)
            {
                var row = await QueueAsync(w, id); // independent context proves commit before lost return
                var observed = ObserveFile(w, original);
                w.Launch(); await w.JoinLaunchAsync();
                await w.RecreateAsync(oldSession);
                (await QueueAsync(w, id)).Id.ShouldBe(row.Id, "C959-pc-192 original committed queue identity");
                await w.EligibleAsync(oldSession); await w.FlushAsync(oldSession);
                await AssertReceiptAsync(w, original, observed, "postinsert/remote=" + remote);
            }
            else
            {
                await using (var db = w.Context())
                {
                    (await db.SessionQueuedMessages.CountAsync(q => q.ExecutionTaskId == id)).ShouldBe(0, "C959-pc-191 no inserted queue");
                    (await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == oldSession && e.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
                }
                w.Launches.Discard(); // accepted but unlaunched process is demonstrably absent
                w.Clock.Advance(TimeSpan.FromMinutes(w.Harness.Delegation.DeliveryFailTimeoutMinutes + 1));
                using (var scope = w.Harness.Provider.CreateScope())
                {
                    (await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().FailNeverStartedAsync(CancellationToken.None)).ShouldBe(1);
                    (await TaskAsync(w, id)).Status.ShouldBe(AgentTaskStatus.Failed);
                    await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(id, CancellationToken.None);
                }
                await w.DispatchAsync();
                var retried = await TaskAsync(w, id);
                retried.Id.ShouldBe(original.Id);
                retried.Workspace.ShouldBe(WorkspaceMode.Worktree);
                retried.RunnerId.ShouldBe(original.RunnerId);
                retried.Attempt.ShouldBe(original.Attempt + 1);
                retried.Status.ShouldBe(AgentTaskStatus.Dispatched, "C959-pc-192 retry actually claimed; see C1029 EVENT diagnostics");
                retried.AgentSessionId.ShouldNotBeNull();
                retried.AgentSessionId.ShouldNotBe(oldSession, "C959-pc-192 retry targets a new session");
                var observed = ObserveFile(w, retried);
                w.Launch(); await w.JoinLaunchAsync();
                await w.EligibleAsync(retried.AgentSessionId!.Value); await w.FlushAsync(retried.AgentSessionId.Value);
                await AssertReceiptAsync(w, retried, observed, "preinsert-retry/remote=" + remote);
                await using var finalDb = w.Context();
                (await finalDb.AgentTaskEvents.CountAsync(e => e.AgentTaskId == id && e.Type == AgentTaskEventType.Retried)).ShouldBe(1);
                (await finalDb.TranscriptEntries.CountAsync(e => e.AgentSessionId == oldSession && e.Kind == TranscriptKinds.UserPrompt)).ShouldBe(0);
            }
        }
    }

    [Test]
    public async Task C1029_Old_generation_does_not_confirm()
    {
        foreach (var remote in new[] { false, true })
        {
            await using var w = await World.CreateAsync(busy: true, remote: remote);
            var id = await w.CreateTaskAsync(Body);
            await w.DispatchAsync();
            var task = await TaskAsync(w, id);
            var observed = ObserveFile(w, task);
            var held = HoldPrompts(w);
            var record = w.Recipient.RecordPrompt!;
            w.Recipient.RecordPrompt = async (sessionId, text) =>
            {
                await record(sessionId, text);
                var attempt = await QueueAsync(w, id);
                await using var db = w.Context();
                var old = await db.TranscriptEntries.SingleAsync(e => e.AgentSessionId == sessionId && e.Sequence == attempt.LastDeliveryBaselineSequence);
                old.Kind = TranscriptKinds.UserPrompt; old.Text = text;
                old.Timestamp = attempt.LastDeliveryStartedAt!.Value.AddMinutes(-5);
                await db.SaveChangesAsync();
            };
            w.Launch(); await w.JoinLaunchAsync();
            var session = task.AgentSessionId!.Value;
            await w.EligibleAsync(session); await w.FlushAsync(session);
            var attempted = await QueueAsync(w, id);
            var floor = attempted.LastDeliveryBaselineSequence.ShouldNotBeNull();
            held.ShouldHaveSingleItem();
            await w.RecreateAsync(session);
            var retained = await QueueAsync(w, id);
            retained.LastDeliveryBaselineSequence.ShouldBe(floor, "C959-pc-223 original floor survives recreation");
            retained.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed, "C959-pc-223 old floor is not a current receipt");
            await AssertRetainedAsync(w, task);
            await ReleaseAsync(w, session, held);
            await AssertReceiptAsync(w, task, observed, "old-floor/remote=" + remote);
        }
    }

    [Test]
    public async Task C1029_Unobservable_screen_retains_spill()
    {
        await using var w = await World.CreateAsync();
        var id = await w.CreateTaskAsync(Body);
        await w.DispatchAsync();
        var task = await TaskAsync(w, id);
        var observed = ObserveFile(w, task, requireNullFloor: true);
        var held = HoldPrompts(w);
        w.Launch(); await w.JoinLaunchAsync();
        var row = await QueueAsync(w, id);
        row.LastDeliveryBaselineSequence.ShouldBeNull("C959-pc-218 actually unobservable baseline");
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered, "C959-pc-218 actual screen fallback verdict");
        await using (var db = w.Context())
            (await db.AgentIncidents.CountAsync(i => i.SessionId == task.AgentSessionId && i.Kind == AgentIncidentKind.DeliveryUnverified))
                .ShouldBe(1, "C959-pc-218 persisted degraded delivery evidence");
        await AssertRetainedAsync(w, task);
        await ReleaseAsync(w, task.AgentSessionId!.Value, held);
        await AssertReceiptAsync(w, task, observed, "unobservable-screen");
    }

    [Test]
    public async Task C1029_Unobservable_timestamp_floor_is_original()
    {
        foreach (var variant in new[] { "older", "null", "equal", "newer" })
        {
            await using var w = await World.CreateAsync();
            var id = await w.CreateTaskAsync(Body);
            await w.DispatchAsync();
            var task = await TaskAsync(w, id);
            var observed = ObserveFile(w, task, requireNullFloor: true);
            var held = HoldPrompts(w, loseInputReply: true);
            w.Launch(); await w.JoinLaunchAsync();
            var row = await QueueAsync(w, id);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.LastDeliveryBaselineSequence.ShouldBeNull();
            var started = row.LastDeliveryStartedAt.ShouldNotBeNull();
            var floor = started.AddSeconds(-30);
            held.ShouldHaveSingleItem();
            // Retained recipient source timestamp differs from ingestion time. PostgreSQL's
            // microsecond precision makes one microsecond the representable boundary step.
            var stamp = variant switch { "older" => floor.AddTicks(-10), "newer" => floor.AddTicks(10), _ => floor };
            w.Recipient.Append(task.AgentSessionId!.Value, TranscriptKinds.UserPrompt, held[0].Text,
                timestamp: new DateTimeOffset(stamp, TimeSpan.Zero), nullTimestamp: variant == "null", sequence: 10000);
            w.Recipient.Append(task.AgentSessionId.Value, TranscriptKinds.TurnEnd, stopReason: "end_turn");
            w.Clock.Advance(TimeSpan.FromMinutes(2));
            await w.RecreateAsync(task.AgentSessionId.Value);
            (await QueueAsync(w, id)).LastDeliveryStartedAt.ShouldBe(started, "C959-pc-246 recreation cannot move attempt start");
            if (variant is "older" or "null")
            {
                // Late confirmation runs before the next attempt save. This fault stops that
                // later claim, so a correct negative cannot retype or overwrite the original floor.
                w.Fault.QueueId = row.Id; w.Fault.RejectRetry = true;
                await Should.ThrowAsync<InvalidOperationException>(() => w.FlushAsync(task.AgentSessionId.Value));
                w.Fault.Hits.ShouldBe(1, "C959-pc-246 stale timestamp did not falsely settle");
                await AssertRetainedAsync(w, task);
                (await QueueAsync(w, id)).LastDeliveryStartedAt.ShouldBe(started);
                await ReleaseAsync(w, task.AgentSessionId.Value, held);
            }
            else await w.FlushAsync(task.AgentSessionId.Value);
            await AssertReceiptAsync(w, task, observed, "timestamp/" + variant);
            (await QueueAsync(w, id)).LastDeliveryStartedAt.ShouldBe(started, "C959-pc-246 settled original floor");
        }
    }

    [Test]
    public async Task C1029_Durable_spill_survives_recreated_graph()
    {
        foreach (var busy in new[] { false, true })
        {
            // Hold the boot flush; the recovery recipient may become eligible before or after recreation.
            await using var w = await World.CreateAsync(busy: true);
            var id = await w.CreateTaskAsync(Body);
            await w.DispatchAsync();
            var task = await TaskAsync(w, id);
            var before = await QueueAsync(w, id);
            var observed = ObserveFile(w, task);
            w.Launch(); await w.JoinLaunchAsync();
            TaskBodies(w, task.AgentSessionId!.Value).ShouldBeEmpty();
            if (!busy) await w.EligibleAsync(task.AgentSessionId.Value);
            var path = SpillPath(w, task, before);
            if (File.Exists(path)) File.Delete(path);
            await w.RecreateAsync(task.AgentSessionId.Value);
            var recovered = await QueueAsync(w, id);
            recovered.Id.ShouldBe(before.Id, "C959-pc-219 original queue ID");
            recovered.RemoteSpillRelativePath.ShouldBe(before.RemoteSpillRelativePath);
            recovered.RemoteSpillBody.ShouldBe(w.Freeze.Full[id], "C959-pc-219 durable E after cache loss");
            recovered.LastDeliveryStartedAt.ShouldBe(before.LastDeliveryStartedAt);
            recovered.LastDeliveryGeneration.ShouldBe(before.LastDeliveryGeneration);
            recovered.LastDeliveryBaselineSequence.ShouldBe(before.LastDeliveryBaselineSequence);
            if (busy) await w.EligibleAsync(task.AgentSessionId.Value);
            await w.FlushAsync(task.AgentSessionId.Value);
            await AssertReceiptAsync(w, task, observed, "recreated/busy=" + busy);
        }
    }

    [Test]
    public async Task C1029_Post_input_crash_late_confirms_once()
    {
        foreach (var busyAfterRecreate in new[] { false, true })
        {
            await using var w = await World.CreateAsync(busy: true);
            var id = await w.CreateTaskAsync(Body);
            await w.DispatchAsync();
            var task = await TaskAsync(w, id);
            var row = await QueueAsync(w, id);
            var observed = ObserveFile(w, task);
            var held = new List<Submitted>();
            w.Recipient.RecordPrompt = (session, text) =>
            {
                var actual = new Submitted(session, text, DateTimeOffset.UtcNow);
                held.Add(actual);
                w.Recipient.Append(session, TranscriptKinds.UserPrompt, actual.Text, timestamp: actual.Timestamp);
                return Task.CompletedTask;
            };
            w.Launch(); await w.JoinLaunchAsync();
            w.Fault.QueueId = row.Id; w.Fault.Verdict = true;
            await w.EligibleAsync(task.AgentSessionId!.Value);
            await Should.ThrowAsync<InvalidOperationException>(() => w.FlushAsync(task.AgentSessionId.Value));
            w.Fault.Hits.ShouldBe(1);
            held.ShouldHaveSingleItem("C959-pc-220 actual terminal submitted before save loss");
            var interrupted = await QueueAsync(w, id);
            interrupted.Status.ShouldBe(QueuedMessageStatus.Sent);
            interrupted.DeliveryVerdict.ShouldBeNull("C959-pc-220 interrupted verdict did not persist");
            await AssertRetainedAsync(w, task);
            if (busyAfterRecreate) w.Recipient.Append(task.AgentSessionId.Value, TranscriptKinds.AssistantText, "actual recipient still working");
            w.Clock.Advance(TimeSpan.FromSeconds(37)); // Original interrupted-attempt age, not a changed timeout.
            await w.RecreateAsync(task.AgentSessionId.Value);
            (await QueueAsync(w, id)).LastDeliveryStartedAt.ShouldBe(interrupted.LastDeliveryStartedAt);
            // The actual recipient record survived the lost verdict save. Ingesting it
            // through the replacement graph must settle the original row without typing.
            await w.EligibleAsync(task.AgentSessionId.Value);
            await w.FlushAsync(task.AgentSessionId.Value);
            await AssertReceiptAsync(w, task, observed, "post-input/busy=" + busyAfterRecreate);
            var settled = await QueueAsync(w, id);
            settled.Id.ShouldBe(row.Id);
            settled.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed, "C959-pc-220 settled same interrupted row");
            TaskBodies(w, task.AgentSessionId.Value).Count.ShouldBe(1, "C959-pc-220 no duplicate submit");
        }
    }

    private static async Task UntilAsync(Func<bool> check, CancellationToken ct)
    { while (!check()) await Task.Delay(20, ct); }
    private static async Task<AgentTask> TaskAsync(World w, Guid id)
    { await using var db = w.Context(); return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id); }
    private static async Task<SessionQueuedMessage> QueueAsync(World w, Guid id)
    { await using var db = w.Context(); return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.ExecutionTaskId == id || q.SourceTaskId == id); }
    private static List<string> TaskBodies(World w, Guid session) => w.Recipient.Terminals[session].SubmittedBodies
        .Where(t => !t.StartsWith("[antiphon-grok-rules:", StringComparison.Ordinal)).ToList();
    private sealed record Submitted(Guid Session, string Text, DateTimeOffset Timestamp);
    private static List<Submitted> HoldPrompts(World w, bool loseInputReply = false)
    {
        var held = new List<Submitted>();
        w.Recipient.RecordPrompt = (id, text) => { held.Add(new(id, text, DateTimeOffset.UtcNow)); if (loseInputReply) throw new InvalidOperationException("C1029 lost reply after actual terminal submit"); return Task.CompletedTask; };
        return held;
    }
    private static async Task ReleaseAsync(World w, Guid session, List<Submitted> held)
    {
        held.ShouldHaveSingleItem();
        w.Recipient.RecordPrompt = null;
        foreach (var actual in held) w.Recipient.Append(actual.Session, TranscriptKinds.UserPrompt, actual.Text, timestamp: actual.Timestamp);
        await w.EligibleAsync(session);
        await w.FlushAsync(session);
    }
    private sealed class FileObservation { public bool? Exists; public byte[]? Bytes; }
    private static FileObservation ObserveFile(World w, AgentTask task, bool requireNullFloor = false)
    {
        var observed = new FileObservation();
        w.Recipient.BeforeBody = async (id, input) =>
        {
            if (!input.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal) || observed.Exists is not null) return;
            id.ShouldBe(task.AgentSessionId!.Value);
            var q = await QueueAsync(w, task.Id);
            if (requireNullFloor) q.LastDeliveryBaselineSequence.ShouldBeNull("C1029 null baseline committed before first body input");
            var path = SpillPath(w, task, q);
            if (path is null) return;
            observed.Exists = File.Exists(path);
            if (observed.Exists.Value) observed.Bytes = await File.ReadAllBytesAsync(path);
        };
        return observed;
    }
    private static PtyDeliveryCeilings Limits(World w, AgentTask task) => w.Harness.Delegation
        .CeilingsFor(PtyBackend.InboxConhost, "selected fixture delivery backend").ForAgentKind(task.AgentKind);
    private static bool MustSpill(World w, AgentTask task) =>
        Encoding.UTF8.GetByteCount(w.Freeze.Full[task.Id]) > Limits(w, task).BriefInlineMaxBytes;
    private static string? SpillPath(World w, AgentTask task, SessionQueuedMessage row) => !MustSpill(w, task) ? null : w.Remote
        ? Path.Combine(task.RemoteWorktreePath!, TypedBodySpill.InboxRelativePath(row.Id.ToString("D")))
        : Path.Combine(task.WorkingDirectory, ".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");
    private static string ExpectedWire(World w, AgentTask task, SessionQueuedMessage row)
    {
        var full = w.Freeze.Full[task.Id];
        if (!MustSpill(w, task)) return full.TrimEnd();
        var path = w.Remote ? TypedBodySpill.InboxRelativePath(row.Id.ToString("D")) : SpillPath(w, task, row)!;
        return DelegationReportFormatter.BuildBriefPointer(w.Freeze.Tasks[task.Id], w.Harness.Delegation,
            path, full.Length, task.AgentKind, maxWireBytes: w.Remote ? Limits(w, task).SingleWriteMaxBytes : null,
            boundSpillPath: w.Remote ? path : null).TrimEnd();
    }
    private static async Task AssertRetainedAsync(World w, AgentTask task)
    {
        var q = await QueueAsync(w, task.Id);
        q.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed);
        if (w.Remote) q.RemoteSpillBody.ShouldBe(w.Freeze.Full[task.Id], "C1029 no current complete receipt retains E");
        else (await File.ReadAllTextAsync(SpillPath(w, task, q)!)).ShouldBe(w.Freeze.Full[task.Id]);
    }
    private static async Task AssertReceiptAsync(World w, AgentTask task, FileObservation observed, string vector)
    {
        await w.Harness.Runtime.CatchUpTranscriptAsync(task.AgentSessionId!.Value, CancellationToken.None);
        var q = await QueueAsync(w, task.Id);
        var full = w.Freeze.Full[task.Id];
        full.ShouldContain(Body); full.ShouldNotContain("\r");
        var expected = ExpectedWire(w, task, q);
        var spilled = MustSpill(w, task);
        if (w.Remote) q.RemoteSpillRelativePath.ShouldBe(spilled ? TypedBodySpill.InboxRelativePath(q.Id.ToString("D")) : null, "C1029 independently selected spill path");
        Encoding.UTF8.GetByteCount(expected).ShouldBeLessThanOrEqualTo(Limits(w, task).SingleWriteMaxBytes, "C1029 unchanged write ceiling");
        if (spilled)
        {
            observed.Exists.ShouldBe(true, "C1029 file exists before pointer " + vector);
            observed.Bytes.ShouldBe(Encoding.UTF8.GetBytes(full), "C1029 exact E before pointer " + vector);
            if (task.AgentKind != AgentKind.ClaudeCode) { expected.ShouldNotContain("\n"); expected.ShouldNotContain("\r"); }
        }
        q.Body.ShouldBe(expected, "C1029 independent W " + vector);
        var terminal = w.Recipient.Terminals[task.AgentSessionId.Value];
        TaskBodies(w, task.AgentSessionId.Value).ShouldHaveSingleItem("C1029 one actual task submission " + vector).ShouldBe(expected);
        var write = terminal.Inputs.Single(i => i.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal));
        var index = terminal.Inputs.ToList().IndexOf(write);
        terminal.Inputs[index + 1].ShouldBe("\r", "C1029 separate Enter " + vector);
        write.ShouldNotContain("\r");
        if (expected.Contains('\n')) write.ShouldBe("\x1b[200~" + expected + "\x1b[201~", "C1029 LF bracketed paste");
        w.Recipient.ProbeRequests.ShouldBeEmpty();
        if (w.Remote) w.Peer.RequestCount(PhoneHomeOperation.Transcript).ShouldBeGreaterThan(0);
        await using var db = w.Context();
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == task.AgentSessionId);
        q.LastDeliveryGeneration.ShouldBe(SessionGeneration.Normalize(session.StartedAt));
        var receipts = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == task.AgentSessionId
            && e.Kind == TranscriptKinds.UserPrompt && e.Text == expected
            && e.Sequence > (q.LastDeliveryBaselineSequence ?? 0)).ToListAsync();
        if (q.LastDeliveryBaselineSequence is null)
        {
            var timestampFloor = q.LastDeliveryStartedAt.ShouldNotBeNull().AddSeconds(-30);
            receipts = receipts.Where(e => e.Timestamp >= timestampFloor).ToList();
        }
        receipts.ShouldHaveSingleItem("C1029 complete selected recipient W " + vector);
        q.Status.ShouldBe(QueuedMessageStatus.Sent);
        q.DeliveryVerdict.ShouldBeOneOf(DeliveryVerdict.Delivered, DeliveryVerdict.LateConfirmed);
        if (w.Remote) q.RemoteSpillBody.ShouldBeNull("C1029 receipt releases durable E " + vector);
        Console.WriteLine("C1029 VECTOR " + vector + $" task={task.Id} queue={q.Id} session={session.Id} receipt=complete submits=1");
    }
}
