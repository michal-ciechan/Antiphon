using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using Rig = Antiphon.Tests.Application.RemoteWorkspacePreparerTests.Rig;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1895. A young terminal mirror is still <c>settling</c> for whole-tree retention and is the tree
/// whose build outputs are dropped. Every ineligible subject sits beside a young control that must be
/// sent, so "nothing was sent" cannot pass. The feature-absent case is the hold-the-line: zero drop frames.
/// </summary>
[Category("Integration")]
[Timeout(45_000)]
public sealed class C1895OutputDropServerTests
{
    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Young_settling_tree_is_dropped_beside_an_old_retain()
    {
        await using var w = await C1677RetentionServerTests.World.StartAsync(outputDrop: true);
        var old = await w.EligibleAsync(TimeSpan.FromHours(24));
        var young = await w.EligibleAsync(TimeSpan.FromHours(1));
        var blocked = await w.EligibleAsync(TimeSpan.FromHours(1), task => task.Status = AgentTaskStatus.Blocked);

        await w.TickAsync();

        w.Requests().Select(r => r.TaskId).ShouldBe([old]);
        var drop = w.DropRequests().ShouldHaveSingleItem();
        drop.TaskId.ShouldBe(young);
        drop.Mode.ShouldBe(WorkspaceOutputDropModes.Execute);
        w.DropRequests().ShouldNotContain(r => r.TaskId == old || r.TaskId == blocked);
        (await w.Rig.ReadTaskAsync(young)).RemoteWorktreePath.ShouldBe(C1677RetentionServerTests.World.PathOf(young));
        (await w.Rig.ReadTaskAsync(blocked)).RemoteWorktreePath.ShouldBe(C1677RetentionServerTests.World.PathOf(blocked));
        (await w.Rig.ReadTaskAsync(old)).RemoteWorktreePath.ShouldBeNull();

        var rows = await w.RowsAsync(young);
        var held = rows.Single(r => r.Lane == RunnerWorktreeRetentionService.LaneMirror);
        held.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        held.ReasonCode.ShouldBe("settling");
        var dropped = rows.Single(r => r.Lane == RunnerWorktreeRetentionService.LaneOutputDrop);
        dropped.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Dropped));
        dropped.ReasonCode.ShouldBe(WorkspaceOutputDropCodes.Dropped);
        dropped.RequestDigest.ShouldBe("4096");
        dropped.DirectoryRemoved.ShouldBe(false);
        dropped.RegistrationRemoved.ShouldBe(false);

        await using var db = w.Rig.NewDb();
        var events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == young).ToListAsync();
        events.ShouldContain(e => e.Type == AgentTaskEventType.Warning && e.Detail.StartsWith("runner_outputs_dropped", StringComparison.Ordinal));
    }

    [Test]
    [Arguments("session-running")]
    [Arguments("sync-debt-ready")]
    [Arguments("mutation")]
    [Arguments("land-pending")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Ineligible_subject_is_held_while_the_young_control_is_dropped(string mode)
    {
        await using var w = await C1677RetentionServerTests.World.StartAsync(outputDrop: true);
        var control = await w.EligibleAsync(TimeSpan.FromHours(1));
        Guid? sessionId = mode == "session-running" ? await SeedSessionAsync(w.Rig, SessionStatus.Running) : null;
        var subject = await w.EligibleAsync(TimeSpan.FromHours(1), task =>
        {
            switch (mode)
            {
                case "mutation": task.Role = AgentTaskRole.Mutation; break;
                case "land-pending": task.LandRequestedAt = w.Rig.Now; break;
                case "session-running": task.AgentSessionId = sessionId; break;
            }
        });
        if (mode == "sync-debt-ready")
            await SeedSyncDebtAsync(w.Rig, subject, AgentTaskSyncDebtState.Ready);

        await w.TickAsync();

        var drop = w.DropRequests().ShouldHaveSingleItem();
        drop.TaskId.ShouldBe(control);
        drop.Mode.ShouldBe(WorkspaceOutputDropModes.Execute);
        (await w.Rig.ReadTaskAsync(subject)).RemoteWorktreePath.ShouldBe(C1677RetentionServerTests.World.PathOf(subject));
        (await w.Rig.ReadTaskAsync(control)).RemoteWorktreePath.ShouldBe(C1677RetentionServerTests.World.PathOf(control));
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Missing_feature_sends_no_output_drop()
    {
        await using var w = await C1677RetentionServerTests.World.StartAsync(outputDrop: false);
        var old = await w.EligibleAsync();
        await w.EligibleAsync(TimeSpan.FromHours(1));

        await w.TickAsync();

        w.DropRequests().ShouldBeEmpty();
        w.Requests().ShouldHaveSingleItem().TaskId.ShouldBe(old);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Execute_off_sends_dry_run_and_does_not_back_off()
    {
        await using var w = await C1677RetentionServerTests.World.StartAsync(outputDrop: true);
        w.Settings.Execute = false;
        var young = await w.EligibleAsync(TimeSpan.FromHours(1));

        await w.TickAsync();
        await w.TickAsync();

        var drops = w.DropRequests();
        drops.Count.ShouldBe(2, "would_drop does not back off");
        drops.ShouldAllBe(r => r.TaskId == young && r.Mode == WorkspaceOutputDropModes.DryRun);
        var rows = (await w.RowsAsync(young)).Where(r => r.Lane == RunnerWorktreeRetentionService.LaneOutputDrop).ToList();
        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(r => r.Outcome == nameof(WorktreeResidueCandidateOutcome.Preview)
            && r.ReasonCode == WorkspaceOutputDropCodes.WouldDrop);
        (await w.Rig.ReadTaskAsync(young)).RemoteWorktreePath.ShouldBe(C1677RetentionServerTests.World.PathOf(young));
        await using var db = w.Rig.NewDb();
        var events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == young).ToListAsync();
        events.ShouldNotContain(e => e.Detail.Contains("runner_outputs_dropped", StringComparison.Ordinal));
    }

    private static async Task<Guid> SeedSessionAsync(Rig rig, SessionStatus status)
    {
        var id = Guid.NewGuid();
        await using var db = rig.NewDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = id, DefinitionName = "grok", AgentKind = AgentKind.Grok, Status = status, Cwd = rig.WorkspacePath,
            Cols = 80, Rows = 24, CreatedAt = rig.Now, StartedAt = rig.Now, LastSeenAt = rig.Now,
            RunnerId = rig.RunnerId, RunnerStoreId = rig.Host.StoreId, RunnerCwd = "/work",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SeedSyncDebtAsync(Rig rig, Guid taskId, AgentTaskSyncDebtState state)
    {
        await using var db = rig.NewDb();
        db.AgentTaskSyncDebts.Add(new AgentTaskSyncDebt
        {
            Id = Guid.NewGuid(), TaskId = taskId, Attempt = 1, SettlementEventId = Guid.NewGuid(), State = state,
            ReasonCode = "runner_sync_lease_busy", CreatedAt = rig.Now, UpdatedAt = rig.Now,
        });
        await db.SaveChangesAsync();
    }
}
