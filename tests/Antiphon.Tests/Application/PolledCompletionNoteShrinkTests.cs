using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class PolledCompletionNoteShrinkTests
{
    private const string Header = "[task completion done] CARD-0132 · Frontier";

    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    [Test]
    public async Task a_note_whose_content_the_caller_already_polled_is_shrunk()
    {
        await using var h = await CreateHarnessAsync();
        var report = "The implementation is complete and all targeted tests pass.";
        var seeded = await SeedAsync(h, report, DelegationNoteDigest.Compute(report));

        await FlushAsync(h);

        var typed = h.Adapter.SubmittedBodies.Single();
        typed.ShouldContain(Header);
        typed.ShouldContain("Report withheld");
        typed.ShouldNotContain(report);
        await using var db = CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == seeded.MessageId);
        row.Body.ShouldBe(typed);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId
            && e.Type == AgentTaskEventType.NoteShrunk)).ShouldBe(1);
    }

    [Test]
    public async Task a_note_whose_content_differs_from_the_poll_is_delivered_in_full()
    {
        await using var h = await CreateHarnessAsync();
        var report = "new report that the caller has not read";
        var seeded = await SeedAsync(h, report, DelegationNoteDigest.Compute("old report the caller read"));

        await FlushAsync(h);

        h.Adapter.SubmittedBodies.ShouldBe([seeded.Body]);
        await using var db = CreateContext();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.TaskId
            && e.Type == AgentTaskEventType.NoteShrunk)).ShouldBeFalse();
    }

    [Test]
    public async Task a_never_polled_task_delivers_its_note_in_full()
    {
        await using var h = await CreateHarnessAsync();
        var seeded = await SeedAsync(h, "a report with no authenticated status poll", null);

        await FlushAsync(h);

        h.Adapter.SubmittedBodies.ShouldBe([seeded.Body]);
        await using var db = CreateContext();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.TaskId
            && e.Type == AgentTaskEventType.NoteShrunk)).ShouldBeFalse();
    }

    [Test]
    public async Task a_note_already_typed_once_is_not_shrunk()
    {
        await using var h = await CreateHarnessAsync();
        var report = "a report protected by the late-confirm baseline";
        var seeded = await SeedAsync(h, report, DelegationNoteDigest.Compute(report), deliveryAttempts: 1);

        await FlushAsync(h);

        h.Adapter.SubmittedBodies.ShouldBe([seeded.Body]);
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.Id == seeded.MessageId)).Body.ShouldBe(seeded.Body);
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.TaskId
            && e.Type == AgentTaskEventType.NoteShrunk)).ShouldBeFalse();
    }

    [Test]
    public async Task a_shrunk_note_keeps_the_caller_facing_warning()
    {
        await using var h = await CreateHarnessAsync();
        var report = "a recovered report the caller already read";
        var warning = "WARNING: this task was recovered from an unbound session. Do not redispatch.";
        var seeded = await SeedAsync(h, report, DelegationNoteDigest.Compute(report),
            header: $"{Header}\n\n{warning}");

        await FlushAsync(h);

        h.Adapter.SubmittedBodies.Single().ShouldContain(warning);
    }

    [Test]
    public async Task an_excerpted_note_still_shrinks_on_a_matching_poll()
    {
        await using var h = await CreateHarnessAsync();
        var report = new string('x', new DelegationSettings().ReplyInlineMaxChars + 100);
        var task = NewTask(h, report, DelegationNoteDigest.Compute(report));
        var excerpt = DelegationReportFormatter.BuildCompletionNote(task, new DelegationSettings(), report);
        excerpt.Excerpted.ShouldBeTrue();
        var seeded = await SeedAsync(h, report, task.LastPolledResultHash,
            header: excerpt.Header, body: excerpt.Body, task: task);

        await FlushAsync(h);

        var typed = h.Adapter.SubmittedBodies.Single();
        typed.ShouldContain("Report withheld");
        typed.ShouldNotContain("THIS REPORT IS AN EXCERPT");
    }

    [Test]
    public async Task a_check_origin_row_is_untouched_by_the_delegation_guard()
    {
        await using var h = await CreateHarnessAsync();
        var report = "a check-origin row must not be shrunk";
        var seeded = await SeedAsync(h, report, DelegationNoteDigest.Compute(report),
            origin: QueuedMessageOrigin.Check);

        await FlushAsync(h);

        h.Adapter.SubmittedBodies.ShouldBe([seeded.Body]);
        await using var db = CreateContext();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.TaskId
            && e.Type == AgentTaskEventType.NoteShrunk)).ShouldBeFalse();
    }

    [Test]
    public async Task the_flag_off_delivers_in_full()
    {
        await using var h = await CreateHarnessAsync(shrinkPolledCompletionNotes: false);
        var report = "a matching report with the safety switch off";
        var seeded = await SeedAsync(h, report, DelegationNoteDigest.Compute(report));

        await FlushAsync(h);

        h.Adapter.SubmittedBodies.ShouldBe([seeded.Body]);
        await using var db = CreateContext();
        (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == seeded.TaskId
            && e.Type == AgentTaskEventType.NoteShrunk)).ShouldBeFalse();
    }

    private static Task<BridgeQueueHarness> CreateHarnessAsync(bool shrinkPolledCompletionNotes = true) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            Delegation = new DelegationSettings { ShrinkPolledCompletionNotes = shrinkPolledCompletionNotes },
        });

    private static async Task FlushAsync(BridgeQueueHarness h) =>
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

    private static AgentTask NewTask(BridgeQueueHarness h, string report, string? polledDigest) => new()
    {
        Id = Guid.NewGuid(),
        RootTaskId = Guid.NewGuid(),
        ParentSessionId = h.SessionId,
        AgentSessionId = h.SessionId,
        ReplyTo = AgentTaskReplyTo.Session,
        Title = "polled completion note shrink",
        Goal = "exercise the queue guard",
        Kind = AgentTaskKind.Worker,
        Role = AgentTaskRole.Code,
        ModelLevel = AgentModelLevel.Frontier,
        Workspace = WorkspaceMode.Shared,
        WorkingDirectory = h.TempRoot,
        Status = AgentTaskStatus.Succeeded,
        Result = report,
        CreatedAt = DateTime.UtcNow.AddMinutes(-2),
        DispatchedAt = DateTime.UtcNow.AddMinutes(-2),
        CompletedAt = DateTime.UtcNow.AddMinutes(-1),
        LastPolledResultHash = polledDigest,
        LastPolledResultAt = polledDigest is null ? null : DateTime.UtcNow,
    };

    private static async Task<Seeded> SeedAsync(
        BridgeQueueHarness h,
        string report,
        string? polledDigest,
        int deliveryAttempts = 0,
        QueuedMessageOrigin origin = QueuedMessageOrigin.Delegation,
        string? header = null,
        string? body = null,
        AgentTask? task = null)
    {
        task ??= NewTask(h, report, polledDigest);
        header ??= Header;
        body ??= $"{header}\n\n{report}";
        var messageId = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = messageId,
            AgentSessionId = h.SessionId,
            Body = body,
            Status = QueuedMessageStatus.Pending,
            Sequence = 1,
            Origin = origin,
            ConversationKey = origin == QueuedMessageOrigin.Check ? "not-a-task-check" : $"task:{task.Id:N}",
            SourceTaskId = task.Id,
            ContentDigest = DelegationNoteDigest.Compute(report),
            NoteHeader = header,
            CreatedAt = DateTime.UtcNow,
            DeliveryAttempts = deliveryAttempts,
            LastDeliveryStartedAt = deliveryAttempts == 0 ? null : DateTime.UtcNow.AddSeconds(-5),
        });
        await db.SaveChangesAsync();
        return new Seeded(task.Id, messageId, body);
    }

    private sealed record Seeded(Guid TaskId, Guid MessageId, string Body);
}
