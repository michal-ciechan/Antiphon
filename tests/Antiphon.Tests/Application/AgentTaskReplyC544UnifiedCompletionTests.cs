using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskReplyIntegrationTests
{
    /// <summary>
    /// CARD-0544 D-9 unified onto CARD-0527's TaskCompletion kind. A Shared Code task that commits on
    /// settle is exactly the case both producers claim: profile-v1 mints the snapshot-bearing
    /// obligation and CARD-0527's legacy mint stands down; a legacy task keeps the snapshot-less row.
    /// Either way the settlement event owns ONE notification and the parent receives ONE note.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C544_shared_commit_on_settle_mints_one_task_completion(bool profiled)
    {
        var seeded = await SeedC527Async(t =>
        {
            t.Role = AgentTaskRole.Code;
            if (!profiled) return;
            t.VerificationProfileVersion = InterimVerificationPolicy.ProfileVersion;
            t.VerificationRound = VerificationRound.Final;
        }, "c544u");
        using var repo = seeded.Repo;
        var a = Path.Combine(repo.Path, "a.md");
        await File.WriteAllTextAsync(a, "a");
        await SeedFileEditAsync(seeded.SessionId, "Write", a, DateTime.UtcNow);
        var factory = C527Factory(repo.WorktreeRoot);
        AttachTerminal(factory, seeded.Parent);
        const string report = "Wrote a.md.";
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);

        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        await using var verify = CreateContext();
        var task = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == seeded.Task.Id);
        task.Status.ShouldBe(AgentTaskStatus.Succeeded, "the settlement committed (no unique-index collision)");
        (await repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain($"antiphon-task: {seeded.Task.Id}");
        var notification = (await verify.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => n.TaskId == seeded.Task.Id).ToListAsync()).ShouldHaveSingleItem();
        notification.Kind.ShouldBe(LandNotificationKind.TaskCompletion);
        TaskCompletionNotification.IsProfiled(notification).ShouldBe(profiled);
        var settlementEvent = await verify.AgentTaskEvents.AsNoTracking().SingleAsync(e => e.Id == notification.SourceEventId);
        settlementEvent.AgentTaskId.ShouldBe(seeded.Task.Id);
        settlementEvent.Type.ShouldBe(AgentTaskEventType.Completed);
        if (profiled)
            TaskCompletionNotification.TryReadSnapshot(notification.CompletionSnapshotJson)!
                .SourceEventId.ShouldBe(settlementEvent.Id);

        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Note.SourceLandNotificationId.ShouldBe(notification.Id);
        receipt.Prompt.Text.ShouldContain("git=committed:");
        (await AgentTaskCheckService.HasCompletionNoteAsync(verify, seeded.Parent, task.RootTaskId, CancellationToken.None))
            .ShouldBeTrue();
    }
}
