using System.Data.Common;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1082 S4b. The dispatcher sweep fast-forwards a seeded debt only to its recorded source.
/// Fifteen methods, twenty-four results: the three refusal rows, the two episode rows, the
/// task-detail projection, and CARD-1136's Held re-check methods. F3d keeps every row Held:
/// a complete retirement, a recreated registration, and an unreadable, empty, symlinked, or
/// moved registration are not a registration id.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class SettlementSyncRecoveryTests
{
    /// <summary>
    /// V-14. A due row fast-forwards the desktop to S, ends Ready, and publishes
    /// <c>AgentTaskChanged</c>. An empty table, and the same table with the kill switch off,
    /// is one indexed read. A crash between the claim, the fast-forward and the row update
    /// leaves a retry that reaches the same Ready checkout.
    /// </summary>
    [Test]
    public async Task C1082_DueDebtFastForwardsDesktopAndMarksReady()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var counter = new StatementCounter();
        world.Interceptors.Add(counter);
        await world.RestartServicesAsync();
        await world.SweepSettlementSyncAsync();
        counter.Reset();

        (await world.SweepSettlementSyncAsync()).ShouldBe(0, "an empty debt table attempts no Git");
        counter.Statements.ShouldBe(1, "one indexed query per tick when nothing is due");
        var sql = counter.Texts.Single();
        sql.ShouldContain("AgentTaskSyncDebts");
        sql.ShouldContain("NextAttemptAt");
        sql.ShouldNotContain("UPDATE");

        world.Services.GetRequiredService<IOptions<DelegationSettings>>().Value.RunnerSyncDebtOnSettlement = false;
        counter.Reset();
        (await world.SweepSettlementSyncAsync()).ShouldBe(0);
        counter.Statements.ShouldBe(1, "the kill switch adds no second query while the table is empty");

        var probeNow = world.DebtClock.GetUtcNow().UtcDateTime;
        await using (var db = world.CreateContext())
        {
            db.AgentTaskSyncDebts.Add(new AgentTaskSyncDebt
            {
                Id = Guid.NewGuid(),
                TaskId = world.TaskId,
                Attempt = world.Task.Attempt,
                SettlementEventId = Guid.NewGuid(),
                State = AgentTaskSyncDebtState.Held,
                ReasonCode = RemoteSettlementSyncReasons.TipNotReported,
                NextAttemptAt = probeNow.AddMinutes(60),
                CreatedAt = probeNow,
                UpdatedAt = probeNow,
            });
            await db.SaveChangesAsync();
        }
        counter.Reset();
        (await world.SweepSettlementSyncAsync()).ShouldBe(0, "a not-due Held row attempts nothing");
        counter.Statements.ShouldBe(1, "a not-due Held row is still one statement per tick");
        var heldSql = counter.Texts.Single();
        heldSql.ShouldContain("AgentTaskSyncDebts");
        heldSql.ShouldContain("NextAttemptAt");
        heldSql.ShouldContain("IS NULL");
        heldSql.ShouldNotContain("UPDATE");
        await using (var db = world.CreateContext())
        {
            var probe = await db.AgentTaskSyncDebts.SingleAsync(d => d.TaskId == world.TaskId);
            db.AgentTaskSyncDebts.Remove(probe);
            await db.SaveChangesAsync();
        }

        var source = await world.Git.RunnerPushAsync("work.txt", "recorded");
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        await SeedAsync(world, source, evidence: "{\"remoteSync\":{\"state\":\"Pending\",\"confirmedSha\":null}}");
        var before = await HistoryAsync(world);
        var bus = (MockEventBus)world.Services.GetRequiredService<IEventBus>();
        bus.Clear();
        world.Git.Git.Clear();

        (await world.SweepSettlementSyncAsync()).ShouldBe(1);
        var debt = await DebtAsync(world);
        debt.State.ShouldBe(AgentTaskSyncDebtState.Ready);
        debt.ConfirmedSha.ShouldBe(source);
        debt.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.SettlementSyncReady);
        debt.SourceReadyAt.ShouldNotBeNull();
        debt.NextAttemptAt.ShouldBeNull();
        (await world.Git.HeadAsync()).ShouldBe(source);
        world.Git.Git.Commands.Where(c => c.Contains("--ff-only", StringComparison.Ordinal))
            .ShouldAllBe(c => c.EndsWith(" " + source, StringComparison.Ordinal));
        File.ReadAllText(Path.Combine(world.Git.Worktree, "work.txt")).ShouldBe("recorded");
        bus.PublishedEvents.ShouldContain(e => e.Group == "dashboard" && e.EventName == "AgentTaskChanged");
        (await HistoryAsync(world)).ShouldBe(before, "G-9 the ready sweep does not rewrite settlement");
        var readyAt = debt.SourceReadyAt;
        (await world.SweepSettlementSyncAsync()).ShouldBe(0, "Ready is not due again");
        (await DebtAsync(world)).SourceReadyAt.ShouldBe(readyAt);
        (await world.Git.HeadAsync()).ShouldBe(source);

        foreach (var cut in new[] { "claimed", "synced", "saved" })
            await AssertCrashRecoversAsync(cut);
    }

    /// <summary>
    /// V-15. An origin tip past the recorded source is Held <c>runner_sync_tip_not_reported</c>
    /// and the desktop stays put. A recorded source that does not descend from the baseline
    /// is Held <c>runner_sync_diverged</c> and is not checked out.
    /// </summary>
    [Test]
    public async Task C1082_AdvancedRemoteTipIsHeldNotFollowed()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var source = await world.Git.RunnerPushAsync("work.txt", "recorded");
        var advanced = await world.Git.RunnerPushAsync("later.txt", "advanced");
        advanced.ShouldNotBe(source);
        await SeedAsync(world, source);
        world.Git.Git.Clear();

        (await world.SweepSettlementSyncAsync()).ShouldBe(1);
        var debt = await DebtAsync(world);
        debt.State.ShouldBe(AgentTaskSyncDebtState.Held);
        debt.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.TipNotReported);
        debt.ConfirmedSha.ShouldBeNull();
        debt.SourceReadyAt.ShouldBeNull();
        debt.NextAttemptAt.ShouldBe(world.DebtClock.GetUtcNow().UtcDateTime.AddMinutes(60));
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        world.Git.Git.Commands.ShouldNotContain(c => c.Contains("--ff-only", StringComparison.Ordinal));
        File.Exists(Path.Combine(world.Git.Worktree, "later.txt")).ShouldBeFalse();
        File.Exists(Path.Combine(world.Git.Worktree, "work.txt")).ShouldBeFalse();

        await using var rewritten = await RunnerSettlementWorld.CreateAsync();
        await rewritten.Git.EnsureRunnerAsync();
        await rewritten.Git.RunAsync(rewritten.Git.Runner, "checkout", "--orphan", "rewritten");
        await rewritten.Git.TryRunAsync(rewritten.Git.Runner, "rm", "-rf", ".");
        await File.WriteAllTextAsync(Path.Combine(rewritten.Git.Runner, "orphan.txt"), "unrelated\n");
        await rewritten.Git.RunAsync(rewritten.Git.Runner, "add", "orphan.txt");
        await rewritten.Git.RunAsync(rewritten.Git.Runner, "commit", "-m", "unrelated");
        var orphan = await rewritten.Git.RunAsync(rewritten.Git.Runner, "rev-parse", "HEAD");
        await rewritten.Git.RunAsync(rewritten.Git.Runner, "push", "--force", "origin",
            orphan + ":refs/heads/" + rewritten.Git.Branch);
        await SeedAsync(rewritten, orphan);
        rewritten.Git.Git.Clear();

        (await rewritten.SweepSettlementSyncAsync()).ShouldBe(1);
        var refused = await DebtAsync(rewritten);
        refused.State.ShouldBe(AgentTaskSyncDebtState.Held);
        refused.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.Diverged);
        refused.ConfirmedSha.ShouldBeNull();
        refused.NextAttemptAt.ShouldBe(rewritten.DebtClock.GetUtcNow().UtcDateTime.AddMinutes(60));
        (await rewritten.Git.HeadAsync()).ShouldBe(rewritten.Git.Baseline);
        rewritten.Git.Git.Commands.ShouldNotContain(c => c.Contains("--ff-only", StringComparison.Ordinal));
        File.Exists(Path.Combine(rewritten.Git.Worktree, "orphan.txt")).ShouldBeFalse();
    }

    /// <summary>
    /// V-16. A busy lease returns at once and the row stays Pending on the 1/2/4/5-minute
    /// backoff, capped at 5. A second sweep while the claim is in Git finds the row not due.
    /// </summary>
    [Test]
    public async Task C1082_LeaseBusyDebtBacksOffAndStaysPending()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var source = await world.Git.RunnerPushAsync("work.txt", "recorded");
        await SeedAsync(world, source);
        var lease = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None);
        lease.ShouldNotBeNull("hold the desktop repository lease");
        try
        {
            var overlapped = false;
            await using (var scope = world.Services.CreateAsyncScope())
            {
                var recovery = scope.ServiceProvider.GetRequiredService<SettlementSyncRecoveryService>();
                recovery.BoundaryAsync = async (name, _) =>
                {
                    if (name != "claimed" || overlapped) return;
                    overlapped = true;
                    (await world.SweepSettlementSyncAsync()).ShouldBe(0,
                        "a debt claimed by the sweep in progress is not due until its backoff");
                };
                var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                (await dispatcher.RecoverSettlementSyncAsync(CancellationToken.None)).ShouldBe(1);
            }
            overlapped.ShouldBeTrue("the overlap probe must run after the claim commits");

            var attempt = 1;
            var row = await DebtAsync(world);
            row.State.ShouldBe(AgentTaskSyncDebtState.Pending);
            row.Attempts.ShouldBe(attempt);
            row.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
            row.NextAttemptAt.ShouldBe(world.DebtClock.GetUtcNow().UtcDateTime.AddMinutes(1));
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);

            var previous = 1;
            foreach (var minutes in new[] { 2, 4, 5, 5 })
            {
                world.DebtClock.Advance(TimeSpan.FromMinutes(previous));
                (await world.SweepSettlementSyncAsync()).ShouldBe(1);
                row = await DebtAsync(world);
                row.State.ShouldBe(AgentTaskSyncDebtState.Pending);
                row.Attempts.ShouldBe(++attempt);
                row.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
                row.NextAttemptAt.ShouldBe(world.DebtClock.GetUtcNow().UtcDateTime.AddMinutes(minutes),
                    "backoff " + minutes);
                (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
                (await world.SweepSettlementSyncAsync()).ShouldBe(0, "future due stays skipped");
                previous = minutes;
            }
        }
        finally
        {
            if (lease is not null) await lease.DisposeAsync();
        }

        world.DebtClock.Advance(TimeSpan.FromMinutes(5));
        (await world.SweepSettlementSyncAsync()).ShouldBe(1);
        var ready = await DebtAsync(world);
        ready.State.ShouldBe(AgentTaskSyncDebtState.Ready);
        ready.ConfirmedSha.ShouldBe(source);
        (await world.Git.HeadAsync()).ShouldBe(source);
    }

    /// <summary>V-17. Dirty, sequencing and diverged desktop checkouts are Held with their sync reason.</summary>
    [Test]
    [Arguments("dirty")]
    [Arguments("sequencer")]
    [Arguments("diverged")]
    public async Task C1082_DesktopRefusalsHoldWithReason(string scenario)
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var source = await world.Git.RunnerPushAsync("work.txt", "recorded");
        string head;
        if (scenario == "dirty")
        {
            await File.WriteAllTextAsync(Path.Combine(world.Git.Worktree, "dirty.txt"), "keep dirty bytes");
            head = world.Git.Baseline;
        }
        else if (scenario == "sequencer")
        {
            var gitDir = await world.Git.RunAsync(world.Git.Worktree, "rev-parse", "--absolute-git-dir");
            await File.WriteAllTextAsync(Path.Combine(gitDir, "CHERRY_PICK_HEAD"), world.Git.Baseline + "\n");
            head = world.Git.Baseline;
        }
        else
        {
            head = await world.Git.DesktopCommitAsync("desktop.txt", "desktop-only");
        }
        await SeedAsync(world, source);
        world.Git.Git.Clear();

        (await world.SweepSettlementSyncAsync()).ShouldBe(1);
        var debt = await DebtAsync(world);
        debt.State.ShouldBe(AgentTaskSyncDebtState.Held, scenario);
        debt.ReasonCode.ShouldBe(scenario switch
        {
            "dirty" => RemoteSettlementSyncReasons.Dirty,
            "sequencer" => RemoteSettlementSyncReasons.Sequencer,
            _ => RemoteSettlementSyncReasons.Diverged,
        }, scenario);
        debt.ConfirmedSha.ShouldBeNull(scenario);
        debt.NextAttemptAt.ShouldBe(world.DebtClock.GetUtcNow().UtcDateTime.AddMinutes(60), scenario);
        (await world.Git.HeadAsync()).ShouldBe(head, scenario);
        world.Git.Git.Commands.ShouldNotContain(c => c.Contains("--ff-only", StringComparison.Ordinal), scenario);
        if (scenario == "dirty")
            (await File.ReadAllTextAsync(Path.Combine(world.Git.Worktree, "dirty.txt"))).ShouldBe("keep dirty bytes");
    }

    /// <summary>
    /// V-18. A changed attempt is Held <c>settlement_sync_episode_changed</c>. A retirement row,
    /// or a worktree directory that is already gone, ends Superseded and does not fast-forward.
    /// </summary>
    [Test]
    [Arguments("attempt")]
    [Arguments("retired")]
    public async Task C1082_ChangedEpisodeOrRetiredWorktreeEndsTheDebt(string scenario)
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var source = await world.Git.RunnerPushAsync("work.txt", "recorded");
        await SeedAsync(world, source);
        if (scenario == "attempt")
        {
            await using var db = world.CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            task.Attempt++;
            await db.SaveChangesAsync();
        }
        else
        {
            await using var db = world.CreateContext();
            var now = world.DebtClock.GetUtcNow().UtcDateTime;
            db.TaskWorktreeRetirements.Add(new TaskWorktreeRetirement
            {
                Id = Guid.NewGuid(),
                TaskId = world.TaskId,
                TaskAttempt = world.Task.Attempt,
                TerminalStatus = AgentTaskStatus.Succeeded,
                TaskCompletedAt = now,
                ReleasedTaskRevision = Guid.NewGuid(),
                CallerIdentity = "sweep",
                ReleaseReason = "retired",
                ReleasedAt = now,
                RepositoryPath = world.Git.Desktop,
                CommonDirectory = world.Git.Desktop,
                WorktreePath = world.Git.Worktree,
                GitDirectory = world.Git.Desktop,
                SourceFullRef = world.Git.FullRef,
                SourceSha = source,
                TargetFullRef = "refs/heads/master",
                State = WorktreeRetirementState.Released,
                Active = true,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }
        world.Git.Git.Clear();

        (await world.SweepSettlementSyncAsync()).ShouldBe(0, scenario + " ends before Git");
        var debt = await DebtAsync(world);
        if (scenario == "attempt")
        {
            debt.State.ShouldBe(AgentTaskSyncDebtState.Held);
            debt.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.SettlementSyncEpisodeChanged);
            debt.NextAttemptAt.ShouldBe(world.DebtClock.GetUtcNow().UtcDateTime.AddMinutes(60), scenario);
        }
        else
        {
            debt.State.ShouldBe(AgentTaskSyncDebtState.Superseded);
            debt.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.SettlementSyncSuperseded);
            debt.NextAttemptAt.ShouldBeNull(scenario);
        }
        debt.ConfirmedSha.ShouldBeNull(scenario);
        debt.Attempts.ShouldBe(0, scenario);
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline, scenario);
        world.Git.Git.Commands.ShouldNotContain(c => c.Contains("--ff-only", StringComparison.Ordinal), scenario);

        if (scenario != "retired") return;

        await using var missing = await RunnerSettlementWorld.CreateAsync();
        var missingSource = await missing.Git.RunnerPushAsync("work.txt", "recorded");
        await SeedAsync(missing, missingSource);
        foreach (var file in Directory.EnumerateFiles(missing.Git.Worktree, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(missing.Git.Worktree, recursive: true);
        missing.Git.Git.Clear();

        (await missing.SweepSettlementSyncAsync()).ShouldBe(0, "a missing worktree is not fast-forwarded");
        var gone = await DebtAsync(missing);
        gone.State.ShouldBe(AgentTaskSyncDebtState.Superseded);
        gone.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.SettlementSyncSuperseded);
        gone.Attempts.ShouldBe(0);
        missing.Git.Git.Commands.ShouldNotContain(c => c.Contains("--ff-only", StringComparison.Ordinal));
    }

    /// <summary>
    /// V-19. Ready changes the debt row only. Evidence, status, stage outcomes, task events
    /// and completion obligations stay byte-for-byte.
    /// </summary>
    [Test]
    public async Task C1082_ReadyDebtLeavesSettlementEvidenceAndOutcomesImmutable()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var source = await world.Git.RunnerPushAsync("work.txt", "recorded");
        const string evidence = "{\"remoteSync\":{\"state\":\"Pending\",\"observedSha\":\"abc\",\"confirmedSha\":null}}";
        await SeedAsync(world, source, evidence: evidence);
        var now = world.DebtClock.GetUtcNow().UtcDateTime;
        var eventId = Guid.NewGuid();
        await using (var db = world.CreateContext())
        {
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = eventId,
                AgentTaskId = world.TaskId,
                Type = AgentTaskEventType.Completed,
                Detail = "settled",
                At = now,
            });
            db.StageOutcomes.Add(new StageOutcome
            {
                Id = Guid.NewGuid(),
                Stage = OrchestrationStage.Review,
                Outcome = StageOutcomeKind.Clean,
                Source = StageOutcomeSource.Delegate,
                SubjectTaskId = world.TaskId,
                StageTaskId = world.TaskId,
                Detail = "bound",
                ReviewedSourceSha = source,
                RecordedAt = now,
            });
            db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
            {
                Id = Guid.NewGuid(),
                TaskId = world.TaskId,
                SourceEventId = eventId,
                Kind = LandNotificationKind.TaskCompletion,
                ReplyTo = AgentTaskReplyTo.Session,
                Body = "completion",
                ContentDigest = "digest",
                CreatedAt = now,
                NextAttemptAt = now,
                State = LandNotificationState.Confirmed,
            });
            await db.SaveChangesAsync();
        }
        var before = await HistoryAsync(world);

        (await world.SweepSettlementSyncAsync()).ShouldBe(1);
        (await DebtAsync(world)).State.ShouldBe(AgentTaskSyncDebtState.Ready);
        (await world.Git.HeadAsync()).ShouldBe(source);
        var after = await HistoryAsync(world);
        after.ShouldBe(before, "G-9 settlement evidence, outcomes, events and obligations stay immutable");
        await world.ReloadAsync();
        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded);
    }

    /// <summary>
    /// V-24. Task detail exposes the debt row beside immutable Pending evidence.
    /// A task with no row leaves syncDebt null.
    /// </summary>
    [Test]
    public async Task C1082_TaskDetailExposesSyncDebt()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        (await DetailAsync(world)).SyncDebt.ShouldBeNull();

        var source = await world.Git.RunnerPushAsync("work.txt", "recorded");
        var evidence = TaskProgressJson.SerializeEvidence(new CompletionProgressEvidence(
            1,
            CompletionProgressAssessment.ProgressObserved,
            RemoteSync: new RemoteSyncEvidence(
                1,
                RemoteSettlementSyncState.Pending,
                ObservedSha: source,
                Reason: RemoteSettlementSyncReasons.LeaseBusy)));
        await SeedAsync(world, source, evidence);

        var detail = await DetailAsync(world);
        var debt = detail.SyncDebt.ShouldNotBeNull();
        debt.State.ShouldBe(nameof(AgentTaskSyncDebtState.Pending));
        debt.SourceSha.ShouldBe(source);
        debt.ConfirmedSha.ShouldBeNull();
        debt.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
        debt.Attempts.ShouldBe(0);
        debt.NextAttemptAt.ShouldNotBeNull();
        detail.ProgressEvidence.ShouldNotBeNull();
        detail.ProgressEvidence!.RemoteSync.ShouldNotBeNull();
        detail.ProgressEvidence.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Pending);
        detail.ProgressEvidence.RemoteSync.ObservedSha.ShouldBe(source);
        detail.ProgressEvidence.RemoteSync.ConfirmedSha.ShouldBeNull();
    }

    /// <summary>
    /// CARD-1082 F3d. A Complete retirement records removal, and the debt row still has no
    /// registration id to bind that retirement to. The due re-check stays Held, keeps the
    /// warning, and does not read the removed directory.
    /// </summary>
    [Test]
    public async Task C1136_CompleteRetirementWithoutRegistrationIdKeepsHeldDebt()
    {
        AssertHeldRecheckReadsNoFilesystem();
        await using var world = await SeedHeldTipAsync();
        var seeded = await DebtAsync(world);
        seeded.WorktreePath.ShouldNotBeNullOrWhiteSpace();
        var path = seeded.WorktreePath!;
        var head = await world.Git.HeadAsync();
        var gitDir = await world.Git.RunAsync(path, "rev-parse", "--absolute-git-dir");
        await world.Git.RunAsync(world.Git.Desktop, "worktree", "remove", "--force", path);
        Directory.Exists(path).ShouldBeFalse();
        Directory.Exists(gitDir).ShouldBeFalse();
        await AddRetirementAsync(world, seeded, WorktreeRetirementState.Complete, active: true,
            committedRemoval: true, gitDirectory: gitDir);

        await AssertNotDueAsync(world, seeded, "committed removal is not due yet");
        world.DebtClock.Advance(TimeSpan.FromMinutes(60));
        await AssertHeldRescheduledAsync(world, seeded, "a complete retirement has no registration id", head: null);
        Directory.Exists(path).ShouldBeFalse();
        Directory.Exists(gitDir).ShouldBeFalse();
        (await world.Git.RunAsync(world.Git.Desktop, "rev-parse", world.Git.FullRef)).ShouldBe(head);
    }

    /// <summary>
    /// CARD-1082 F3c D1. A Complete retirement of an earlier registration does not
    /// supersede the live debt after the same path is registered again. Temporary
    /// absence of the recreated directory stays Held and moves the next attempt
    /// forward 60 minutes. Restoring the directory keeps the warning.
    /// </summary>
    [Test]
    public async Task C1136_RecreatedRegistrationKeepsHeldDebtAndReschedules()
    {
        AssertHeldRecheckReadsNoFilesystem();
        await using var world = await SeedHeldTipAsync();
        var seeded = await DebtAsync(world);
        seeded.WorktreePath.ShouldNotBeNullOrWhiteSpace();
        var path = seeded.WorktreePath!;
        var head = await world.Git.HeadAsync();
        var history = await HistoryAsync(world);
        var gitDir = await world.Git.RunAsync(path, "rev-parse", "--absolute-git-dir");
        await world.Git.RunAsync(world.Git.Desktop, "worktree", "remove", "--force", path);
        await world.Git.RunAsync(world.Git.Desktop, "branch", "-D", world.Git.Branch);
        Directory.Exists(path).ShouldBeFalse();
        Directory.Exists(gitDir).ShouldBeFalse();
        await AddRetirementAsync(world, seeded, WorktreeRetirementState.Complete, active: true,
            committedRemoval: true, gitDirectory: gitDir);
        await world.Git.RunAsync(world.Git.Desktop, "worktree", "add", "-b", world.Git.Branch, path, head);
        (await world.Git.RunAsync(world.Git.Desktop, "worktree", "list", "--porcelain"))
            .ShouldContain("worktree " + path);

        await AssertNotDueAsync(world, seeded, "recreated registration is not due yet");
        world.DebtClock.Advance(TimeSpan.FromMinutes(59));
        world.Git.Git.Clear();
        (await world.SweepSettlementSyncAsync()).ShouldBe(0, "minute 59 is not due");
        (await DebtAsync(world)).Revision.ShouldBe(seeded.Revision);

        var aside = path + "-f3c-aside";
        PrepareTree(path);
        Directory.Move(path, aside);
        try
        {
            world.DebtClock.Advance(TimeSpan.FromMinutes(1));
            var counter = new StatementCounter();
            world.Interceptors.Add(counter);
            await world.RestartServicesAsync();
            var due = world.DebtClock.GetUtcNow().UtcDateTime;
            world.Git.Git.Clear();
            counter.Reset();
            (await world.SweepSettlementSyncAsync()).ShouldBe(0, "a recreated registration is not an attempt");
            counter.Statements.ShouldBe(5, "a due Held re-check is one reschedule and no retirement read");
            var debt = await DebtAsync(world);
            debt.State.ShouldBe(AgentTaskSyncDebtState.Held);
            debt.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.TipNotReported);
            debt.Attempts.ShouldBe(seeded.Attempts);
            debt.Revision.ShouldBe(seeded.Revision + 1);
            debt.NextAttemptAt.ShouldBe(due.AddMinutes(60));
            world.Git.Git.Commands.ShouldBeEmpty();
            (await HistoryAsync(world)).ShouldBe(history);
            Directory.Exists(path).ShouldBeFalse();

            Directory.Move(aside, path);
            aside = "";
            world.DebtClock.Advance(TimeSpan.FromMinutes(60));
            await AssertHeldRescheduledAsync(world, debt, "restored recreated registration stays Held", head);
            Directory.Exists(path).ShouldBeTrue();
        }
        finally
        {
            if (aside.Length > 0 && Directory.Exists(aside) && !Directory.Exists(path))
                Directory.Move(aside, path);
        }
    }

    /// <summary>
    /// CARD-1082 F3d round 3. An empty gitdir, a gitdir held so a second read fails, a
    /// symlink alias, and a worktree moved onto the task row are not identity. Each stays
    /// Held. The Held restamp closure, including its helpers, touches no filesystem.
    /// </summary>
    [Test]
    [Arguments("empty-pointer")]
    [Arguments("unreadable")]
    [Arguments("symlink")]
    [Arguments("moved")]
    public async Task C1136_AliasOrUnreadableRegistrationKeepsHeldDebtAndReschedules(string scenario)
    {
        AssertHeldRecheckReadsNoFilesystem();
        await using var world = await SeedHeldTipAsync();
        var seeded = await DebtAsync(world);
        seeded.WorktreePath.ShouldNotBeNullOrWhiteSpace();
        var path = seeded.WorktreePath!;
        var gitDir = await world.Git.RunAsync(path, "rev-parse", "--absolute-git-dir");
        await world.Git.RunAsync(world.Git.Desktop, "worktree", "remove", "--force", path);
        await AddRetirementAsync(world, seeded, WorktreeRetirementState.Complete, active: true,
            committedRemoval: true, gitDirectory: gitDir);

        var scratch = Path.Combine(world.Git.Desktop, ".git", "worktrees", "f3d-" + scenario);
        var moved = path + "-f3d-moved";
        string? link = null;
        FileStream? unreadableGate = null;
        try
        {
            if (scenario == "moved")
            {
                Directory.CreateDirectory(moved);
                await using var db = world.CreateContext();
                var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
                task.WorktreePath = moved;
                await db.SaveChangesAsync();
            }
            else
            {
                Directory.CreateDirectory(scratch);
                var gitdir = Path.Combine(scratch, "gitdir");
                if (scenario == "empty-pointer")
                    await File.WriteAllTextAsync(gitdir, "");
                else if (scenario == "unreadable")
                {
                    await File.WriteAllTextAsync(gitdir, path + "\n");
                    unreadableGate = new FileStream(gitdir, FileMode.Open, FileAccess.Read, FileShare.None);
                    AssertExclusiveRegistrationUnreadable(gitdir);
                }
                else
                {
                    link = Path.Combine(Path.GetTempPath(), "f3d-alias-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateSymbolicLink(link, Path.GetDirectoryName(path)!);
                    var alias = Path.Combine(link, Path.GetFileName(path)!, ".git");
                    await File.WriteAllTextAsync(gitdir, alias + "\n");
                }
            }

            world.DebtClock.Advance(TimeSpan.FromMinutes(60));
            await AssertHeldRescheduledAsync(world, seeded, scenario, head: null);
            if (scenario == "moved")
            {
                await using var db = world.CreateContext();
                (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == world.TaskId))
                    .WorktreePath.ShouldBe(moved, scenario);
                Directory.Exists(moved).ShouldBeTrue(scenario);
            }
        }
        finally
        {
            unreadableGate?.Dispose();
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
            if (link is not null && Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(moved))
                Directory.Delete(moved, recursive: true);
        }
    }

    /// <summary>
    /// CARD-1136 F3b. A revoked retirement is not proof the worktree is gone. The due
    /// re-check keeps the Held warning and moves the next attempt forward 60 minutes,
    /// whether the recorded directory is still present or temporarily absent.
    /// </summary>
    [Test]
    [Arguments("live")]
    [Arguments("absent")]
    public async Task C1136_RevokedRetirementKeepsHeldDebtAndReschedules(string scenario)
    {
        AssertHeldRecheckReadsNoFilesystem();
        await using var world = await SeedHeldTipAsync();
        var seeded = await DebtAsync(world);
        seeded.WorktreePath.ShouldNotBeNullOrWhiteSpace();
        var path = seeded.WorktreePath!;
        var head = await world.Git.HeadAsync();
        await AddRetirementAsync(world, seeded, WorktreeRetirementState.Revoked, active: false, committedRemoval: false);
        if (scenario == "absent")
            DeleteTree(path);

        await AssertNotDueAsync(world, seeded, scenario + " is not due yet");
        world.DebtClock.Advance(TimeSpan.FromMinutes(60));
        await AssertHeldRescheduledAsync(world, seeded, scenario, scenario == "live" ? head : null);
        if (scenario == "absent")
            Directory.Exists(path).ShouldBeFalse(scenario);
        else
            Directory.Exists(path).ShouldBeTrue(scenario);
    }

    /// <summary>
    /// CARD-1136 F3b. A missing worktree directory without a completed retirement is not
    /// permanent. The row stays Held across the absence and again after the directory returns.
    /// </summary>
    [Test]
    public async Task C1136_TemporaryWorktreeAbsenceKeepsHeldDebtAndReschedules()
    {
        AssertHeldRecheckReadsNoFilesystem();
        await using var world = await SeedHeldTipAsync();
        var seeded = await DebtAsync(world);
        seeded.WorktreePath.ShouldNotBeNullOrWhiteSpace();
        var path = seeded.WorktreePath!;
        var head = await world.Git.HeadAsync();
        var aside = path + "-f3b-aside";
        PrepareTree(path);
        Directory.Move(path, aside);
        try
        {
            await AssertNotDueAsync(world, seeded, "absence is not due yet");
            world.DebtClock.Advance(TimeSpan.FromMinutes(60));
            await AssertHeldRescheduledAsync(world, seeded, "absent path stays Held", head: null);
            Directory.Exists(path).ShouldBeFalse();

            var mid = await DebtAsync(world);
            Directory.Move(aside, path);
            aside = "";
            world.DebtClock.Advance(TimeSpan.FromMinutes(60));
            await AssertHeldRescheduledAsync(world, mid, "restored path stays Held", head);
            Directory.Exists(path).ShouldBeTrue();
        }
        finally
        {
            if (aside.Length > 0 && Directory.Exists(aside) && !Directory.Exists(path))
                Directory.Move(aside, path);
        }
    }

    /// <summary>
    /// CARD-1136 F3b. Released, partial, or contradicted retirement evidence does not
    /// supersede. A blank recorded path is not a removed registration.
    /// </summary>
    [Test]
    [Arguments("released-absent")]
    [Arguments("complete-present")]
    [Arguments("blank-path")]
    public async Task C1136_UncertainHeldRegistrationKeepsHeldDebtAndReschedules(string scenario)
    {
        AssertHeldRecheckReadsNoFilesystem();
        await using var world = await SeedHeldTipAsync();
        var seeded = await DebtAsync(world);
        seeded.WorktreePath.ShouldNotBeNullOrWhiteSpace();
        var path = seeded.WorktreePath!;
        var head = await world.Git.HeadAsync();
        if (scenario == "released-absent")
        {
            await AddRetirementAsync(world, seeded, WorktreeRetirementState.Released, active: true, committedRemoval: false);
            DeleteTree(path);
        }
        else if (scenario == "complete-present")
            await AddRetirementAsync(world, seeded, WorktreeRetirementState.Complete, active: true, committedRemoval: true);
        else
        {
            await using var db = world.CreateContext();
            var row = await db.AgentTaskSyncDebts.SingleAsync(d => d.Id == seeded.Id);
            row.WorktreePath = null;
            await db.SaveChangesAsync();
        }

        await AssertNotDueAsync(world, seeded, scenario + " is not due yet");
        world.DebtClock.Advance(TimeSpan.FromMinutes(60));
        await AssertHeldRescheduledAsync(world, seeded, scenario, scenario == "released-absent" ? null : head);
    }

    /// <summary>
    /// CARD-1136 F3b. A retirement read that throws is not proof of removal. The due
    /// re-check keeps Held and still moves the next attempt forward 60 minutes.
    /// </summary>
    [Test]
    public async Task C1136_HeldRecheckReadFailureKeepsHeldDebtAndReschedules()
    {
        AssertHeldRecheckReadsNoFilesystem();
        await using var world = await SeedHeldTipAsync();
        var seeded = await DebtAsync(world);
        var head = await world.Git.HeadAsync();
        world.Interceptors.Add(new RetirementReadFailure());
        await world.RestartServicesAsync();
        world.DebtClock.Advance(TimeSpan.FromMinutes(60));
        await AssertHeldRescheduledAsync(world, seeded, "retirement read failed", head);
    }

    /// <summary>
    /// V-9. After 60 minutes a Held row whose worktree is still registered stays Held, moves
    /// <c>NextAttemptAt</c> forward 60 minutes, advances <c>Revision</c>, runs no Git, counts
    /// no attempt, and still warns.
    /// </summary>
    [Test]
    public async Task C1136_HeldDebtWithALiveWorktreeStaysHeldAndReschedules()
    {
        AssertHeldRecheckReadsNoFilesystem();
        await using var world = await SeedHeldTipAsync();
        var before = await DebtAsync(world);
        var head = await world.Git.HeadAsync();
        var counter = new StatementCounter();
        world.Interceptors.Add(counter);
        await world.RestartServicesAsync();

        (await world.SweepSettlementSyncAsync()).ShouldBe(0, "a not-due Held row is not claimed");
        counter.Statements.ShouldBe(1, "a not-due Held row is still one statement per tick");
        var sql = counter.Texts.Single();
        sql.ShouldContain("AgentTaskSyncDebts");
        sql.ShouldContain("NextAttemptAt");
        sql.ShouldContain("IS NULL");
        sql.ShouldNotContain("UPDATE");
        (await DebtAsync(world)).Revision.ShouldBe(before.Revision);

        world.DebtClock.Advance(TimeSpan.FromMinutes(60));
        world.Git.Git.Clear();
        counter.Reset();
        (await world.SweepSettlementSyncAsync()).ShouldBe(0, "a Held re-check is not an attempt");
        counter.Statements.ShouldBe(5, "a due Held re-check is one reschedule and no retirement read");
        var debt = await DebtAsync(world);
        debt.State.ShouldBe(AgentTaskSyncDebtState.Held);
        debt.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.TipNotReported);
        debt.NextAttemptAt.ShouldBe(before.NextAttemptAt!.Value.AddMinutes(60));
        debt.NextAttemptAt.ShouldBe(world.DebtClock.GetUtcNow().UtcDateTime.AddMinutes(60));
        debt.Revision.ShouldBe(before.Revision + 1);
        debt.Attempts.ShouldBe(before.Attempts);
        world.Git.Git.Commands.ShouldBeEmpty();
        (await world.Git.HeadAsync()).ShouldBe(head);
        var item = SettlementSyncDebtAttention.Build(
            [debt], [world.Task], world.DebtClock.GetUtcNow().UtcDateTime, new DelegationSettings())
            .ShouldHaveSingleItem();
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.Kind.ShouldBe(AttentionKind.SessionDisagreement);
        item.ConditionKey.ShouldBe("settlement-sync-debt:" + debt.Id.ToString("D"));
    }

    private static async Task<RunnerSettlementWorld> SeedHeldTipAsync()
    {
        var world = await RunnerSettlementWorld.CreateAsync();
        var source = await world.Git.RunnerPushAsync("work.txt", "recorded");
        var advanced = await world.Git.RunnerPushAsync("later.txt", "advanced");
        advanced.ShouldNotBe(source);
        await SeedAsync(world, source);
        world.Git.Git.Clear();
        (await world.SweepSettlementSyncAsync()).ShouldBe(1);
        var debt = await DebtAsync(world);
        debt.State.ShouldBe(AgentTaskSyncDebtState.Held);
        debt.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.TipNotReported);
        debt.Attempts.ShouldBe(1);
        debt.NextAttemptAt.ShouldBe(world.DebtClock.GetUtcNow().UtcDateTime.AddMinutes(60));
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        world.Git.Git.Clear();
        return world;
    }

    private static async Task AssertNotDueAsync(
        RunnerSettlementWorld world, AgentTaskSyncDebt before, string because)
    {
        world.Git.Git.Clear();
        (await world.SweepSettlementSyncAsync()).ShouldBe(0, because);
        var early = await DebtAsync(world);
        early.State.ShouldBe(AgentTaskSyncDebtState.Held, because);
        early.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.TipNotReported, because);
        early.Attempts.ShouldBe(before.Attempts, because);
        early.Revision.ShouldBe(before.Revision, because);
        early.NextAttemptAt.ShouldBe(before.NextAttemptAt, because);
        world.Git.Git.Commands.ShouldBeEmpty(because);
    }

    private static async Task AssertHeldRescheduledAsync(
        RunnerSettlementWorld world, AgentTaskSyncDebt before, string because, string? head)
    {
        var due = world.DebtClock.GetUtcNow().UtcDateTime;
        world.Git.Git.Clear();
        (await world.SweepSettlementSyncAsync()).ShouldBe(0, because);
        var debt = await DebtAsync(world);
        debt.State.ShouldBe(AgentTaskSyncDebtState.Held, because);
        debt.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.TipNotReported, because);
        debt.Attempts.ShouldBe(before.Attempts, because);
        debt.Revision.ShouldBe(before.Revision + 1, because);
        debt.NextAttemptAt.ShouldBe(due.AddMinutes(60), because);
        world.Git.Git.Commands.ShouldBeEmpty(because);
        if (head is not null)
            (await world.Git.HeadAsync()).ShouldBe(head, because);
        var item = SettlementSyncDebtAttention.Build(
            [debt], [world.Task], due, new DelegationSettings()).ShouldHaveSingleItem();
        item.Severity.ShouldBe(AlertSeverity.Warning, because);
        item.Kind.ShouldBe(AttentionKind.SessionDisagreement, because);
        item.ConditionKey.ShouldBe("settlement-sync-debt:" + debt.Id.ToString("D"), because);
    }

    private static async Task AddRetirementAsync(
        RunnerSettlementWorld world,
        AgentTaskSyncDebt debt,
        WorktreeRetirementState state,
        bool active,
        bool committedRemoval,
        string? gitDirectory = null)
    {
        await using var db = world.CreateContext();
        var now = world.DebtClock.GetUtcNow().UtcDateTime;
        db.TaskWorktreeRetirements.Add(new TaskWorktreeRetirement
        {
            Id = Guid.NewGuid(),
            TaskId = world.TaskId,
            TaskAttempt = debt.Attempt,
            TerminalStatus = AgentTaskStatus.Succeeded,
            TaskCompletedAt = now,
            ReleasedTaskRevision = Guid.NewGuid(),
            CallerIdentity = "sweep",
            ReleaseReason = "retired",
            ReleasedAt = now,
            RepositoryPath = world.Git.Desktop,
            CommonDirectory = world.Git.Desktop,
            WorktreePath = debt.WorktreePath ?? "",
            GitDirectory = gitDirectory ?? world.Git.Desktop,
            SourceFullRef = world.Git.FullRef,
            SourceSha = debt.SourceSha ?? "",
            TargetFullRef = "refs/heads/master",
            State = state,
            Active = active,
            DirectoryRemovedAt = committedRemoval ? now : null,
            RegistrationRemovedAt = committedRemoval ? now : null,
            RetirementCompletedAt = committedRemoval ? now : null,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static void PrepareTree(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
    }

    private static void DeleteTree(string path)
    {
        PrepareTree(path);
        Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// The Held claim restamps the debt and does not read a retirement or the filesystem.
    /// The checked closure is the Held-reachable part of <c>ClaimAsync</c> (the shared prefix,
    /// the Held branch, and the rescue tail; not the Pending else) plus every same-class method
    /// it calls: <c>LockTaskAsync</c>, <c>RescheduleHeldAsync</c>, and
    /// <c>RescheduleHeldInNewTransactionAsync</c>. A filesystem, retirement, or Git call in any
    /// of those methods fails this pin.
    /// </summary>
    private static void AssertHeldRecheckReadsNoFilesystem()
    {
        var source = ReadRepoFile("server/Application/Services/SettlementSyncRecoveryService.cs");
        var closure = ScanHeldRestampClosure(source);
        string.Join(", ", closure.Methods).ShouldBe(
            "ClaimAsync, LockTaskAsync, RescheduleHeldAsync, RescheduleHeldInNewTransactionAsync",
            "Held restamp call-path closure");
        closure.FilesystemAccess.ShouldBeEmpty(
            "Held restamp closure " + string.Join(", ", closure.Methods));
    }

    /// <summary>A second open must fail while the registration file is held exclusively.</summary>
    private static void AssertExclusiveRegistrationUnreadable(string gitdir)
    {
        IOException? denied = null;
        try
        {
            using var probe = new FileStream(gitdir, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (IOException ex)
        {
            denied = ex;
        }

        denied.ShouldNotBeNull("the gitdir registration is held exclusively and cannot be read");
    }

    private readonly record struct HeldRestampScan(IReadOnlyList<string> Methods, IReadOnlyList<string> FilesystemAccess);

    private static HeldRestampScan ScanHeldRestampClosure(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var type = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == "SettlementSyncRecoveryService");
        var methods = type.Members.OfType<MethodDeclarationSyntax>()
            .ToDictionary(method => method.Identifier.ValueText, StringComparer.Ordinal);
        var claim = methods["ClaimAsync"];
        var heldIf = claim.DescendantNodes().OfType<IfStatementSyntax>().Single(statement =>
            statement.Condition.ToString().Contains("debt.State == AgentTaskSyncDebtState.Held", StringComparison.Ordinal));
        var pendingElse = heldIf.Else;
        bool HeldReachable(SyntaxNode node) => pendingElse is null || !pendingElse.Span.Contains(node.Span);

        var names = new List<string> { "ClaimAsync" };
        var seen = new HashSet<string>(StringComparer.Ordinal) { "ClaimAsync" };
        var scanned = new List<(string Name, SyntaxNode Node, Func<SyntaxNode, bool> Include)>
        {
            ("ClaimAsync", claim, HeldReachable),
        };
        var queue = new Queue<string>(LocalCalls(claim, HeldReachable, methods));
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!seen.Add(name)) continue;
            names.Add(name);
            var body = methods[name];
            scanned.Add((name, body, static _ => true));
            foreach (var called in LocalCalls(body, static _ => true, methods))
                queue.Enqueue(called);
        }

        var access = new List<string>();
        foreach (var (name, node, include) in scanned)
        {
            foreach (var hit in node.DescendantNodes().Where(include))
            {
                var described = DescribeFilesystemAccess(hit);
                if (described is not null)
                    access.Add(name + ": " + described);
            }
        }

        return new HeldRestampScan(names, access);
    }

    private static IEnumerable<string> LocalCalls(
        SyntaxNode node, Func<SyntaxNode, bool> include, Dictionary<string, MethodDeclarationSyntax> methods)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!include(call)) continue;
            var name = InvocationName(call);
            if (name is not null && methods.ContainsKey(name) && seen.Add(name))
                yield return name;
        }
    }

    private static string? InvocationName(InvocationExpressionSyntax call) => call.Expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => null,
    };

    private static string? DescribeFilesystemAccess(SyntaxNode node)
    {
        if (node is MemberAccessExpressionSyntax access)
        {
            var receiver = access.Expression.ToString();
            if (IsFilesystemReceiver(receiver))
                return receiver + "." + access.Name.Identifier.ValueText;
            if (access.Name.Identifier.ValueText is "TaskWorktreeRetirements" or "Superseded")
                return access.ToString();
        }

        if (node is ObjectCreationExpressionSyntax created && IsFilesystemType(created.Type.ToString()))
            return "new " + created.Type;

        if (node is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression)
            && literal.Token.ValueText.Contains("gitdir", StringComparison.Ordinal))
            return "gitdir";

        if (node is InvocationExpressionSyntax call
            && InvocationName(call) is "RegistrationGoneAsync" or "WorktreeMissing" or "SyncSettledAsync")
            return InvocationName(call);

        return null;
    }

    private static bool IsFilesystemReceiver(string receiver) =>
        receiver is "File" or "Directory" or "FileInfo" or "DirectoryInfo"
        || receiver.EndsWith(".File", StringComparison.Ordinal)
        || receiver.EndsWith(".Directory", StringComparison.Ordinal)
        || receiver.EndsWith(".FileInfo", StringComparison.Ordinal)
        || receiver.EndsWith(".DirectoryInfo", StringComparison.Ordinal);

    private static bool IsFilesystemType(string type) =>
        type is "FileStream" or "FileInfo" or "DirectoryInfo"
        || type.EndsWith(".FileStream", StringComparison.Ordinal)
        || type.EndsWith(".FileInfo", StringComparison.Ordinal)
        || type.EndsWith(".DirectoryInfo", StringComparison.Ordinal);

    private static string ReadRepoFile(string relative)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(directory, relative);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = Path.GetDirectoryName(directory)!;
        }

        throw new FileNotFoundException(relative);
    }

    /// <summary>Fails a retirement read. The Held re-check does not issue one; a reintroduced query that throws must still reschedule.</summary>
    private sealed class RetirementReadFailure : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            if (command.CommandText.Contains("TaskWorktreeRetirements", StringComparison.Ordinal))
                throw new IOException("retirement read unavailable");
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("TaskWorktreeRetirements", StringComparison.Ordinal))
                throw new IOException("retirement read unavailable");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static async Task<AgentTaskDetailDto> DetailAsync(RunnerSettlementWorld world)
    {
        await using var scope = world.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
            .GetAsync(world.TaskId, CancellationToken.None);
    }

    private static async Task AssertCrashRecoversAsync(string cut)
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var source = await world.Git.RunnerPushAsync("crash.txt", cut);
        await SeedAsync(world, source, evidence: "pending-bytes-" + cut);
        var before = await HistoryAsync(world);
        var fired = false;
        await using (var scope = world.Services.CreateAsyncScope())
        {
            var recovery = scope.ServiceProvider.GetRequiredService<SettlementSyncRecoveryService>();
            recovery.BoundaryAsync = (name, _) =>
            {
                if (name != cut) return Task.CompletedTask;
                fired = true;
                throw new IOException("simulated crash");
            };
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .RecoverSettlementSyncAsync(CancellationToken.None);
        }
        fired.ShouldBeTrue(cut);
        if (cut == "saved")
        {
            (await DebtAsync(world)).State.ShouldBe(AgentTaskSyncDebtState.Ready, cut);
            (await world.SweepSettlementSyncAsync()).ShouldBe(0, cut);
        }
        else
        {
            var pending = await DebtAsync(world);
            pending.State.ShouldBe(AgentTaskSyncDebtState.Pending, cut);
            pending.Attempts.ShouldBe(1, cut);
            world.DebtClock.Advance(TimeSpan.FromMinutes(1));
            (await world.SweepSettlementSyncAsync()).ShouldBe(1, cut);
            (await DebtAsync(world)).State.ShouldBe(AgentTaskSyncDebtState.Ready, cut);
        }
        (await world.Git.HeadAsync()).ShouldBe(source, cut);
        (await HistoryAsync(world)).ShouldBe(before, cut);
        var head = await world.Git.HeadAsync();
        (await world.SweepSettlementSyncAsync()).ShouldBe(0, cut);
        (await world.Git.HeadAsync()).ShouldBe(head, cut);
    }

    private static async Task SeedAsync(RunnerSettlementWorld world, string source, string? evidence = null)
    {
        var now = world.DebtClock.GetUtcNow().UtcDateTime;
        await using var db = world.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
        task.Status = AgentTaskStatus.Succeeded;
        if (evidence is not null) task.CompletionProgressEvidenceJson = evidence;
        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)!.Primary;
        db.AgentTaskSyncDebts.Add(new AgentTaskSyncDebt
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            Attempt = task.Attempt,
            SettlementEventId = Guid.NewGuid(),
            RunnerId = task.RunnerId,
            WorktreePath = task.WorktreePath,
            RemoteWorktreePath = task.RemoteWorktreePath,
            RepositoryPath = baseline.CanonicalRepository,
            FullRef = baseline.FullRef,
            BaselineSha = baseline.LocalSha,
            SourceSha = source,
            DesktopBeforeSha = world.Git.Baseline,
            EndpointFingerprint = baseline.Remote.EndpointFingerprint,
            State = AgentTaskSyncDebtState.Pending,
            ReasonCode = RemoteSettlementSyncReasons.LeaseBusy,
            NextAttemptAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        await world.ReloadAsync();
    }

    private static async Task<AgentTaskSyncDebt> DebtAsync(RunnerSettlementWorld world)
    {
        await using var db = world.CreateContext();
        return await db.AgentTaskSyncDebts.AsNoTracking().SingleAsync(d => d.TaskId == world.TaskId);
    }

    private static async Task<string> HistoryAsync(RunnerSettlementWorld world)
    {
        await using var db = world.CreateContext();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == world.TaskId);
        return JsonSerializer.Serialize(new
        {
            task.Status,
            task.Attempt,
            task.Result,
            task.CompletionProgressEvidenceJson,
            task.ProgressBaselineJson,
            task.WorktreePath,
            task.ConcurrencyToken,
            Events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == world.TaskId)
                .OrderBy(e => e.Id).Select(e => new { e.Id, e.Type, e.Detail }).ToListAsync(),
            Outcomes = await db.StageOutcomes.AsNoTracking()
                .Where(o => o.SubjectTaskId == world.TaskId || o.StageTaskId == world.TaskId)
                .OrderBy(o => o.Id)
                .Select(o => new { o.Id, o.Stage, o.Outcome, o.Detail, o.ReviewedSourceSha }).ToListAsync(),
            Notes = await db.AgentTaskLandNotifications.AsNoTracking().Where(n => n.TaskId == world.TaskId)
                .OrderBy(n => n.Id).Select(n => new { n.Id, n.Kind, n.Body, n.State, n.ContentDigest }).ToListAsync(),
        });
    }

    private sealed class StatementCounter : DbCommandInterceptor
    {
        public int Statements { get; private set; }
        public List<string> Texts { get; } = [];

        public void Reset()
        {
            Statements = 0;
            Texts.Clear();
        }

        private void Hit(DbCommand command)
        {
            Statements++;
            Texts.Add(command.CommandText);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Hit(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Hit(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Hit(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Hit(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
