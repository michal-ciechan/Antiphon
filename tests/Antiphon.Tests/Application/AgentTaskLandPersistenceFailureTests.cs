using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandPersistenceFailureTests
{
    [Test]
    [Arguments("success", "before-save")]
    [Arguments("success", "after-save")]
    [Arguments("success", "commit")]
    [Arguments("success", "after-commit")]
    [Arguments("refusal", "before-save")]
    [Arguments("refusal", "after-save")]
    [Arguments("refusal", "commit")]
    [Arguments("refusal", "after-commit")]
    [Arguments("cleanup", "before-save")]
    [Arguments("cleanup", "after-save")]
    [Arguments("cleanup", "commit")]
    [Arguments("cleanup", "after-commit")]
    public async Task C467_V06_AtomicSettlementFaultMatrix(string outcome, string cut)
    {
        await using var h = new LandingSafetyHarness(); await h.InitializeAsync(); await h.AddSourceAsync();
        if (outcome == "refusal")
        {
            await using var db = h.CreateContext();
            await db.AgentTasks.Where(t => t.Id == h.Fixture.TaskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorktreeBranch, (string?)null));
        }
        if (outcome == "cleanup")
        {
            var residue = Path.Combine(h.Fixture.Source, ".antiphon", "valuable.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(residue)!); await File.WriteAllTextAsync(residue, "preserve");
            await h.RunAsync(); File.Delete(residue); await h.RepostAsync();
        }
        h.Fault.TerminalCut = cut;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        h.Fault.Triggered.ShouldBeTrue();
        await using (var observer = h.CreateContext())
        {
            var task = await observer.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            var request = await observer.AgentTaskLandRequests.SingleAsync(r => r.Id == task.CurrentLandRequestId);
            request.IsPending.ShouldBe(cut != "after-commit");
            (task.LandRequestedAt is null).ShouldBe(cut == "after-commit");
            (await observer.AgentTaskEvents.CountAsync(e => e.LandRequestId == request.Id && e.IsLandTerminal)).ShouldBe(cut == "after-commit" ? 1 : 0);
            (await observer.AgentTaskLandNotifications.CountAsync(n => n.RequestId == request.Id)).ShouldBe(cut == "after-commit" ? 1 : 0);
        }
        h.Fixture.Git.Trace.Clear();
        await h.RestartServicesAsync();
        if (cut == "after-commit") await h.FailAsync(new IOException("lost commit acknowledgement")); else await h.RunAsync();
        await using var final = h.CreateContext();
        var current = await final.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
        (await final.AgentTaskLandNotifications.CountAsync(n => n.RequestId == current.CurrentLandRequestId)).ShouldBe(1);
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
        if (outcome != "refusal") await h.Fixture.AssertRemoteSourceAsync();
    }
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C448_F07_PublicationSettlementIsAtomic(bool committed)
        => C448_V16_AcknowledgedCheckpointsGateDependentMutations(LandPhase.Complete, committed);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C448_F03_TargetIntentAcknowledgement(bool committed)
        => C448_V16_AcknowledgedCheckpointsGateDependentMutations(LandPhase.TargetAdvanceStarted, committed);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C448_F04_PushIntentAcknowledgement(bool committed)
        => C448_V16_AcknowledgedCheckpointsGateDependentMutations(LandPhase.PushStarted, committed);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C448_F05_PublicationReceiptAcknowledgement(bool committed)
        => C448_V16_AcknowledgedCheckpointsGateDependentMutations(LandPhase.PublicationConfirmed, committed);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task C448_F06_CleanupIntentAcknowledgement(bool committed)
        => C448_V16_AcknowledgedCheckpointsGateDependentMutations(LandPhase.CleanupStarted, committed);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_F01_SourcePinResultMustBeAcknowledgedBeforeTheNextPin(bool afterCommit)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        h.Fault.Matches = op => op.Phase == LandPhase.Inspected && op.SourcePinned;
        h.Fault.AfterCommit = afterCommit;
        h.Fixture.Git.Trace.Clear();
        var interrupted = false;
        try { await h.RunAsync(); }
        catch (LandingSafetyHarness.InjectedSaveFailure) { interrupted = true; }
        h.Fault.Triggered.ShouldBeTrue();
        var before = (await h.OperationAsync()).ShouldNotBeNull();
        before.SourcePinned.ShouldBe(afterCommit);
        before.TargetPinned.ShouldBeFalse();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("remove") || a[0] == "push"
            || a[0] == "update-ref" && a.Any(x => x.EndsWith("/target-before", StringComparison.Ordinal)));
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", before.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
        interrupted.ShouldBeTrue("save acknowledgement failure must end this invocation");
        await h.RestartServicesAsync();
        await h.RunAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(before.Id);
        after.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", before.RecoveryRefPrefix + "/source")).Trim().ShouldBe(source);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_F04_PushResultAcknowledgementCannotAuthorizeCleanup(bool afterCommit)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        h.Fault.Matches = op => op.Phase == LandPhase.PushStarted && op.PushExitCode is not null;
        h.Fault.AfterCommit = afterCommit;
        h.Fixture.Git.Trace.Clear();
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        h.Fault.Triggered.ShouldBeTrue();
        var before = (await h.OperationAsync()).ShouldNotBeNull();
        before.PushExitCode.HasValue.ShouldBe(afterCommit);
        before.RemoteConfirmedAt.ShouldBeNull();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(source);
        h.Fixture.Git.Trace.Clear();
        await h.RestartServicesAsync();
        await h.RunAsync();
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Id.ShouldBe(before.Id);
        after.RemoteConfirmedAt.ShouldNotBeNull();
        after.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
        h.Fixture.Git.Trace.ShouldContain(a => a[0] == "ls-remote");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V23_DeliveryFailureKeepsTheCommittedPublicationAndEvent()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var logger = new DeliveryLogger();
        h.Logger = logger;
        h.Messages = new Antiphon.Server.Application.Services.SessionMessageQueueService(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(h.Services),
            null!, new MockEventBus(), TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Antiphon.Server.Application.Services.SessionMessageQueueService>.Instance);
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.ParentSessionId = Guid.NewGuid(); // The real queue must report the missing delivery destination.
            task.ReplyTo = AgentTaskReplyTo.Session;
            await db.SaveChangesAsync();
        }
        await h.RunAsync();
        logger.Errors.ShouldBeEmpty(); // The drain commits an obligation; the independent worker owns delivery.
        var published = (await h.OperationAsync())!;
        published.Publication.ShouldBe(LandPublicationOutcome.Landed);
        published.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        await using var observer = h.CreateContext();
        var obligation = await observer.AgentTaskLandNotifications.SingleAsync(n => n.TaskId == h.Fixture.TaskId);
        obligation.ReplyTo.ShouldBe(AgentTaskReplyTo.Session);
        obligation.ConfirmedAt.ShouldBeNull();
        (await observer.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.Landed)).ShouldBe(1);
        (await observer.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(0);
        (await observer.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId)).LandRequestedAt.ShouldBeNull();
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private sealed class DeliveryLogger : Microsoft.Extensions.Logging.ILogger<Antiphon.Server.Application.Services.AgentTaskLandService>
    {
        public List<Exception> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (exception is not null) Errors.Add(exception); }
    }

    [Test]
    public async Task C448_V23_NotificationFailureCannotReplaceConfirmedPublication()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        h.Events = new FailingEventBus();
        await Should.ThrowAsync<NotificationFailure>(() => h.RunAsync());
        var published = (await h.OperationAsync()).ShouldNotBeNull();
        published.Publication.ShouldBe(LandPublicationOutcome.Landed);
        published.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        h.Events = new MockEventBus();
        await h.FailAsync(new NotificationFailure());
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Publication.ShouldBe(published.Publication);
        after.RemoteConfirmedAt.ShouldBe(published.RemoteConfirmedAt);
        await using var observer = h.CreateContext();
        (await observer.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(0);
        (await observer.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId
            && e.Type == AgentTaskEventType.Landed)).ShouldBe(1);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private sealed class NotificationFailure : Exception;
    private sealed class FailingEventBus : Antiphon.Server.Application.Interfaces.IEventBus
    {
        public Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default) => throw new NotificationFailure();
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) => throw new NotificationFailure();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_F07_RefusalEvidenceAndTerminalEventCommitTogether(bool afterCommit)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = await h.AddSourceAsync();
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.LandVerifyFilter = "fixture-required";
            await db.SaveChangesAsync();
        }
        h.Verifier.Passed = false;
        h.Fault.Phase = LandPhase.Refused;
        h.Fault.AfterCommit = afterCommit;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        h.Fault.Triggered.ShouldBeTrue();
        await using var observer = h.CreateContext();
        var stored = await observer.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
        var operation = await observer.AgentTaskLandings.SingleAsync(o => o.TaskId == stored.Id);
        (operation.Phase == LandPhase.Refused).ShouldBe(afterCommit);
        (stored.LandRequestedAt is null).ShouldBe(afterCommit);
        (await observer.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == stored.Id && e.Type == AgentTaskEventType.LandRefused))
            .ShouldBe(afterCommit);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(sha);
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
    }

    [Test]
    [Arguments(LandPhase.Inspected, false)]
    [Arguments(LandPhase.Inspected, true)]
    [Arguments(LandPhase.RecoveryPinned, false)]
    [Arguments(LandPhase.RecoveryPinned, true)]
    [Arguments(LandPhase.RebaseStarted, false)]
    [Arguments(LandPhase.RebaseStarted, true)]
    [Arguments(LandPhase.Prepared, false)]
    [Arguments(LandPhase.Prepared, true)]
    [Arguments(LandPhase.Verified, false)]
    [Arguments(LandPhase.Verified, true)]
    [Arguments(LandPhase.TargetAdvanceStarted, false)]
    [Arguments(LandPhase.TargetAdvanceStarted, true)]
    [Arguments(LandPhase.LocalTargetAdvanced, false)]
    [Arguments(LandPhase.LocalTargetAdvanced, true)]
    [Arguments(LandPhase.PushStarted, false)]
    [Arguments(LandPhase.PushStarted, true)]
    [Arguments(LandPhase.PublicationConfirmed, false)]
    [Arguments(LandPhase.PublicationConfirmed, true)]
    [Arguments(LandPhase.CleanupStarted, false)]
    [Arguments(LandPhase.CleanupStarted, true)]
    [Arguments(LandPhase.Complete, false)]
    [Arguments(LandPhase.Complete, true)]
    public async Task C448_V16_AcknowledgedCheckpointsGateDependentMutations(LandPhase phase, bool afterCommit)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = await h.AddSourceAsync();
        h.Fault.Phase = phase;
        h.Fault.AfterCommit = afterCommit;
        var interrupted = false;
        try { await h.RunAsync(); }
        catch (LandingSafetyHarness.InjectedSaveFailure) { interrupted = true; }
        h.Fault.Triggered.ShouldBeTrue();
        if (phase != LandPhase.Complete)
        {
            Directory.Exists(h.Fixture.Source).ShouldBeTrue("save acknowledgement must precede dependent removal");
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
        }
        if (phase is LandPhase.Inspected or LandPhase.RecoveryPinned or LandPhase.RebaseStarted or LandPhase.Prepared or LandPhase.Verified or LandPhase.TargetAdvanceStarted)
        {
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
            h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
        }
        if (phase == LandPhase.PushStarted) h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
        if (phase == LandPhase.Complete)
        {
            await using var observer = h.CreateContext();
            var task = await observer.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            var op = await observer.AgentTaskLandings.SingleAsync(o => o.TaskId == task.Id);
            (op.Phase == LandPhase.Complete).ShouldBe(afterCommit);
            (task.LandRequestedAt is null).ShouldBe(afterCommit);
            (await observer.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Landed)).ShouldBe(afterCommit);
        }
        if (phase != LandPhase.Complete)
        {
            await using var observer = h.CreateContext();
            (await observer.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId)).LandRequestedAt
                .ShouldNotBeNull("an unacknowledged checkpoint cannot settle the pending request");
            (await observer.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId
                && (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.AlreadyPresent
                    || e.Type == AgentTaskEventType.LandedWithResidue))).ShouldBe(0);
        }
        interrupted.ShouldBeTrue("save acknowledgement failure must end this invocation");
        await h.RestartServicesAsync();
        await h.RunAsync();
        var recovered = (await h.OperationAsync()).ShouldNotBeNull();
        if (phase == LandPhase.Prepared && !afterCommit || phase == LandPhase.RebaseStarted && afterCommit)
        {
            recovered.LastReason.ShouldBe("interrupted_rebase_requires_inspection");
            recovered.Cleanup.ShouldBe(LandCleanupStatus.NotStarted);
            Directory.Exists(h.Fixture.Source).ShouldBeTrue();
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", recovered.RecoveryRefPrefix + "/source")).Trim().ShouldBe(sha);
        }
        else
        {
            recovered.Cleanup.ShouldBe(LandCleanupStatus.Complete);
            recovered.VerifiedSourceSha.ShouldBe(sha);
            (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(sha);
        }
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
