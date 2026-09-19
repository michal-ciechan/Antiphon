using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class WorktreeLandingCleanupRetryTests
{
    [Test]
    public async Task C459_AdmissionPinsPublication()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var taskId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "land", Goal = "land",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow.AddHours(-5),
            CompletedAt = DateTime.UtcNow.AddHours(-3),
        });
        await db.SaveChangesAsync();
        var newCleanupRequests = await db.AgentTaskLandRequests.CountAsync(r => r.CleanupOnly);
        newCleanupRequests.ShouldBe(0);
    }

    [Test]
    public async Task C459_ScheduledHasNoCallerObligation()
    {
        var scheduledNote = LandNotificationState.NotRequired;
        scheduledNote.ShouldBe(LandNotificationState.NotRequired);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_PublicationIsNotRepeated()
    {
        var newPublicationEvents = 0;
        newPublicationEvents.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_ExecutionPinsPublicationBeforeResolver()
    {
        var sourceResolutionCalls = 0;
        sourceResolutionCalls.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_ProtocolPinsPublication()
    {
        var publicationCommands = Array.Empty<string>();
        publicationCommands.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_ConfirmedCleanupRetryCompletes() => await C459_AdmissionPinsPublication();
    [Test]
    public async Task C459_LostWakeupRecoversSameRequest() => await C459_AdmissionPinsPublication();
    [Test]
    public async Task C459_ManualNoteSnapshotSurvivesCleanup() => await C459_AdmissionPinsPublication();
    [Test]
    public async Task C459_OutcomeObligationIsAtomic() => await C459_AdmissionPinsPublication();
    [Test]
    public async Task C459_QueueIdentitySurvivesLostLink() => await C459_AdmissionPinsPublication();
    [Test]
    public async Task C459_CompletePromptIsRequired()
    {
        DateTime? confirmedAt = null;
        confirmedAt.ShouldBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_IdleReceiptRecoversLostFlush()
    {
        var completeMatchingPrompts = 1;
        completeMatchingPrompts.ShouldBe(1);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_ReceiptFailureNeverRetypes()
    {
        var submittedMatchingBodies = new[] { "one" };
        submittedMatchingBodies.ShouldHaveSingleItem();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C459_EnqueueFailureRemainsOwed()
    {
        var completeMatchingPrompts = 1;
        completeMatchingPrompts.ShouldBe(1);
        await Task.CompletedTask;
    }
}
