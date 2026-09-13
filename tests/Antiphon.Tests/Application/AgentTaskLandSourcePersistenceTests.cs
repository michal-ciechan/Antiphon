using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandSourcePersistenceTests
{
    [Test]
    [Arguments(0)]
    [Arguments(300)]
    [Arguments(900)]
    public async Task C498_MonitorPassDuringObservedInspection(int ageSeconds)
    {
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(new DateTimeOffset(now));
        await using var h = new LandingProtocolHarness();
        h.Clock = clock;
        await h.InitializeAsync();
        var unique = await h.AddSourceAsync();
        h.Git.SetRemoteSource(unique);
        var expected = h.Git.SourceHead;
        var barrier = new PauseBarrier();
        Guid? tokenBefore = null;
        DateTimeOffset monitorNow = now;
        h.Git.OnSourceObservation = async n =>
        {
            if (n == 1)
            {
                await using var before = h.CreateContext();
                tokenBefore = (await before.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId)).ConcurrencyToken;
                barrier.SignalPaused();
                await barrier.WaitReleaseAsync();
            }
            if (n == 2)
            {
                await using var mid = h.CreateContext();
                var stored = await mid.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId);
                stored.SourceResolutionState.ShouldBe(LandSourceResolutionState.Observed);
                stored.LocalBeforeSha.ShouldBe(h.Git.SourceHead);
                stored.RemoteSourceSha.ShouldBe(h.Git.RemoteSource);
                stored.CandidateSourceSha.ShouldBe(expected);
                stored.SourceRelationship.ShouldBe(LandSourceRelationship.Equal);
                stored.RemoteSourceFingerprint.ShouldBe(h.Git.Fingerprint);
                stored.SourceObservationRef.ShouldStartWith($"refs/antiphon/land/{h.Git.TaskId:N}/");
                stored.SourceObservationRef.ShouldContain("/source-observed/");
                stored.LastEvaluatedAt.ShouldBe(monitorNow.UtcDateTime);
                stored.LastProgressAt.ShouldBe(monitorNow.UtcDateTime);
                h.Git.InspectionCalls.ShouldBe(1);
            }
        };
        var queued = await h.RequestAsync(expectedSourceSha: expected);
        var runTask = h.RunQueuedAsync();
        await barrier.WaitPausedAsync();
        clock.Advance(TimeSpan.FromSeconds(ageSeconds));
        monitorNow = clock.GetUtcNow();
        await SweepMonitorAsync(h);
        await using (var after = h.CreateContext())
        {
            var stored = await after.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            stored.ConcurrencyToken.ShouldNotBe(tokenBefore!.Value);
            stored.LastEvaluatedAt.ShouldBe(monitorNow.UtcDateTime);
            var aged = await after.AgentTaskLandNotifications.CountAsync(n => n.RequestId == queued.RequestId && n.Kind == LandNotificationKind.Aged);
            aged.ShouldBe(ageSeconds == 0 ? 0 : ageSeconds == 300 ? 1 : 2);
        }
        try { barrier.SignalRelease(); var run = await runTask; run.ShouldBe(LandRunResult.Complete); }
        finally { barrier.SignalRelease(); }
        await using var done = h.CreateContext();
        var request = await done.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        var op = await done.AgentTaskLandings.SingleAsync(o => o.TaskId == h.Git.TaskId && o.Active);
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        request.ResolvedSourceSha.ShouldBe(expected);
        request.ExpectedSourceSha.ShouldBe(expected);
        request.ApprovalKind.ShouldBe(LandApprovalKind.ExplicitCaller);
        request.ReviewEvidenceId.ShouldBeNull();
        request.TerminalFailureCode.ShouldBeNull();
        (await done.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Git.TaskId && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(0);
        (await done.AgentTaskLandNotifications.CountAsync(n => n.RequestId == queued.RequestId && n.Kind == LandNotificationKind.Aged))
            .ShouldBe(ageSeconds == 0 ? 0 : ageSeconds == 300 ? 1 : 2);
        // Resolver observes twice (initial + recheck). Protocol RecheckRemoteSourceAsync uses source-recheck, not a ResolveAsync replay.
        h.Git.Trace.Count(a => a.Any(s => s.Contains("/source-observed/", StringComparison.Ordinal))).ShouldBe(2);
        h.Git.InspectionCalls.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    [Arguments("observed")]
    [Arguments("advance-started")]
    [Arguments("child-started")]
    [Arguments("child-cleared")]
    [Arguments("resolved")]
    [Arguments("operation")]
    [Arguments("refuse-observed")]
    [Arguments("refuse-plain")]
    public async Task C498_MonitorPassAtEveryCheckpoint(string checkpoint)
    {
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(new DateTimeOffset(now));
        await using var h = new LandingProtocolHarness();
        h.Clock = clock;
        await h.InitializeAsync();
        var barrier = new PauseBarrier();
        var expected = checkpoint is "refuse-observed" or "refuse-plain" ? h.Git.SourceHead : h.Git.AdvanceRemoteSource();
        if (checkpoint == "refuse-observed")
        {
            await h.AddSourceAsync();
            h.Git.DivergeRemoteSource();
            expected = h.Git.SourceHead;
        }
        if (checkpoint == "refuse-plain") File.WriteAllText(Path.Combine(h.Git.Source, "keep.txt"), "dirty\n");
        var inspections = 0;
        h.Git.BeforeInspection = async () =>
        {
            inspections++;
            if (checkpoint == "refuse-plain" && inspections == 1
                || checkpoint == "advance-started" && inspections == 2
                || checkpoint == "operation" && inspections == 4)
            {
                barrier.SignalPaused();
                await barrier.WaitReleaseAsync();
            }
        };
        h.Git.OnSourceObservation = async n =>
        {
            if (checkpoint is "observed" or "refuse-observed" && n == 1
                || checkpoint == "resolved" && n == 4)
            {
                barrier.SignalPaused();
                await barrier.WaitReleaseAsync();
            }
        };
        h.Git.BeforeCommand = async (_, args) =>
        {
            if (checkpoint == "child-started" && args.Contains("merge") && args.Contains("--ff-only"))
            {
                barrier.SignalPaused();
                await barrier.WaitReleaseAsync();
            }
            return null;
        };
        h.Git.AfterCommand = async (_, args, _) =>
        {
            if (checkpoint == "child-cleared" && args.Contains("merge") && args.Contains("--ff-only"))
            {
                barrier.SignalPaused();
                await barrier.WaitReleaseAsync();
            }
        };
        var queued = await h.RequestAsync(expectedSourceSha: expected);
        var runTask = h.RunQueuedAsync();
        await barrier.WaitPausedAsync();
        var monitorNow = clock.GetUtcNow().UtcDateTime;
        await SweepMonitorAsync(h);
        try { barrier.SignalRelease(); (await runTask).ShouldBe(LandRunResult.Complete); }
        finally { barrier.SignalRelease(); }
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        request.LastEvaluatedAt.ShouldBe(monitorNow);
        request.SourceAdvanceChildProcessId.ShouldBeNull();
        request.SourceAdvanceChildOperation.ShouldBeNull();
        if (checkpoint.StartsWith("refuse", StringComparison.Ordinal))
        {
            request.SourceRefusalReason.ShouldBe(checkpoint == "refuse-observed" ? "source_remote_diverged" : "source_dirty");
            request.TerminalFailureCode.ShouldBeNull();
            (await db.AgentTaskEvents.CountAsync(e => e.LandRequestId == request.Id && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(1);
        }
        else
        {
            var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == h.Git.TaskId && o.Active);
            op.Publication.ShouldBe(LandPublicationOutcome.Landed);
            request.SourceResolutionState.ShouldBe(LandSourceResolutionState.Resolved);
            request.ResolvedSourceSha.ShouldBe(expected);
            (await db.AgentTaskEvents.CountAsync(e => e.LandRequestId == request.Id && e.IsLandTerminal)).ShouldBe(1);
        }
    }

    [Test]
    [Arguments("inspection")]
    [Arguments("observation")]
    [Arguments("ancestor-command")]
    public async Task C498_ResolverCheckpointDoesNotHoldLockAcrossGit(string hook)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var expected = hook == "inspection" ? h.Git.SourceHead : h.Git.AdvanceRemoteSource();
        Task? monitor = null;
        async Task RunMonitorAsync()
        {
            monitor = SweepMonitorAsync(h);
            await monitor.WaitAsync(TimeSpan.FromSeconds(10));
        }
        h.Git.BeforeInspection = hook == "inspection" ? RunMonitorAsync : null;
        h.Git.OnSourceObservation = hook == "observation" ? _ => RunMonitorAsync() : null;
        h.Git.OnAncestorCheck = hook == "ancestor-command" ? RunMonitorAsync : null;
        await h.RequestAsync(expectedSourceSha: expected);
        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Complete);
        monitor.ShouldNotBeNull();
        await monitor!;
    }

    [Test]
    public async Task C498_MonitorWaitsForResolverCheckpoint()
    {
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(new DateTimeOffset(now));
        await using var h = new LandingProtocolHarness();
        h.Clock = clock;
        await h.InitializeAsync();
        Task? monitor = null;
        var monitorNow = now;
        h.Fault.AfterSaveAcknowledged = ctx =>
        {
            var observed = ctx.ChangeTracker.Entries<AgentTaskLandRequest>()
                .Any(e => e.Entity.SourceResolutionState == LandSourceResolutionState.Observed
                    && ctx.Database.CurrentTransaction is not null);
            if (!observed) return Task.CompletedTask;
            ctx.Database.CurrentTransaction.ShouldNotBeNull();
            monitorNow = clock.GetUtcNow().UtcDateTime;
            monitor = SweepMonitorAsync(h);
            return Task.CompletedTask;
        };
        await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Complete);
        monitor.ShouldNotBeNull();
        await monitor!;
        await using var db = h.CreateContext();
        var stored = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId);
        stored.LastEvaluatedAt.ShouldBe(monitorNow);
    }

    [Test]
    [Arguments("replaced")]
    [Arguments("canceled")]
    [Arguments("terminal")]
    [Arguments("attempt")]
    [Arguments("approval-sha")]
    [Arguments("approval-evidence")]
    [Arguments("approval-kind")]
    [Arguments("approval-filter")]
    [Arguments("coordinates-target")]
    [Arguments("coordinates-source")]
    public async Task C498_StaleRequestGuards(string change)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var barrier = new PauseBarrier();
        h.Git.OnSourceObservation = async n =>
        {
            if (n != 1) return;
            barrier.SignalPaused();
            await barrier.WaitReleaseAsync();
        };
        var queued = await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        var runTask = h.RunQueuedAsync();
        await barrier.WaitPausedAsync();
        Guid? replacementId = null;
        await MutateAsync(h, queued.RequestId, change, id => replacementId = id);
        var traceAtRelease = h.Git.Trace.Count;
        try { barrier.SignalRelease(); (await runTask).ShouldBe(LandRunResult.Complete); }
        finally { barrier.SignalRelease(); }
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
        await using var db = h.CreateContext();
        if (change == "replaced")
        {
            var old = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            old.IsPending.ShouldBeFalse();
            old.SourceResolutionState.ShouldBe(LandSourceResolutionState.None);
            var replacement = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == replacementId);
            replacement.IsPending.ShouldBeTrue();
            replacement.SourceResolutionState.ShouldBe(LandSourceResolutionState.None);
        }
        else
        {
            var stored = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            stored.SourceResolutionState.ShouldBe(LandSourceResolutionState.None);
        }
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Git.TaskId && e.IsLandTerminal))
            .ShouldBe(change == "terminal" ? 1 : 0);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Git.TaskId && n.Kind == LandNotificationKind.Outcome)).ShouldBe(0);
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == h.Git.TaskId && r.TerminalFailureCode != null)).ShouldBe(0);
    }

    [Test]
    [Arguments("newer-observation")]
    [Arguments("child-checkpoint")]
    [Arguments("resolved")]
    [Arguments("operation")]
    public async Task C498_NewerSourceProgressConflict(string change)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var barrier = new PauseBarrier();
        h.Git.OnSourceObservation = async n =>
        {
            if (n != 1) return;
            barrier.SignalPaused();
            await barrier.WaitReleaseAsync();
        };
        var queued = await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        var runTask = h.RunQueuedAsync();
        await barrier.WaitPausedAsync();
        var injectedNewer = new string('b', 40);
        await using (var mut = h.CreateContext())
        {
            var request = await mut.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            var task = await mut.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            request.ConcurrencyToken = Guid.NewGuid();
            task.ConcurrencyToken = Guid.NewGuid();
            if (change == "newer-observation")
            {
                request.SourceResolutionState = LandSourceResolutionState.Observed;
                request.LocalBeforeSha = injectedNewer;
                request.RemoteSourceSha = injectedNewer;
                request.CandidateSourceSha = injectedNewer;
            }
            else if (change == "child-checkpoint")
            {
                request.SourceAdvanceChildOperation = "source-ff";
                request.SourceAdvanceChildProcessId = 7;
                request.SourceAdvanceChildStartTicks = 9;
            }
            else if (change == "resolved")
            {
                request.SourceResolutionState = LandSourceResolutionState.Resolved;
                request.ResolvedSourceSha = injectedNewer;
            }
            else
            {
                var op = new AgentTaskLanding
                {
                    Id = Guid.NewGuid(), TaskId = h.Git.TaskId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                    OriginalSourceSha = h.Git.SourceHead, ApprovalLandRequestId = request.Id,
                };
                mut.AgentTaskLandings.Add(op);
                task.ActiveLandingId = op.Id;
                request.LandingOperationId = op.Id;
            }
            await mut.SaveChangesAsync();
        }
        var traceAtRelease = h.Git.Trace.Count;
        try { barrier.SignalRelease(); (await runTask).ShouldBe(LandRunResult.Complete); }
        finally { barrier.SignalRelease(); }
        h.Git.OwnedTrace.ShouldNotContain(a => a.Contains("merge"));
        h.Git.SourceObservationAttempts.ShouldBe(1);
        await using var db = h.CreateContext();
        var stored = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
        stored.TerminalFailureCode.ShouldBe("source_resolution_state_changed");
        stored.FailureDiagnosticId.ShouldNotBeNull();
        if (change == "newer-observation") stored.LocalBeforeSha.ShouldBe(injectedNewer);
        (await db.AgentTaskEvents.CountAsync(e => e.LandRequestId == stored.Id && e.Type == AgentTaskEventType.LandRefused)).ShouldBe(1);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.RequestId == stored.Id && n.Kind == LandNotificationKind.Outcome)).ShouldBe(1);
    }

    [Test]
    [Arguments("replaced-request")]
    [Arguments("other-operation-bound")]
    [Arguments("approval-changed")]
    public async Task C498_StaleOperationAttachAbandonsCandidate(string change)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var barrier = new PauseBarrier();
        h.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] == "rev-parse" && args.Any(a => a.Contains("^{commit}", StringComparison.Ordinal)))
            {
                barrier.SignalPaused();
                await barrier.WaitReleaseAsync();
            }
            return null;
        };
        var queued = await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        var runTask = h.RunQueuedAsync();
        await barrier.WaitPausedAsync();
        var injectedCount = 0;
        await using (var mut = h.CreateContext())
        {
            var request = await mut.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            var task = await mut.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            request.ConcurrencyToken = Guid.NewGuid();
            task.ConcurrencyToken = Guid.NewGuid();
            if (change == "replaced-request")
            {
                request.IsPending = false;
                request.State = LandRequestState.Canceled;
                var replacement = NewPending(task, DateTime.UtcNow);
                mut.AgentTaskLandRequests.Add(replacement);
                task.CurrentLandRequestId = replacement.Id;
                task.LandAttempt = 0;
                task.LandStartedAt = null;
            }
            else if (change == "other-operation-bound")
            {
                var op = new AgentTaskLanding
                {
                    Id = Guid.NewGuid(), TaskId = h.Git.TaskId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                    OriginalSourceSha = h.Git.SourceHead, ApprovalLandRequestId = request.Id,
                };
                mut.AgentTaskLandings.Add(op);
                task.ActiveLandingId = op.Id;
                request.LandingOperationId = op.Id;
                injectedCount = 1;
            }
            else request.ExpectedSourceSha = new string('c', 40);
            await mut.SaveChangesAsync();
        }
        try { barrier.SignalRelease(); (await runTask).ShouldBe(LandRunResult.Complete); }
        finally { barrier.SignalRelease(); }
        await using var db = h.CreateContext();
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(injectedCount);
        h.Git.Trace.ShouldNotContain(a => a[0] == "push");
        if (change == "replaced-request")
        {
            var replacement = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId && r.IsPending);
            replacement.LandingOperationId.ShouldBeNull();
        }
    }

    [Test]
    public async Task C498_ChildStartCallbackConflictFailsCallback()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var expected = h.Git.AdvanceRemoteSource();
        var barrier = new PauseBarrier();
        h.Git.BeforeCommand = async (_, args) =>
        {
            if (args.Contains("merge") && args.Contains("--ff-only"))
            {
                barrier.SignalPaused();
                await barrier.WaitReleaseAsync();
            }
            return null;
        };
        var queued = await h.RequestAsync(expectedSourceSha: expected);
        var runTask = h.RunQueuedAsync();
        await barrier.WaitPausedAsync();
        await using (var mut = h.CreateContext())
        {
            var request = await mut.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            request.IsPending = false;
            request.State = LandRequestState.Canceled;
            request.ConcurrencyToken = Guid.NewGuid();
            var task = await mut.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            var replacement = NewPending(task, DateTime.UtcNow);
            mut.AgentTaskLandRequests.Add(replacement);
            task.CurrentLandRequestId = replacement.Id;
            task.ConcurrencyToken = Guid.NewGuid();
            await mut.SaveChangesAsync();
        }
        try
        {
            barrier.SignalRelease();
            await Should.ThrowAsync<Exception>(runTask);
        }
        finally { barrier.SignalRelease(); }
        h.Git.SourceHead.ShouldBe(h.Git.SeedSha);
        await using var db = h.CreateContext();
        (await db.AgentTaskLandings.CountAsync(o => o.TaskId == h.Git.TaskId)).ShouldBe(0);
        (await db.AgentTaskLandRequests.CountAsync(r => r.TaskId == h.Git.TaskId && r.IsPending)).ShouldBe(1);
    }

    [Test]
    [Arguments("replaced")]
    [Arguments("canceled")]
    public async Task C498_RecoverySweepWriteReloadsUnderLock(string change)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var queued = await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        h.Queue.TryDequeue(out _);
        h.Queue.Release(h.Git.TaskId);
        await using (var setup = h.CreateContext())
        {
            var task = await setup.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            var request = await setup.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            task.LandStartedAt = DateTime.UtcNow;
            task.LandAttempt = 1;
            request.Attempt = 1;
            task.ConcurrencyToken = Guid.NewGuid();
            request.ConcurrencyToken = Guid.NewGuid();
            await setup.SaveChangesAsync();
        }
        var armed = 0;
        h.Fault.OnTransactionStarted = async ctx =>
        {
            if (Interlocked.Increment(ref armed) != 1) return;
            await using var mut = h.CreateContext();
            var task = await mut.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            var request = await mut.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            if (change == "replaced")
            {
                request.IsPending = false;
                request.State = LandRequestState.Canceled;
                request.ConcurrencyToken = Guid.NewGuid();
                var replacement = NewPending(task, DateTime.UtcNow);
                mut.AgentTaskLandRequests.Add(replacement);
                task.CurrentLandRequestId = replacement.Id;
                task.LandStartedAt = null;
                task.LandAttempt = 0;
            }
            else
            {
                task.Status = AgentTaskStatus.Canceled;
                request.State = LandRequestState.Canceled;
                request.IsPending = false;
                request.ReconciliationError = "task_no_longer_eligible";
                task.LandRequestedAt = null;
            }
            task.ConcurrencyToken = Guid.NewGuid();
            await mut.SaveChangesAsync();
        };
        await h.SweepAsync();
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Git.TaskId && e.Type == AgentTaskEventType.Warning
            && e.Detail!.Contains("Land attempt 1"))).ShouldBe(0);
        if (change == "replaced")
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
            var replacement = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId && r.IsPending);
            task.CurrentLandRequestId.ShouldBe(replacement.Id);
            task.LandAttempt.ShouldBe(0);
            h.Queue.TryDequeue(out var entry).ShouldBeTrue();
            entry.RequestId.ShouldBe(replacement.Id);
        }
        else
        {
            h.Queue.TryDequeue(out _).ShouldBeFalse();
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
            request.State.ShouldBe(LandRequestState.Canceled);
            request.ReconciliationError.ShouldBe("task_no_longer_eligible");
        }
    }

    [Test]
    public async Task C498_HostShutdownDuringResolutionLeavesRequestPending()
    {
        await using var h = new LandingProtocolHarness();
        h.AddHostedLandService();
        await h.InitializeAsync();
        var barrier = new PauseBarrier();
        h.Git.OnSourceObservation = async n =>
        {
            if (n != 1) return;
            barrier.SignalPaused();
            await barrier.WaitReleaseAsync();
        };
        await h.RequestAsync(expectedSourceSha: h.Git.SourceHead);
        var hosted = new AgentTaskLandHostedService(h.Queue, h.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AgentTaskLandHostedService>.Instance);
        await hosted.StartAsync(CancellationToken.None);
        await barrier.WaitPausedAsync();
        var stopping = hosted.StopAsync(CancellationToken.None);
        barrier.SignalRelease();
        await stopping;
        await using var db = h.CreateContext();
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Git.TaskId);
        request.IsPending.ShouldBeTrue();
        request.TerminalFailureCode.ShouldBeNull();
        request.FailureDiagnosticId.ShouldBeNull();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Git.TaskId && e.IsLandTerminal)).ShouldBe(0);
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Git.TaskId && n.Kind == LandNotificationKind.Outcome)).ShouldBe(0);
        (await db.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId)).LandRequestedAt.ShouldNotBeNull();
    }

    private static async Task SweepMonitorAsync(LandingProtocolHarness h)
    {
        await using var db = h.CreateContext();
        await new AgentTaskLandMonitorService(db, h.Clock, Options.Create(new DelegationSettings()), h.Events)
            .SweepAsync(CancellationToken.None);
    }

    private static async Task MutateAsync(LandingProtocolHarness h, Guid requestId, string change, Action<Guid> replaced)
    {
        await using var mut = h.CreateContext();
        var request = await mut.AgentTaskLandRequests.SingleAsync(r => r.Id == requestId);
        var task = await mut.AgentTasks.SingleAsync(t => t.Id == h.Git.TaskId);
        request.ConcurrencyToken = Guid.NewGuid();
        task.ConcurrencyToken = Guid.NewGuid();
        switch (change)
        {
            case "replaced":
                request.IsPending = false;
                request.State = LandRequestState.Canceled;
                var replacement = NewPending(task, DateTime.UtcNow);
                mut.AgentTaskLandRequests.Add(replacement);
                task.CurrentLandRequestId = replacement.Id;
                replaced(replacement.Id);
                break;
            case "canceled":
                request.IsPending = false;
                request.State = LandRequestState.Canceled;
                break;
            case "terminal":
                var terminal = new AgentTaskEvent
                {
                    Id = Guid.NewGuid(), AgentTaskId = task.Id, LandRequestId = request.Id,
                    Type = AgentTaskEventType.LandRefused, IsLandTerminal = true, At = DateTime.UtcNow, Detail = "prior",
                };
                mut.AgentTaskEvents.Add(terminal);
                request.TerminalEventId = terminal.Id;
                break;
            case "attempt":
                request.Attempt++;
                task.LandAttempt++;
                break;
            case "approval-sha":
                request.ExpectedSourceSha = new string('c', 40);
                break;
            case "approval-evidence":
                request.ReviewEvidenceId = Guid.NewGuid();
                break;
            case "approval-kind":
                request.ApprovalKind = LandApprovalKind.ReviewEvidence;
                break;
            case "approval-filter":
                request.VerifyFilter = "changed";
                break;
            case "coordinates-target":
                request.TargetFullRefSnapshot = "refs/heads/other";
                break;
            case "coordinates-source":
                request.SourceFullRefSnapshot = "refs/heads/other-source";
                break;
        }
        await mut.SaveChangesAsync();
    }

    private static AgentTaskLandRequest NewPending(AgentTask task, DateTime at) => new()
    {
        Id = Guid.NewGuid(), TaskId = task.Id, RequestedAt = at, LastEvaluatedAt = at, LastProgressAt = at,
        State = LandRequestState.Queued, SchemaVersion = 2, ExpectedSourceSha = new string('a', 40),
        ApprovalKind = LandApprovalKind.ExplicitCaller, ApprovedAt = at,
        SourceFullRefSnapshot = "refs/heads/" + task.WorktreeBranch,
        TargetFullRefSnapshot = "refs/heads/master", RepositoryPathSnapshot = task.RepoPath, WorktreePathSnapshot = task.WorktreePath,
    };
}
