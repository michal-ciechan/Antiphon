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

/// <summary>
/// CARD-0604 D-3/D-4. What a REMOTE session's Completion obligation does with its spilled body.
///
/// D-3: a replayed Completion rendering takes the frozen wire text and skips SpillQueueBodyAsync
/// entirely — and that is the only place a remote body is staged for the Input frame. Every retry
/// of a spilled completion therefore typed the frozen pointer with nothing travelling behind it,
/// so the runner wrote no file and the agent was told to read one that was never created.
///
/// D-4: the frozen receipt derived its spill path from the DESKTOP cwd even for a session whose
/// file is written by the runner under <c>RunnerCwd</c>, so the receipt named a Windows path that
/// exists on neither machine.
/// </summary>
[Category("Integration")]
public sealed class RemoteCompletionSpillTests
{
    private const string RunnerCwd = "/work/worktrees/task-deadbeef";

    [Test]
    public async Task Replayed_completion_restages_the_body_for_the_runner()
    {
        var courier = new RemoteSpillCourier();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s => s.AddSingleton(courier),
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SetRunnerCwdAsync(db, h.SessionId, RunnerCwd);

        var logical = "the whole completion report\n" + new string('r', 4096);
        var (run, completions, wire) = await SeedFrozenRenderingAsync(db, h.SessionId, [logical]);

        await h.Queue.RestageCommittedSpillAsync(db, h.SessionId, run, completions, wire, CancellationToken.None);

        courier.TryPeek(h.SessionId, out var staged)
            .ShouldBeTrue("a replayed remote completion must carry its body to the runner again");
        staged.RunnerCwd.ShouldBe(RunnerCwd);
        staged.Spill.Body.ShouldBe(logical, "the body is the composed text, not the pointer that replaced it");
        staged.Spill.RelativePath.ShouldBe(TypedBodySpill.InboxRelativePath(run[0].Id.ToString("D")),
            "the retry must point at the same file the first attempt named");
    }

    /// <summary>
    /// CARD-0604 D-3b. The guard on the CALL SITE, not just the method. Every test above drives
    /// <see cref="SessionMessageQueueService.RestageCommittedSpillAsync"/> directly, so deleting
    /// the one line in the flush that invokes it leaves them all green while the reported defect
    /// is fully back: a remote retry types the frozen pointer and nothing travels behind it. This
    /// one runs the real flush end to end and asserts the body is staged for the runner.
    /// </summary>
    [Test]
    public async Task A_replayed_completion_flush_stages_the_body_behind_the_pointer_it_types()
    {
        var courier = new RemoteSpillCourier();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s => s.AddSingleton(courier),
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SetRunnerCwdAsync(db, h.SessionId, RunnerCwd);

        var logical = "the whole completion report\n" + new string('z', 4096);
        var (run, _, wire) = await SeedFrozenRenderingAsync(db, h.SessionId, [logical]);

        // Exactly what the first typed attempt left behind: the row rewritten to the POINTER, one
        // attempt charged, and the generation still the one that typed it — i.e. a redelivery of a
        // committed rendering, which is the only state that reaches the committedWire branch.
        var generation = SessionGeneration.Normalize(await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == h.SessionId).Select(s => s.StartedAt).SingleAsync());
        run[0].Body = wire;
        run[0].DeliveryAttempts = 1;
        run[0].LastDeliveryStartedAt = DateTime.UtcNow.AddMinutes(-4);
        run[0].LastDeliveryGeneration = generation;
        run[0].LastDeliveryBaselineSequence = 0;
        await db.SaveChangesAsync();

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldHaveSingleItem().ShouldBe(wire,
            "a frozen rendering is replayed verbatim: the retry types the same pointer, never a recomposed body");
        courier.TryPeek(h.SessionId, out var staged).ShouldBeTrue(
            "the flush that typed the pointer must also stage the body, or the runner writes no file "
            + "and the agent is told to read one that does not exist");
        staged.RunnerCwd.ShouldBe(RunnerCwd);
        staged.Spill.Body.ShouldBe(logical, "the body behind the pointer is the composed text");
        staged.Spill.RelativePath.ShouldBe(TypedBodySpill.InboxRelativePath(run[0].Id.ToString("D")),
            "the retry must point at the same file the first attempt named");
    }

    [Test]
    public async Task Replayed_completion_restages_the_whole_batch_body()
    {
        var courier = new RemoteSpillCourier();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s => s.AddSingleton(courier),
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SetRunnerCwdAsync(db, h.SessionId, RunnerCwd);

        var first = "earlier completion\n" + new string('s', 2048);
        var second = "later completion\n" + new string('t', 2048);
        var (run, completions, wire) = await SeedFrozenRenderingAsync(db, h.SessionId, [first, second]);

        await h.Queue.RestageCommittedSpillAsync(db, h.SessionId, run, completions, wire, CancellationToken.None);

        courier.TryPeek(h.SessionId, out var staged).ShouldBeTrue();
        // The batch is recomposed from the members' own frozen logical notes, in run order, which
        // is exactly what the first attempt composed and spilled.
        staged.Spill.Body.ShouldBe(ChannelPromptFormat.FormatBatch([first], second));
    }

    [Test]
    public async Task A_desktop_session_stages_nothing()
    {
        var courier = new RemoteSpillCourier();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s => s.AddSingleton(courier),
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        // No RunnerCwd: the desktop wrote the file itself on the first attempt and the path in the
        // pointer is one the session can actually open.
        var (run, completions, wire) = await SeedFrozenRenderingAsync(
            db, h.SessionId, ["the whole completion report\n" + new string('u', 4096)]);

        await h.Queue.RestageCommittedSpillAsync(db, h.SessionId, run, completions, wire, CancellationToken.None);

        courier.IsStaged(h.SessionId).ShouldBeFalse("a local session's body must never travel in an Input frame");
    }

    [Test]
    public async Task An_unspilled_rendering_stages_nothing()
    {
        var courier = new RemoteSpillCourier();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s => s.AddSingleton(courier),
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SetRunnerCwdAsync(db, h.SessionId, RunnerCwd);

        // Wire text == composed text: the note was typed inline, so there is no file behind it.
        var logical = "a short completion note";
        var (run, completions, _) = await SeedFrozenRenderingAsync(db, h.SessionId, [logical], wire: logical);

        await h.Queue.RestageCommittedSpillAsync(db, h.SessionId, run, completions, logical, CancellationToken.None);

        courier.IsStaged(h.SessionId).ShouldBeFalse("nothing was spilled, so nothing has to travel");
    }

    [Test]
    public async Task Frozen_receipt_names_the_path_the_runner_actually_writes()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SetRunnerCwdAsync(db, h.SessionId, RunnerCwd);

        var composed = "the whole completion report\n" + new string('v', 4096);
        var (run, completions, wire) = await SeedFrozenRenderingAsync(db, h.SessionId, [composed], freeze: false);

        await SessionMessageQueueService.FreezeCompletionRenderingAsync(
            db, h.SessionId, run, completions,
            new Dictionary<Guid, string> { [run[0].Id] = composed },
            composed, wire, spilled: true, DateTime.UtcNow, CancellationToken.None);

        var saved = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == completions[0].Id);
        var delivery = TaskCompletionNotification.TryReadDelivery(saved.CompletionDeliveryJson).ShouldNotBeNull();
        // The runner writes runnerCwd.TrimEnd('/') + "/" + relative (RunnerWorkspaceService).
        delivery.SpillPath.ShouldBe(RunnerCwd + "/" + TypedBodySpill.InboxRelativePath(run[0].Id.ToString("D")));
        delivery.SpillPath!.Contains('\\')
            .ShouldBeFalse("a POSIX runner path must never carry a Windows separator");
        delivery.SpillSha256.ShouldBe(TaskCompletionNotification.Sha256(composed));
    }

    [Test]
    public async Task Frozen_receipt_still_names_the_desktop_path_for_a_local_session()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var cwd = await db.AgentSessions.AsNoTracking().Where(s => s.Id == h.SessionId)
            .Select(s => s.Cwd).SingleAsync();
        cwd.ShouldNotBeNullOrWhiteSpace("the fixture session has a desktop cwd to derive from");

        var composed = "the whole completion report\n" + new string('w', 4096);
        var (run, completions, wire) = await SeedFrozenRenderingAsync(db, h.SessionId, [composed], freeze: false);

        await SessionMessageQueueService.FreezeCompletionRenderingAsync(
            db, h.SessionId, run, completions,
            new Dictionary<Guid, string> { [run[0].Id] = composed },
            composed, wire, spilled: true, DateTime.UtcNow, CancellationToken.None);

        var saved = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == completions[0].Id);
        var delivery = TaskCompletionNotification.TryReadDelivery(saved.CompletionDeliveryJson).ShouldNotBeNull();
        delivery.SpillPath.ShouldBe(TypedBodySpill.InboxAbsolutePath(cwd!, run[0].Id.ToString("D")));
    }

    [Test]
    public async Task A_null_body_batched_completion_retry_persists_and_sends_the_composed_batch()
    {
        var courier = new RemoteSpillCourier();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s => s.AddSingleton(courier),
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SetRunnerCwdAsync(db, h.SessionId, RunnerCwd);

        var first = "earlier completion note " + new string('s', 80);
        var second = "later completion note " + new string('t', 80);
        var composed = ChannelPromptFormat.FormatBatch([first], second);
        var (run, _, wire) = await SeedFrozenRenderingAsync(db, h.SessionId, [first, second]);
        await ArmBatchRetryAsync(db, h.SessionId, run);

        string? held = null;
        var record = h.Adapter.OnSubmitted;
        h.Adapter.OnSubmitted = async submitted =>
        {
            await using var live = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            held = (await live.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == run[0].Id))
                .RemoteSpillBody;
            submitted.ShouldContain(TypedBodySpill.InboxRelativePath(run[0].Id.ToString("D")));
            submitted.ShouldNotBe(first);
            if (record is not null)
                await record(submitted);
        };

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        held.ShouldBe(composed);
        courier.TryPeek(h.SessionId, out var staged).ShouldBeTrue();
        staged.Spill.Body.ShouldBe(composed);
        staged.Spill.RelativePath.ShouldBe(TypedBodySpill.InboxRelativePath(run[0].Id.ToString("D")));
    }

    [Test]
    public async Task A_batched_completion_whose_member_note_cannot_be_rebuilt_is_canceled()
    {
        var courier = new RemoteSpillCourier();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            ConfigureServices = s => s.AddSingleton(courier),
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await SetRunnerCwdAsync(db, h.SessionId, RunnerCwd);

        var first = "earlier completion note " + new string('a', 80);
        var second = "later completion note " + new string('b', 80);
        var (run, completions, _) = await SeedFrozenRenderingAsync(db, h.SessionId, [first, second]);
        completions[1].CompletionSnapshotJson = null;
        completions[1].CompletionDeliveryJson = null;
        await ArmBatchRetryAsync(db, h.SessionId, run);

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        courier.IsStaged(h.SessionId).ShouldBeFalse();
        await using var saved = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var rows = await saved.SessionQueuedMessages.AsNoTracking()
            .Where(m => run.Select(r => r.Id).Contains(m.Id))
            .ToListAsync();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(row => row.Status == QueuedMessageStatus.Canceled
            && row.DeliveryVerdict == DeliveryVerdict.SpillBodyMissing
            && row.RemoteSpillBody == null);
    }

    /// <summary>
    /// A pending retry of a frozen batch: both rows share a conversation key and one prior attempt,
    /// and the logical notes stay on the rows. The pointer lives only in the frozen wire.
    /// </summary>
    private static async Task ArmBatchRetryAsync(
        AppDbContext db, Guid sessionId, IReadOnlyList<SessionQueuedMessage> run)
    {
        var generation = SessionGeneration.Normalize(await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId).Select(s => s.StartedAt).SingleAsync());
        foreach (var row in run)
        {
            row.ConversationKey = "c647-null-batch";
            row.DeliveryAttempts = 1;
            row.LastDeliveryStartedAt = DateTime.UtcNow.AddMinutes(-4);
            row.LastDeliveryGeneration = generation;
            row.LastDeliveryBaselineSequence = 0;
            row.RemoteSpillBody = null;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Binds the fixture session to a runner. CK_AgentSessions_RunnerBinding_AllOrNone means the
    /// three columns move together, so a RunnerCwd on its own is not a state the database allows.
    /// </summary>
    private static async Task SetRunnerCwdAsync(AppDbContext db, Guid sessionId, string runnerCwd) =>
        await db.AgentSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RunnerId, "server2")
                .SetProperty(x => x.RunnerStoreId, Guid.NewGuid())
                .SetProperty(x => x.RunnerCwd, runnerCwd));

    /// <summary>
    /// The settlement snapshot every profiled Completion obligation carries, in the shape
    /// <c>AgentTaskReplyService</c> writes it: a Final profile-v1 session reply whose raw result is
    /// the logical note. Only its PRESENCE is load-bearing here — the committedWire branch replays
    /// the frozen delivery and never re-renders from the snapshot — but a row without one is a
    /// different kind of note entirely and the flush's query skips it.
    /// </summary>
    private static string SnapshotJson(Guid taskId, Guid settlementEventId, Guid sessionId, string raw) =>
        System.Text.Json.JsonSerializer.Serialize(
            new TaskCompletionNotification.Snapshot(
                TaskCompletionNotification.SnapshotVersion, taskId, taskId, settlementEventId, null,
                sessionId, AgentTaskStatus.Succeeded, raw, TaskCompletionNotification.Sha256(raw),
                TaskCompletionNotification.Sha256(raw), 1, VerificationRound.Final,
                VerificationScope.Full, null, null, null, false, null, null,
                "Delegate report", raw, null, null, null, null, Path.GetTempPath(), null,
                false, OutputDistillerMode.Shadow, null, null),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

    /// <summary>
    /// One queued row plus one TaskCompletion notification per logical note, with the rendering
    /// already frozen (unless <paramref name="freeze"/> is false) exactly as a first typed attempt
    /// would have committed it.
    /// </summary>
    private static async Task<(List<SessionQueuedMessage> Run, List<AgentTaskLandNotification> Completions, string Wire)>
        SeedFrozenRenderingAsync(
            AppDbContext db, Guid sessionId, IReadOnlyList<string> logicals, string? wire = null, bool freeze = true)
    {
        var now = DateTime.UtcNow;
        var run = new List<SessionQueuedMessage>();
        var completions = new List<AgentTaskLandNotification>();
        var sequence = await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId)
            .Select(m => (long?)m.Sequence).MaxAsync() ?? 0;

        foreach (var logical in logicals)
        {
            var taskId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "C604 completion", Goal = "spill fixture",
                WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = sessionId, CreatedAt = now,
            });
            // The notification's SourceEventId is a real foreign key onto the settlement event.
            var settlement = new AgentTaskEvent
            {
                // Detail is capped at 4000 chars and is not the rendering: the body lives on the
                // notification, which is what a Completion obligation actually carries.
                Id = Guid.NewGuid(), AgentTaskId = taskId, Type = AgentTaskEventType.Completed,
                At = now, Detail = "C604 settlement",
            };
            db.AgentTaskEvents.Add(settlement);
            var notification = new AgentTaskLandNotification
            {
                Id = Guid.NewGuid(), TaskId = taskId, SourceEventId = settlement.Id,
                Kind = LandNotificationKind.TaskCompletion, ReplyTo = AgentTaskReplyTo.Session,
                ParentSessionId = sessionId, Body = logical,
                ContentDigest = TaskCompletionNotification.Sha256(logical),
                CreatedAt = now, NextAttemptAt = now, State = LandNotificationState.AwaitingReceipt,
                // A profiled Completion obligation ALWAYS carries its settlement snapshot, and the
                // flush's own query is `Kind == TaskCompletion && CompletionSnapshotJson != null`
                // (TaskCompletionNotification.IsProfiled). A snapshot-less row is CARD-0527's
                // legacy note, which never reaches the committedWire branch at all.
                CompletionSnapshotJson = SnapshotJson(taskId, settlement.Id, sessionId, logical),
            };
            db.AgentTaskLandNotifications.Add(notification);
            completions.Add(notification);

            var row = new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Body = logical,
                Status = QueuedMessageStatus.Pending, Sequence = ++sequence,
                Origin = QueuedMessageOrigin.Delegation, CreatedAt = now,
                SourceTaskId = taskId, SourceLandNotificationId = notification.Id,
            };
            db.SessionQueuedMessages.Add(row);
            run.Add(row);
        }

        await db.SaveChangesAsync();

        var composed = logicals.Count == 1
            ? logicals[0]
            : ChannelPromptFormat.FormatBatch(logicals.Take(logicals.Count - 1).ToList(), logicals[^1]);
        // The wire text of a spilled rendering is the POINTER, never the body.
        var wireText = wire ?? ("Your brief did not fit in one write. It is at "
            + TypedBodySpill.InboxRelativePath(run[0].Id.ToString("D")));

        if (freeze)
        {
            var members = run.Select(m => m.Id).ToList();
            foreach (var notification in completions)
            {
                var row = run.Single(m => m.SourceLandNotificationId == notification.Id);
                notification.CompletionDeliveryJson = TaskCompletionNotification.SerializeDelivery(
                    new TaskCompletionNotification.Delivery(
                        TaskCompletionNotification.SnapshotVersion, "raw", row.Body, wireText,
                        TaskCompletionNotification.Sha256(wireText), members, null, null, now));
            }

            await db.SaveChangesAsync();
        }

        _ = composed;
        return (run, completions, wireText);
    }
}
