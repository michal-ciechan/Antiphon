using System.Data.Common;
using System.Text.Json;
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
/// Six methods, nine results: CP-6 counts the three refusal rows and the two episode rows.
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
        debt.NextAttemptAt.ShouldBeNull();
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
        debt.NextAttemptAt.ShouldBeNull(scenario);
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
        }
        else
        {
            debt.State.ShouldBe(AgentTaskSyncDebtState.Superseded);
            debt.ReasonCode.ShouldBe(RemoteSettlementSyncReasons.SettlementSyncSuperseded);
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
