using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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

    private static async Task SetRunnerCwdAsync(AppDbContext db, Guid sessionId, string runnerCwd) =>
        await db.AgentSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RunnerCwd, runnerCwd));

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
            var notification = new AgentTaskLandNotification
            {
                Id = Guid.NewGuid(), TaskId = taskId, SourceEventId = Guid.NewGuid(),
                Kind = LandNotificationKind.TaskCompletion, ReplyTo = AgentTaskReplyTo.Session,
                ParentSessionId = sessionId, Body = logical,
                ContentDigest = TaskCompletionNotification.Sha256(logical),
                CreatedAt = now, NextAttemptAt = now, State = LandNotificationState.AwaitingReceipt,
                CompletionSnapshotJson = null,
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
