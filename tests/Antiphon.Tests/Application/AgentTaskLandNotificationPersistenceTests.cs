using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandNotificationPersistenceTests
{
    [Test]
    [Arguments("landed")]
    [Arguments("already-present")]
    [Arguments("residue-cleanup")]
    [Arguments("preoperation-refusal")]
    public async Task C467_V05_OutcomeObligationMatrix(string outcome)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        if (outcome != "already-present") await h.AddSourceAsync();
        var residue = Path.Combine(h.Fixture.Source, ".antiphon", "valuable.txt");
        if (outcome == "residue-cleanup")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(residue)!);
            await File.WriteAllTextAsync(residue, "owned residue");
        }
        if (outcome == "preoperation-refusal")
        {
            await using var db = h.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.WorktreeBranch = null;
            await db.SaveChangesAsync();
        }
        await h.RunAsync();
        await using var observer = h.CreateContext();
        var request = await observer.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.TaskId == h.Fixture.TaskId);
        request.IsPending.ShouldBeFalse();
        request.State.ShouldBe(LandRequestState.Completed);
        var note = await observer.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.RequestId == request.Id);
        note.SourceEventId.ShouldBe(request.TerminalEventId!.Value);
        note.State.ShouldBe(LandNotificationState.NotRequired);
        note.Body.ShouldContain(request.Id.ToString("N"));
        note.Body.ShouldContain(note.Id.ToString("N"));
        note.Body.ShouldContain("publication=");
        note.Body.ShouldContain("cleanup=");
        note.ConfirmedAt.ShouldBeNull();
        (await observer.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Fixture.TaskId)).LandRequestedAt.ShouldBeNull();
        var terminal = await observer.AgentTaskEvents.SingleAsync(e => e.Id == note.SourceEventId);
        terminal.IsLandTerminal.ShouldBeTrue();
        terminal.Type.ShouldBe(outcome switch {
            "already-present" => AgentTaskEventType.AlreadyPresent,
            "residue-cleanup" => AgentTaskEventType.LandedWithResidue,
            "preoperation-refusal" => AgentTaskEventType.LandRefused,
            _ => AgentTaskEventType.Landed,
        });
        if (outcome != "preoperation-refusal") await h.Fixture.AssertRemoteSourceAsync();
        await h.FailAsync(new IOException("lost acknowledgement"));
        (await observer.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(1);
        if (outcome == "residue-cleanup")
        {
            File.Delete(residue);
            await h.RepostAsync();
            await h.RunAsync();
            var notes = await observer.AgentTaskLandNotifications.AsNoTracking().Where(n => n.TaskId == h.Fixture.TaskId).ToListAsync();
            notes.Count.ShouldBe(2);
            notes.Select(n => n.RequestId).Distinct().Count().ShouldBe(2);
            notes.Select(n => n.ContentDigest).Distinct().Count().ShouldBe(2);
            notes.Select(n => n.LandingOperationId).Distinct().Count().ShouldBe(1);
            (await observer.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandingCleanup)).ShouldBe(1);
        }
    }
}
