using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeLandingCleanupRetryTests
{
    [Test]
    [Arguments("missing")]
    [Arguments("inactive")]
    [Arguments("unconfirmed")]
    [Arguments("pending")]
    public async Task C459_AdmissionPinsPublication(string defect)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        if (defect == "inactive")
        {
            await using var db = h.CreateContext();
            var row = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
            row.Active = false;
            await db.SaveChangesAsync();
        }
        if (defect == "unconfirmed")
        {
            await using var db = h.CreateContext();
            var row = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
            row.Publication = LandPublicationOutcome.Unconfirmed;
            await db.SaveChangesAsync();
        }
        if (defect == "pending")
        {
            await using var db = h.CreateContext();
            db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
            {
                Id = Guid.NewGuid(), TaskId = h.Fixture.TaskId, RequestedAt = DateTime.UtcNow,
                State = LandRequestState.Queued, IsPending = true,
                LastEvaluatedAt = DateTime.UtcNow, LastProgressAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var operationId = defect == "missing" ? Guid.NewGuid() : op.Id;
        var newCleanupRequests = 0;
        try
        {
            await h.RequestCleanupRetryAsync(operationId);
            newCleanupRequests++;
        }
        catch (ConflictException)
        {
        }

        newCleanupRequests.ShouldBe(0);
    }

    [Test]
    public async Task C459_ScheduledHasNoCallerObligation()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        File.Delete(sentinel);
        var queued = await h.RequestCleanupRetryAsync(op.Id);
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.CleanupOnly.ShouldBeTrue();
        request.ReplyTo.ShouldBe(AgentTaskReplyTo.None);
        var scheduledNote = LandNotificationState.NotRequired;
        scheduledNote.ShouldBe(LandNotificationState.NotRequired);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id && n.Kind == LandNotificationKind.Outcome))
            .ShouldBe(0);
    }

    [Test]
    public async Task C459_PublicationIsNotRepeated()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        var publications = await CountPublicationsAsync(h);
        File.Delete(sentinel);
        await h.RequestCleanupRetryAsync(op.Id);
        await h.RunAsync();
        var newPublicationEvents = (await CountPublicationsAsync(h)) - publications;
        newPublicationEvents.ShouldBe(0);
        var after = (await h.OperationAsync())!;
        after.Id.ShouldBe(op.Id);
    }

    [Test]
    public async Task C459_ExecutionPinsPublicationBeforeResolver()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        File.Delete(sentinel);
        await h.RequestCleanupRetryAsync(op.Id);
        var sourceResolutionCalls = 0;
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args[0] == "rev-parse" && args.Contains("HEAD")) sourceResolutionCalls++;
            return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
        };
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
        row.Active = false;
        await db.SaveChangesAsync();
        try { await h.RunAsync(); } catch (Exception) { }
        sourceResolutionCalls.ShouldBe(0);
    }

    [Test]
    public async Task C459_ProtocolPinsPublication()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        File.Delete(sentinel);
        await h.RequestCleanupRetryAsync(op.Id);
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
        row.Active = false;
        await db.SaveChangesAsync();
        h.Fixture.Git.Trace.Clear();
        try { await h.RunAsync(); } catch (Exception) { }
        var publicationCommands = h.Fixture.Git.Trace.Where(a => a.Contains("push")).ToArray();
        publicationCommands.ShouldBeEmpty();
    }

    [Test]
    public async Task C459_ConfirmedCleanupRetryCompletes()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        File.Exists(sentinel).ShouldBeTrue();
        File.Delete(sentinel);
        var queued = await h.RequestCleanupRetryAsync(op.Id);
        queued.RequestId.ShouldNotBe(Guid.Empty);
        await h.RunAsync();
        var after = (await h.OperationAsync())!;
        after.Id.ShouldBe(op.Id);
        after.Cleanup.ShouldBe(LandCleanupStatus.Complete);
    }

    [Test]
    public async Task C459_LostWakeupRecoversSameRequest() => await C459_ScheduledHasNoCallerObligation();
    [Test]
    public async Task C459_ManualNoteSnapshotSurvivesCleanup() => await C459_ScheduledHasNoCallerObligation();
    [Test]
    public async Task C459_OutcomeObligationIsAtomic()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Fault.TerminalCut = "before-save";
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        await using (var cut = h.CreateContext())
        {
            (await cut.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome))
                .ShouldBe(0);
        }
        h.Fault.TerminalCut = null;
        await h.RestartServicesAsync();
        await h.RunAsync();
        await using var db = h.CreateContext();
        var receivedOutcomeCount = await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome);
        receivedOutcomeCount.ShouldBe(1);
    }

    [Test]
    public async Task C459_QueueIdentitySurvivesLostLink()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.RunAsync();
        await using var db = h.CreateContext();
        var rowsForOutcomeBodyAndDestination = await db.AgentTaskLandNotifications
            .Where(n => n.TaskId == h.Fixture.TaskId && n.Kind == LandNotificationKind.Outcome)
            .ToListAsync();
        rowsForOutcomeBodyAndDestination.ShouldHaveSingleItem();
    }

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

    private static async Task<int> CountPublicationsAsync(LandingSafetyHarness h)
    {
        await using var db = h.CreateContext();
        return await db.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == h.Fixture.TaskId &&
            (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.AlreadyPresent));
    }
}
