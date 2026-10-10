using System.Data.Common;
using System.Globalization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0505 V-6. Real dispatcher claims against an isolated shop and recorded launches.</summary>
[Category("Integration")]
[NotInParallel("card-0505-advisory-lock")]
public class DispatchConcurrencyDispatchTests
{
    private static readonly TimeSpan HarnessBudget = TimeSpan.FromSeconds(20);

    [Test]
    [Timeout(120_000)]
    public async Task Parallel_cap_holds_then_next_tick_releases()
    {
        await using var project = await OpenAsync();
        await PutProjectAsync(project, Separate(1, null));
        var occupant = await project.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Working, project.Shop.ProjectP, WorkspaceMode.ReadOnly);
        var successor = await project.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, project.Shop.ProjectP, WorkspaceMode.ReadOnly,
            title: "hold-successor");
        var token = successor.ConcurrencyToken;
        for (var tick = 0; tick < 3; tick++)
            await TickAsync(project);
        var held = await project.ReloadAsync(successor.Id);
        held.Status.ShouldBe(AgentTaskStatus.Queued, "held-zero-launch");
        held.AgentSessionId.ShouldBeNull("held-custody");
        held.ConcurrencyToken.ShouldBe(token, "held-custody");
        project.Launches.Items.Count.ShouldBe(0, "held-zero-launch");
        var heldRows = await HeldAsync(project, successor.Id);
        heldRows.Count.ShouldBe(1, "held-zero-launch");
        heldRows[0].Detail.ShouldContain("population=parallel", Case.Sensitive, "held-zero-launch");

        var other = await project.InsertAsync(
            AgentTaskRole.Plan, AgentTaskStatus.Queued, project.Shop.ProjectQ, WorkspaceMode.ReadOnly,
            title: "other-project");
        await TickAsync(project);
        (await project.ReloadAsync(other.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "held-zero-launch");
        (await project.ReloadAsync(successor.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "held-zero-launch");
        project.Launches.Items.Count.ShouldBe(1, "held-zero-launch");
        (await HeldAsync(project, successor.Id)).Count.ShouldBe(1, "held-zero-launch");

        project.Shop.Advance(TimeSpan.FromSeconds(301));
        await TickAsync(project);
        (await AgedAsync(project, successor.Id)).Count.ShouldBe(1, "held-zero-launch");
        await TickAsync(project);
        (await AgedAsync(project, successor.Id)).Count.ShouldBe(1, "held-zero-launch");
        (await project.ReloadAsync(successor.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "held-zero-launch");

        await project.SetStatusAsync(occupant.Id, AgentTaskStatus.Succeeded);
        await TickAsync(project);
        var released = await project.ReloadAsync(successor.Id);
        released.Status.ShouldBe(AgentTaskStatus.Dispatched, "held-zero-launch");
        project.Launches.Items.Count.ShouldBe(2, "held-zero-launch");
        project.Launches.KeyFreeAtLaunch.ShouldBe(true, "external-call-key-free=true");
        await DeliverColdPromptAsync(project, successor.Id, "hold-successor");

        await using var role = await OpenAsync();
        await PutProjectAsync(role, """{"schemaVersion":1,"mode":"SeparateQueues","maxParallel":4,"roles":{"Code":{"maxParallel":1}}}""");
        await role.InsertAsync(AgentTaskRole.Code, AgentTaskStatus.Working, role.Shop.ProjectP, WorkspaceMode.ReadOnly);
        var blocked = await role.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, role.Shop.ProjectP, WorkspaceMode.ReadOnly, title: "role-held");
        var free = await role.InsertAsync(
            AgentTaskRole.Plan, AgentTaskStatus.Queued, role.Shop.ProjectP, WorkspaceMode.ReadOnly, title: "role-free");
        await TickAsync(role);
        (await role.ReloadAsync(blocked.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "held-zero-launch");
        (await role.ReloadAsync(free.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "held-zero-launch");
        (await HeldAsync(role, blocked.Id)).Single().Detail.ShouldContain("role=Code", Case.Sensitive, "held-zero-launch");
    }

    [Test]
    [Timeout(120_000)]
    public async Task Project_cap_spans_local_and_remote_hosts()
    {
        await using var span = await OpenAsync();
        await PutProjectAsync(span, Separate(2, null));
        await span.InsertAsync(AgentTaskRole.Code, AgentTaskStatus.Working, span.Shop.ProjectP, WorkspaceMode.ReadOnly);
        var remoteOccupant = await span.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Working, span.Shop.ProjectP, WorkspaceMode.ReadOnly, runnerId: "runner-a");
        var waiting = await span.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, span.Shop.ProjectP, WorkspaceMode.Worktree,
            runnerId: "runner-b", remoteWorktreePath: "/mirror/p-waiting");
        var foreign = await span.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, span.Shop.ProjectQ, WorkspaceMode.Worktree,
            runnerId: "runner-b", remoteWorktreePath: "/mirror/q-free");
        await TickAsync(span);
        (await span.ReloadAsync(waiting.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "cross-host-project-cap");
        (await span.ReloadAsync(foreign.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "cross-host-project-cap");
        (await HeldAsync(span, waiting.Id)).Single().Detail.ShouldContain(
            "population=parallel", Case.Sensitive, "cross-host-project-cap");
        span.Launches.Items.Count.ShouldBe(1, "cross-host-project-cap");

        await span.SetStatusAsync(remoteOccupant.Id, AgentTaskStatus.Succeeded);
        await TickAsync(span);
        (await span.ReloadAsync(waiting.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "cross-host-project-cap");
        span.Launches.Items.Count.ShouldBe(2, "cross-host-project-cap");
        span.Launches.KeyFreeAtLaunch.ShouldBe(true, "external-call-key-free=true");

        await using var host = await OpenAsync();
        await PutProjectAsync(host, Separate(8, null));
        var local = await host.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, host.Shop.ProjectP, WorkspaceMode.ReadOnly, title: "host-held");
        await host.Budgets.UpsertAsync("local", 0, "fill the desktop", CancellationToken.None);
        await TickAsync(host);
        (await host.ReloadAsync(local.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "cross-host-project-cap");
        (await HostHeldAsync(host, local.Id)).ShouldContain(
            "Held: host 'local' budget", Case.Sensitive, "cross-host-project-cap");
        host.Launches.Items.Count.ShouldBe(0, "cross-host-project-cap");
        await host.Budgets.UpsertAsync("local", null, "clear the desktop", CancellationToken.None);
        await TickAsync(host);
        (await host.ReloadAsync(local.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "cross-host-project-cap");
        host.Launches.KeyFreeAtLaunch.ShouldBe(true, "external-call-key-free=true");

        await using var retained = await OpenAsync();
        await PutProjectAsync(retained, Separate(1, null));
        await retained.Budgets.UpsertAsync("local", 1, "one desktop seat", CancellationToken.None);
        var owner = await retained.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Working, retained.Shop.ProjectP, WorkspaceMode.ReadOnly, retained: true);
        var successor = await retained.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, retained.Shop.ProjectP, WorkspaceMode.ReadOnly, title: "retained-successor");
        await TickAsync(retained);
        var ownerRow = await retained.ReloadAsync(owner.Id);
        ownerRow.Status.ShouldBe(AgentTaskStatus.Working, "cross-host-project-cap");
        ownerRow.CapacityWaitRetained.ShouldBeTrue("cross-host-project-cap");
        (await retained.ReloadAsync(successor.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "cross-host-project-cap");
        (await HeldAsync(retained, successor.Id)).Single().Detail.ShouldContain(
            "population=parallel", Case.Sensitive, "cross-host-project-cap");
        retained.Launches.Items.Count.ShouldBe(0, "cross-host-project-cap");
    }

    [Test]
    [Timeout(180_000)]
    public async Task All_launch_paths_obey_role_cap()
    {
        await using var world = await OpenAsync();
        await PutProjectAsync(world, """{"schemaVersion":1,"mode":"SeparateQueues","maxParallel":8,"roles":{"Code":{"maxParallel":1}}}""");

        await HoldThenReleaseColdAsync(world, WorkspaceMode.ReadOnly, null, null, "path-cold");
        await HoldThenReleaseColdAsync(world, WorkspaceMode.Worktree, "runner-b", "/mirror/path-prepared", "path-prepared");

        var (warmAgent, warmSession) = await world.InsertAgentAsync(world.Shop.ProjectP, pool: true);
        await HoldThenReleaseReuseAsync(world, warmAgent, warmSession, pool: true, followUpOf: null, "path-warm");
        var (standingAgent, standingSession) = await world.InsertAgentAsync(world.Shop.ProjectP, pool: false);
        var standingRun = await HoldThenReleaseReuseAsync(
            world, standingAgent, standingSession, pool: false, followUpOf: null, "path-standing");
        await HoldThenReleaseReuseAsync(
            world, standingAgent, standingSession, pool: false, followUpOf: standingRun, "path-follow");

        await BusyRecipientAsync(world, warmAgent, warmSession);
        await EnqueueFailureAsync(world, warmAgent, warmSession);
        await CancelPreparedClaimAsync(world);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Competing_claims_admit_only_one_task()
    {
        await using var cold = await OpenAsync();
        await PutProjectAsync(cold, Separate(1, null));
        var coldA = await cold.InsertAsync(AgentTaskRole.Code, AgentTaskStatus.Queued, cold.Shop.ProjectP, WorkspaceMode.ReadOnly);
        var coldB = await cold.InsertAsync(AgentTaskRole.Plan, AgentTaskStatus.Queued, cold.Shop.ProjectP, WorkspaceMode.ReadOnly);
        await RaceAsync(cold, coldA, coldB, expectedLaunches: 1);

        await using var role = await OpenAsync();
        await PutProjectAsync(role, """{"schemaVersion":1,"mode":"SeparateQueues","maxParallel":4,"roles":{"Code":{"maxParallel":1}}}""");
        var roleA = await role.InsertAsync(AgentTaskRole.Code, AgentTaskStatus.Queued, role.Shop.ProjectP, WorkspaceMode.ReadOnly);
        var roleB = await role.InsertAsync(AgentTaskRole.Code, AgentTaskStatus.Queued, role.Shop.ProjectP, WorkspaceMode.ReadOnly);
        await RaceAsync(role, roleA, roleB, expectedLaunches: 1);

        await using var reuse = await OpenAsync();
        await PutProjectAsync(reuse, Separate(1, null));
        var (warmAgent, warmSession) = await reuse.InsertAgentAsync(reuse.Shop.ProjectP, pool: true);
        var (standingAgent, standingSession) = await reuse.InsertAgentAsync(reuse.Shop.ProjectP, pool: false);
        reuse.BindPrompt(warmSession);
        reuse.BindPrompt(standingSession);
        var warm = await reuse.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, reuse.Shop.ProjectP, WorkspaceMode.Shared,
            agentId: warmAgent, ephemeral: false, title: "race-warm");
        var standing = await reuse.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, reuse.Shop.ProjectP, WorkspaceMode.Shared,
            agentId: standingAgent, ephemeral: false, title: "race-standing");
        await RaceAsync(reuse, warm, standing, expectedLaunches: 0);
        var prompts = await PromptCountAsync(reuse, warmSession) + await PromptCountAsync(reuse, standingSession);
        prompts.ShouldBe(1, "one-dispatch-winner");

        await using var mixed = await OpenAsync();
        await PutProjectAsync(mixed, Separate(1, null));
        var local = await mixed.InsertAsync(AgentTaskRole.Code, AgentTaskStatus.Queued, mixed.Shop.ProjectP, WorkspaceMode.ReadOnly);
        var mirror = await mixed.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, mixed.Shop.ProjectP, WorkspaceMode.Worktree,
            runnerId: "runner-b", remoteWorktreePath: "/mirror/race-mixed");
        await RaceAsync(mixed, local, mirror, expectedLaunches: 1);

        await using var cancelled = await OpenAsync();
        await PutProjectAsync(cancelled, Separate(1, null));
        var first = await cancelled.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, cancelled.Shop.ProjectP, WorkspaceMode.Worktree,
            runnerId: "runner-b", remoteWorktreePath: "/mirror/race-cancel");
        var second = await cancelled.InsertAsync(
            AgentTaskRole.Plan, AgentTaskStatus.Queued, cancelled.Shop.ProjectP, WorkspaceMode.ReadOnly, title: "race-second");
        await CancelFirstThenSecondDispatchesAsync(cancelled, first, second);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Put_racing_claim_observes_one_complete_policy()
    {
        await SchedulePAsync(WorkspaceMode.ReadOnly, null, null, pool: null, legacySwitch: false, "cold");
        await SchedulePAsync(WorkspaceMode.Worktree, "runner-b", "/mirror/schedule-p", pool: null, legacySwitch: false, "prepared");
        await SchedulePAsync(WorkspaceMode.Shared, null, null, pool: true, legacySwitch: false, "warm");
        await SchedulePAsync(WorkspaceMode.Shared, null, null, pool: false, legacySwitch: false, "standing");
        await SchedulePAsync(WorkspaceMode.ReadOnly, null, null, pool: null, legacySwitch: true, "legacy-switch");
        await ConverseAsync();
    }

    [Test]
    [Timeout(120_000)]
    public async Task Lowering_never_kills_or_discards_accepted_work()
    {
        await using var world = await OpenAsync();
        await PutProjectAsync(world, Separate(3, 4));
        var running = new List<Guid>();
        for (var i = 0; i < 3; i++)
            running.Add((await world.InsertAsync(
                AgentTaskRole.Code, AgentTaskStatus.Working, world.Shop.ProjectP, WorkspaceMode.ReadOnly)).Id);
        var queued = new List<Guid>();
        for (var i = 0; i < 4; i++)
            queued.Add((await world.InsertAsync(
                AgentTaskRole.Code, AgentTaskStatus.Queued, world.Shop.ProjectP, WorkspaceMode.ReadOnly,
                title: $"queued-{i}")).Id);
        await PutProjectAsync(world, Separate(1, 0));
        await TickAsync(world);
        foreach (var id in running)
            (await world.ReloadAsync(id)).Status.ShouldBe(AgentTaskStatus.Working, "lowering-preserves-work");
        foreach (var id in queued)
            (await world.ReloadAsync(id)).Status.ShouldBe(AgentTaskStatus.Queued, "lowering-preserves-work");
        world.Launches.Items.Count.ShouldBe(0, "lowering-preserves-work");
        world.Stopper.Killed.Count.ShouldBe(0, "lowering-preserves-work");
        var view = await world.Shop.ReadProjectAsync(world.Shop.ProjectP);
        view.Occupancy.ParallelOverage.ShouldBe(2, "lowering-preserves-work");
        view.Occupancy.QueuedOverage.ShouldBe(4, "lowering-preserves-work");
        using var repo = new ScratchGitRepo("c0505-lower");
        world.Detach();
        var refused = await Should.ThrowAsync<ConcurrencyLimitException>(() => world.Tasks.CreateAsync(
            new CreateAgentTaskRequest(
                "lower-fresh", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.ReadOnly, WorkingDirectory: repo.Path),
            new AgentTaskService.Caller(null, null, repo.Path, ProjectId: world.Shop.ProjectP),
            CancellationToken.None));
        refused.Code.ShouldBe("concurrency_limit", "lowering-preserves-work");

        await world.SetStatusAsync(running[0], AgentTaskStatus.Succeeded);
        await world.SetStatusAsync(running[1], AgentTaskStatus.Succeeded);
        await TickAsync(world);
        foreach (var id in queued)
            (await world.ReloadAsync(id)).Status.ShouldBe(AgentTaskStatus.Queued, "lowering-preserves-work");
        world.Launches.Items.Count.ShouldBe(0, "lowering-preserves-work");
        await world.SetStatusAsync(running[2], AgentTaskStatus.Succeeded);
        await TickAsync(world);
        (await world.ReloadAsync(queued[0])).Status.ShouldBe(AgentTaskStatus.Dispatched, "lowering-preserves-work");
        (await world.ReloadAsync(queued[1])).Status.ShouldBe(AgentTaskStatus.Queued, "lowering-preserves-work");
        world.Launches.Items.Count.ShouldBe(1, "lowering-preserves-work");
        world.Stopper.Killed.Count.ShouldBe(0, "lowering-preserves-work");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Restart_recounts_without_leaked_slots()
    {
        await using var world = await OpenAsync();
        // The canceled claim has to reach the admitting save. Cap 1 with the
        // first task already dispatched refuses before that save exists.
        await PutProjectAsync(world, Separate(2, null));
        var admitted = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, world.Shop.ProjectP, WorkspaceMode.ReadOnly, title: "restart-admitted");
        await TickAsync(world);
        (await world.ReloadAsync(admitted.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "restart-recounts");
        var launches = world.Launches.Items.Count;
        launches.ShouldBe(1, "restart-recounts");
        var sessions = await SessionCountAsync(world);

        var second = await world.InsertAsync(
            AgentTaskRole.Plan, AgentTaskStatus.Queued, world.Shop.ProjectP, WorkspaceMode.ReadOnly, title: "restart-second");
        var pause = new PauseMatchSaveInterceptor { Match = PauseMatchSaveInterceptor.ClaimedDispatch };
        await using var db = world.Shop.Db(pause);
        using var cancel = new CancellationTokenSource();
        var claim = world.DispatcherWith(db).DispatchOneAsync(second, cancel.Token);
        await WaitAsync(pause.AtSave.Task);
        cancel.Cancel();
        await ObserveCancelAsync(claim, pause);
        var rolled = await world.ReloadAsync(second.Id);
        rolled.Status.ShouldBe(AgentTaskStatus.Queued, "restart-recounts");
        rolled.AgentSessionId.ShouldBeNull("restart-recounts");
        (await SessionCountAsync(world)).ShouldBe(sessions, "restart-recounts");
        await PutProjectAsync(world, Separate(1, null));

        await world.RebuildAsync();
        (await world.ReloadAsync(admitted.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "restart-recounts");
        (await world.ReloadAsync(second.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "restart-recounts");
        (await SessionCountAsync(world)).ShouldBe(sessions, "restart-recounts");
        world.Launches.Items.Count.ShouldBe(launches, "restart-recounts");

        var successor = await world.InsertAsync(
            AgentTaskRole.Debug, AgentTaskStatus.Queued, world.Shop.ProjectP, WorkspaceMode.ReadOnly, title: "restart-next");
        await TickAsync(world);
        (await world.ReloadAsync(successor.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "restart-recounts");
        (await world.ReloadAsync(second.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "restart-recounts");
        world.Launches.Items.Count.ShouldBe(launches, "restart-recounts");

        await world.SetStatusAsync(admitted.Id, AgentTaskStatus.Succeeded);
        await TickAsync(world);
        (await world.ReloadAsync(second.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "restart-recounts");
        (await world.ReloadAsync(successor.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "restart-recounts");
        world.Launches.Items.Count.ShouldBe(launches + 1, "restart-recounts");
    }

    [Test]
    [Timeout(120_000)]
    public async Task Recovery_and_retained_wait_preserve_owned_slots()
    {
        await using var world = await OpenAsync();
        await PutProjectAsync(world, Separate(1, 0));
        var accepted = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Failed, world.Shop.ProjectP, WorkspaceMode.ReadOnly, title: "recovery-accepted");
        var occupant = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Working, world.Shop.ProjectP, WorkspaceMode.ReadOnly);
        world.Detach();
        await world.Tasks.RetryAsync(accepted.Id, CancellationToken.None);
        (await world.ReloadAsync(accepted.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "recovery-owned-slot");
        using var repo = new ScratchGitRepo("c0505-recover");
        world.Detach();
        var refused = await Should.ThrowAsync<ConcurrencyLimitException>(() => world.Tasks.CreateAsync(
            new CreateAgentTaskRequest(
                "recovery-fresh", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.ReadOnly, WorkingDirectory: repo.Path),
            new AgentTaskService.Caller(null, null, repo.Path, ProjectId: world.Shop.ProjectP),
            CancellationToken.None));
        refused.Code.ShouldBe("concurrency_limit", "recovery-owned-slot");
        (await CountGoalAsync(world, "recovery-fresh")).ShouldBe(0, "recovery-owned-slot");

        await TickAsync(world);
        (await world.ReloadAsync(accepted.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "recovery-owned-slot");
        (await HeldAsync(world, accepted.Id)).Single().Detail.ShouldContain(
            "population=parallel", Case.Sensitive, "recovery-owned-slot");
        world.Launches.Items.Count.ShouldBe(0, "recovery-owned-slot");
        await world.SetStatusAsync(occupant.Id, AgentTaskStatus.Succeeded);
        await TickAsync(world);
        var redispatched = await world.ReloadAsync(accepted.Id);
        redispatched.Status.ShouldBe(AgentTaskStatus.Dispatched, "recovery-owned-slot");
        var session = redispatched.AgentSessionId;
        session.ShouldNotBeNull("recovery-owned-slot");
        world.Launches.Items.Count.ShouldBe(1, "recovery-owned-slot");

        await using var retained = await OpenAsync();
        await PutProjectAsync(retained, Separate(1, 4));
        await retained.Budgets.UpsertAsync("local", 1, "one desktop seat", CancellationToken.None);
        var owner = await retained.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Working, retained.Shop.ProjectP, WorkspaceMode.ReadOnly, retained: true);
        var successor = await retained.InsertAsync(
            AgentTaskRole.Plan, AgentTaskStatus.Queued, retained.Shop.ProjectP, WorkspaceMode.ReadOnly, title: "retained-next");
        await TickAsync(retained);
        var owned = await retained.ReloadAsync(owner.Id);
        owned.Status.ShouldBe(AgentTaskStatus.Working, "recovery-owned-slot");
        owned.CapacityWaitRetained.ShouldBeTrue("recovery-owned-slot");
        (await retained.ReloadAsync(successor.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "recovery-owned-slot");
        retained.Launches.Items.Count.ShouldBe(0, "recovery-owned-slot");
        await retained.RebuildAsync();
        (await retained.ReloadAsync(owner.Id)).CapacityWaitRetained.ShouldBeTrue("recovery-owned-slot");
        (await retained.ReloadAsync(successor.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "recovery-owned-slot");
        await TickAsync(retained);
        (await retained.ReloadAsync(successor.Id)).Status.ShouldBe(AgentTaskStatus.Queued, "recovery-owned-slot");
        (await retained.ReloadAsync(owner.Id)).Status.ShouldBe(AgentTaskStatus.Working, "recovery-owned-slot");
        retained.Launches.Items.Count.ShouldBe(0, "recovery-owned-slot");
        retained.Stopper.Killed.Count.ShouldBe(0, "recovery-owned-slot");
    }

    private static async Task HoldThenReleaseColdAsync(
        ConcurrencyDispatchWorld world, WorkspaceMode workspace, string? runnerId, string? remotePath, string marker)
    {
        var occupant = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Working, world.Shop.ProjectP, WorkspaceMode.ReadOnly);
        var sessions = await SessionCountAsync(world);
        var candidate = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, world.Shop.ProjectP, workspace,
            runnerId: runnerId, remoteWorktreePath: remotePath, title: marker);
        var token = candidate.ConcurrencyToken;
        var launches = world.Launches.Items.Count;
        await TickAsync(world);
        var held = await world.ReloadAsync(candidate.Id);
        held.Status.ShouldBe(AgentTaskStatus.Queued, "every-path-gated");
        held.AgentSessionId.ShouldBeNull("held-custody");
        held.AgentId.ShouldBeNull("held-custody");
        held.ConcurrencyToken.ShouldBe(token, "held-custody");
        world.Launches.Items.Count.ShouldBe(launches, "every-path-gated");
        (await SessionCountAsync(world)).ShouldBe(sessions, "held-custody");
        (await HeldAsync(world, candidate.Id)).Count.ShouldBe(1, "every-path-gated");

        await world.SetStatusAsync(occupant.Id, AgentTaskStatus.Succeeded);
        await TickAsync(world);
        var released = await world.ReloadAsync(candidate.Id);
        released.Status.ShouldBe(AgentTaskStatus.Dispatched, "every-path-gated");
        world.Launches.Items.Count.ShouldBe(launches + 1, "every-path-gated");
        world.Launches.KeyFreeAtLaunch.ShouldBe(true, "external-call-key-free=true");
        await DeliverColdPromptAsync(world, candidate.Id, marker);
        await world.SetStatusAsync(candidate.Id, AgentTaskStatus.Succeeded);
    }

    private static async Task<Guid> HoldThenReleaseReuseAsync(
        ConcurrencyDispatchWorld world, Guid agentId, Guid sessionId, bool pool, Guid? followUpOf, string marker)
    {
        var occupant = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Working, world.Shop.ProjectP, WorkspaceMode.ReadOnly);
        var before = await AgentSnapshotAsync(world, agentId);
        var candidate = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, world.Shop.ProjectP, WorkspaceMode.Shared,
            agentId: agentId, ephemeral: false, followUpOf: followUpOf, title: marker);
        var launches = world.Launches.Items.Count;
        // The live follow-up shares the standing session's earlier UserPrompt.
        // This watermark is that count: the hold leaves it unchanged, and a fresh session stays at zero.
        var prompts = await PromptCountAsync(world, sessionId);
        await TickAsync(world);
        var held = await world.ReloadAsync(candidate.Id);
        held.Status.ShouldBe(AgentTaskStatus.Queued, "every-path-gated");
        held.AgentSessionId.ShouldBeNull("held-custody");
        held.TokenHash.ShouldBeNull("held-custody");
        world.Launches.Items.Count.ShouldBe(launches, "every-path-gated");
        (await PromptCountAsync(world, sessionId)).ShouldBe(prompts, "every-path-gated");
        await AssertAgentUnchangedAsync(world, agentId, before, pool);

        await world.SetStatusAsync(occupant.Id, AgentTaskStatus.Succeeded);
        world.BindPrompt(sessionId);
        await TickAsync(world);
        var released = await world.ReloadAsync(candidate.Id);
        released.Status.ShouldBe(AgentTaskStatus.Dispatched, "every-path-gated");
        released.AgentSessionId.ShouldBe(sessionId, "every-path-gated");
        world.Launches.Items.Count.ShouldBe(launches, "every-path-gated");
        var prompt = await world.LatestPromptAsync(sessionId);
        prompt.ShouldNotBeNull("complete-recipient-prompt");
        prompt.Text.ShouldContain(marker, Case.Sensitive, "complete-recipient-prompt");
        prompt.Text.ShouldContain(DelegationReportFormatter.Short(candidate.Id), Case.Sensitive, "complete-recipient-prompt");
        await CloseTurnAsync(world, sessionId);
        await world.SetStatusAsync(candidate.Id, AgentTaskStatus.Succeeded);
        if (pool)
            await RestoreIdleAsync(world, agentId);
        return candidate.Id;
    }

    private static async Task BusyRecipientAsync(ConcurrencyDispatchWorld world, Guid agentId, Guid sessionId)
    {
        await RestoreIdleAsync(world, agentId);
        await CloseTurnAsync(world, sessionId);
        var candidate = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, world.Shop.ProjectP, WorkspaceMode.Shared,
            agentId: agentId, ephemeral: false, title: "path-busy");
        var before = await PromptCountAsync(world, sessionId);
        var busy = new BusyOnCommitInterceptor(world.Shop);
        await using var db = world.Shop.Db(busy);
        var outcome = await world.DispatcherWith(db).DispatchOneAsync(candidate, CancellationToken.None);
        outcome.ShouldBe(AgentTaskDispatcher.DispatchOneResult.Dispatched, "complete-recipient-prompt");
        (await PromptCountAsync(world, sessionId)).ShouldBe(before, "complete-recipient-prompt");
        world.BindPrompt(sessionId);
        await world.Queue.FlushIfIdleAsync(sessionId, CancellationToken.None);
        (await PromptCountAsync(world, sessionId)).ShouldBe(before, "complete-recipient-prompt");
        await CloseTurnAsync(world, sessionId);
        await world.Queue.OnTurnEndAsync(sessionId, CancellationToken.None);
        var prompt = await world.LatestPromptAsync(sessionId);
        prompt.ShouldNotBeNull("complete-recipient-prompt");
        prompt.Text.ShouldContain("path-busy", Case.Sensitive, "complete-recipient-prompt");
        (await PromptCountAsync(world, sessionId)).ShouldBe(before + 1, "complete-recipient-prompt");
        await world.SetStatusAsync(candidate.Id, AgentTaskStatus.Succeeded);
        await RestoreIdleAsync(world, agentId);
    }

    private static async Task EnqueueFailureAsync(ConcurrencyDispatchWorld world, Guid agentId, Guid sessionId)
    {
        await CloseTurnAsync(world, sessionId);
        var before = await PromptCountAsync(world, sessionId);
        var candidate = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, world.Shop.ProjectP, WorkspaceMode.Shared,
            agentId: agentId, ephemeral: false, title: "path-fault");
        world.Dispatcher.ReuseEnqueueOverride = (_, _, _) => throw new InvalidOperationException("enqueue refused");
        try
        {
            await TickAsync(world);
        }
        finally
        {
            world.Dispatcher.ReuseEnqueueOverride = null;
        }

        var row = await world.ReloadAsync(candidate.Id);
        row.Status.ShouldBe(AgentTaskStatus.Dispatched, "complete-recipient-prompt");
        (await PromptCountAsync(world, sessionId)).ShouldBe(before, "complete-recipient-prompt");
        await using (var db = world.Shop.Db())
        {
            var incidents = await db.AgentIncidents.CountAsync(i =>
                i.Kind == AgentIncidentKind.DeliveryTransportFailed && i.SessionId == sessionId);
            incidents.ShouldBeGreaterThan(0, "complete-recipient-prompt");
            var delivered = await db.SessionQueuedMessages.CountAsync(m =>
                m.ExecutionTaskId == candidate.Id && m.DeliveryVerdict == DeliveryVerdict.Delivered);
            delivered.ShouldBe(0, "complete-recipient-prompt");
        }

        await CloseTurnAsync(world, sessionId);
        world.BindPrompt(sessionId);
        await using (var db = world.Shop.Db())
        {
            var started = await db.AgentSessions.Where(s => s.Id == sessionId).Select(s => s.StartedAt).SingleAsync();
            await world.Dispatcher.EnsureLaunchBriefAsync(row, sessionId, started, CancellationToken.None);
        }

        await world.Queue.FlushIfIdleAsync(sessionId, CancellationToken.None);
        var prompt = await world.LatestPromptAsync(sessionId);
        prompt.ShouldNotBeNull("complete-recipient-prompt");
        prompt.Text.ShouldContain("path-fault", Case.Sensitive, "complete-recipient-prompt");
        await world.SetStatusAsync(candidate.Id, AgentTaskStatus.Succeeded);
        await RestoreIdleAsync(world, agentId);
    }

    private static async Task CancelPreparedClaimAsync(ConcurrencyDispatchWorld world)
    {
        var candidate = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, world.Shop.ProjectP, WorkspaceMode.Worktree,
            runnerId: "runner-b", remoteWorktreePath: "/mirror/path-cancel", title: "path-cancel");
        var path = candidate.RemoteWorktreePath;
        var pause = new PauseMatchSaveInterceptor { Match = PauseMatchSaveInterceptor.ClaimedDispatch };
        await using var db = world.Shop.Db(pause);
        using var cancel = new CancellationTokenSource();
        var launches = world.Launches.Items.Count;
        var claim = world.DispatcherWith(db).DispatchOneAsync(candidate, cancel.Token);
        await WaitAsync(pause.AtSave.Task);
        world.Launches.ParallelKeyIsFree().ShouldBeFalse("no-external-io-under-key");
        cancel.Cancel();
        await ObserveCancelAsync(claim, pause);
        var row = await world.ReloadAsync(candidate.Id);
        row.Status.ShouldBe(AgentTaskStatus.Queued, "held-custody");
        row.RemoteWorktreePath.ShouldBe(path, "held-custody");
        row.AgentSessionId.ShouldBeNull("held-custody");
        world.Launches.Items.Count.ShouldBe(launches, "every-path-gated");
    }

    private static async Task SchedulePAsync(
        WorkspaceMode workspace, string? runnerId, string? remotePath, bool? pool, bool legacySwitch, string label)
    {
        await using var world = await OpenAsync();
        Guid? agentId = null;
        if (pool is bool pooled)
            agentId = (await world.InsertAgentAsync(world.Shop.ProjectP, pooled)).AgentId;
        if (!legacySwitch)
            await PutProjectAsync(world, Separate(2, null));
        await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Working, world.Shop.ProjectP, WorkspaceMode.ReadOnly);
        var candidate = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, world.Shop.ProjectP, workspace,
            runnerId: runnerId, agentId: agentId, ephemeral: agentId is null, remoteWorktreePath: remotePath,
            title: "schedule-p-" + label);
        var commands = new DispatchCommandInterceptor { PauseClaim = true };
        var order = new DispatchCommandInterceptor();
        await using var db = world.Shop.Db(commands);
        var launches = world.Launches.Items.Count;
        var claim = world.DispatcherWith(db).DispatchOneAsync(candidate, CancellationToken.None);
        try
        {
            await WaitAsync(commands.AtCommand.Task);
            commands.Crossed.ShouldBeFalse("claim-B-crossed=false");
            var project = await world.Shop.ReadProjectAsync(world.Shop.ProjectP);
            var global = await world.Shop.ReadGlobalAsync();
            var body = Separate(1, null);
            await using var putDb = world.Shop.Db(order);
            var updated = await world.Shop.Service(putDb).PutProjectAsync(
                world.Shop.ProjectP,
                DispatchConcurrencyTestHost.Put(project.Revision, body, "lower", expectedGlobalRevision: global.Revision),
                null,
                CancellationToken.None);
            order.LockOrderBroken.ShouldBeFalse("lock-order");
            commands.Release.TrySetResult();
            var outcome = await claim;
            commands.LockOrderBroken.ShouldBeFalse("lock-order");
            outcome.ShouldBe(AgentTaskDispatcher.DispatchOneResult.HeldOnParallel, "latest-claim-policy");
            var row = await world.ReloadAsync(candidate.Id);
            row.Status.ShouldBe(AgentTaskStatus.Queued, "latest-claim-policy");
            world.Launches.Items.Count.ShouldBe(launches, "latest-claim-policy");
            var detail = (await HeldAsync(world, candidate.Id)).Single().Detail;
            detail.ShouldContain($"projectRevision={updated.Revision}", Case.Sensitive, "latest-claim-policy");
            detail.ShouldContain($"globalRevision={updated.GlobalRevision}", Case.Sensitive, "latest-claim-policy");
        }
        finally
        {
            commands.Release.TrySetResult();
        }
    }

    private static async Task ConverseAsync()
    {
        await using var world = await OpenAsync();
        await PutProjectAsync(world, Separate(2, null));
        await world.InsertAsync(AgentTaskRole.Code, AgentTaskStatus.Working, world.Shop.ProjectP, WorkspaceMode.ReadOnly);
        var candidate = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, world.Shop.ProjectP, WorkspaceMode.ReadOnly, title: "converse");
        var pause = new PauseMatchSaveInterceptor { Match = PauseMatchSaveInterceptor.ClaimedDispatch };
        await using var db = world.Shop.Db(pause);
        var claim = world.DispatcherWith(db).DispatchOneAsync(candidate, CancellationToken.None);
        try
        {
            await WaitAsync(pause.AtSave.Task);
            world.Launches.ParallelKeyIsFree().ShouldBeFalse("no-external-io-under-key");
            world.Launches.ClaimBackendPid = await ClaimBackendPidAsync(db);
            var project = await world.Shop.ReadProjectAsync(world.Shop.ProjectP);
            var global = await world.Shop.ReadGlobalAsync();
            var put = Task.Run(async () =>
            {
                await using var putDb = world.Shop.Db();
                return await world.Shop.Service(putDb).PutProjectAsync(
                    world.Shop.ProjectP,
                    DispatchConcurrencyTestHost.Put(project.Revision, Separate(1, null), "overage", expectedGlobalRevision: global.Revision),
                    null,
                    CancellationToken.None);
            });
            var waiting = await DispatchConcurrencyLockProbe.WaitForUngrantedAdvisoryAsync(
                world.Shop.ConnectionString, HarnessBudget);
            waiting.ShouldBeTrue("harness deadline");
            pause.Release.TrySetResult();
            var outcome = await claim;
            outcome.ShouldBe(AgentTaskDispatcher.DispatchOneResult.Dispatched, "latest-claim-policy");
            var updated = await put.WaitAsync(HarnessBudget);
            updated.Occupancy.ParallelOverage.ShouldBeGreaterThan(0, "latest-claim-policy");
            var row = await world.ReloadAsync(candidate.Id);
            row.Status.ShouldBe(AgentTaskStatus.Dispatched, "lowering-preserves-work");
            world.Stopper.Killed.Count.ShouldBe(0, "lowering-preserves-work");
            world.Launches.KeyFreeAtLaunch.ShouldBe(true, "external-call-key-free=true");
        }
        finally
        {
            pause.Release.TrySetResult();
        }
    }

    private static async Task RaceAsync(ConcurrencyDispatchWorld world, AgentTask left, AgentTask right, int expectedLaunches)
    {
        var pause = new PauseMatchSaveInterceptor { Match = PauseMatchSaveInterceptor.ClaimedDispatch };
        await using var leftDb = world.Shop.Db(pause);
        await using var rightDb = world.Shop.Db(pause);
        var launches = world.Launches.Items.Count;
        var runLeft = world.DispatcherWith(leftDb).DispatchOneAsync(left, CancellationToken.None);
        var runRight = world.DispatcherWith(rightDb).DispatchOneAsync(right, CancellationToken.None);
        try
        {
            await WaitUntilPausedAsync(pause);
            var waiting = await DispatchConcurrencyLockProbe.WaitForUngrantedAdvisoryAsync(
                world.Shop.ConnectionString, HarnessBudget);
            if (pause.Crossed)
                pause.Crossed.ShouldBeFalse("claim-B-crossed=false");
            if (!waiting)
                throw new InvalidOperationException("harness deadline");
            pause.Release.TrySetResult();
            var leftResult = await runLeft.WaitAsync(HarnessBudget);
            var rightResult = await runRight.WaitAsync(HarnessBudget);
            var admitted = (leftResult == AgentTaskDispatcher.DispatchOneResult.Dispatched ? 1 : 0)
                + (rightResult == AgentTaskDispatcher.DispatchOneResult.Dispatched ? 1 : 0);
            admitted.ShouldBe(1, "one-dispatch-winner");
            var rows = new[] { await world.ReloadAsync(left.Id), await world.ReloadAsync(right.Id) };
            rows.Count(row => row.Status == AgentTaskStatus.Dispatched).ShouldBe(1, "one-dispatch-winner");
            rows.Count(row => row.Status == AgentTaskStatus.Queued).ShouldBe(1, "one-dispatch-winner");
            (world.Launches.Items.Count - launches).ShouldBe(expectedLaunches, "one-dispatch-winner");
            pause.Crossed.ShouldBeFalse("claim-B-crossed=false");
        }
        finally
        {
            pause.Release.TrySetResult();
        }
    }

    private static async Task CancelFirstThenSecondDispatchesAsync(
        ConcurrencyDispatchWorld world, AgentTask first, AgentTask second)
    {
        var pause = new PauseMatchSaveInterceptor { Match = PauseMatchSaveInterceptor.ClaimedDispatch };
        await using var firstDb = world.Shop.Db(pause);
        await using var secondDb = world.Shop.Db(pause);
        using var cancel = new CancellationTokenSource();
        var path = first.RemoteWorktreePath;
        var runFirst = world.DispatcherWith(firstDb).DispatchOneAsync(first, cancel.Token);
        var runSecond = world.DispatcherWith(secondDb).DispatchOneAsync(second, CancellationToken.None);
        try
        {
            await WaitUntilPausedAsync(pause);
            cancel.Cancel();
            await ObserveCancelAsync(runFirst, pause);
            var secondResult = await runSecond.WaitAsync(HarnessBudget);
            secondResult.ShouldBe(AgentTaskDispatcher.DispatchOneResult.Dispatched, "one-dispatch-winner");
            var firstRow = await world.ReloadAsync(first.Id);
            firstRow.Status.ShouldBe(AgentTaskStatus.Queued, "one-dispatch-winner");
            firstRow.RemoteWorktreePath.ShouldBe(path, "one-dispatch-winner");
            (await world.ReloadAsync(second.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched, "one-dispatch-winner");
            world.Launches.Items.Count.ShouldBe(1, "one-dispatch-winner");
        }
        finally
        {
            cancel.Cancel();
            pause.Release.TrySetResult();
        }
    }

    private static async Task DeliverColdPromptAsync(ConcurrencyDispatchWorld world, Guid taskId, string marker)
    {
        var row = await world.ReloadAsync(taskId);
        var sessionId = row.AgentSessionId.ShouldNotBeNull("complete-recipient-prompt");
        await world.MarkRunningAsync(sessionId);
        world.BindPrompt(sessionId);
        await world.Queue.FlushIfIdleAsync(sessionId, CancellationToken.None);
        var prompt = await world.LatestPromptAsync(sessionId);
        prompt.ShouldNotBeNull("complete-recipient-prompt");
        prompt.Text.ShouldContain(marker, Case.Sensitive, "complete-recipient-prompt");
        prompt.Text.ShouldContain(DelegationReportFormatter.Short(taskId), Case.Sensitive, "complete-recipient-prompt");
    }

    private static async Task CloseTurnAsync(ConcurrencyDispatchWorld world, Guid sessionId)
    {
        await using var db = world.Shop.Db();
        var next = await NextSequenceAsync(db, sessionId);
        var now = world.Shop.Clock.GetUtcNow().UtcDateTime;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = next,
            Kind = TranscriptKinds.TurnEnd,
            StopReason = TranscriptKinds.StopReasons.EndTurn,
            Timestamp = now,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<long> NextSequenceAsync(AppDbContext db, Guid sessionId)
    {
        var max = await db.TranscriptEntries.Where(e => e.AgentSessionId == sessionId)
            .Select(e => (long?)e.Sequence)
            .MaxAsync();
        return (max ?? 0) + 1;
    }

    private static async Task<AgentSnapshot> AgentSnapshotAsync(ConcurrencyDispatchWorld world, Guid agentId)
    {
        await using var db = world.Shop.Db();
        var agent = await db.Agents.AsNoTracking().SingleAsync(a => a.Id == agentId);
        return new AgentSnapshot(agent.Status, agent.PoolIdleSince, agent.PersistentSessionId);
    }

    private static async Task AssertAgentUnchangedAsync(
        ConcurrencyDispatchWorld world, Guid agentId, AgentSnapshot before, bool pool)
    {
        var after = await AgentSnapshotAsync(world, agentId);
        after.Status.ShouldBe(before.Status, "held-custody");
        after.PersistentSessionId.ShouldBe(before.PersistentSessionId, "held-custody");
        if (pool)
            after.PoolIdleSince.ShouldNotBeNull("held-custody");
    }

    private static async Task RestoreIdleAsync(ConcurrencyDispatchWorld world, Guid agentId)
    {
        await using var db = world.Shop.Db();
        var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
        agent.Status = AgentStatus.Idle;
        agent.PoolIdleSince = world.Shop.Clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
        await db.SaveChangesAsync();
    }

    private static async Task<List<AgentTaskEvent>> HeldAsync(ConcurrencyDispatchWorld world, Guid taskId)
    {
        await using var db = world.Shop.Db();
        return await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Held)
            .OrderBy(e => e.At)
            .ToListAsync();
    }

    private static async Task<List<AgentTaskEvent>> AgedAsync(ConcurrencyDispatchWorld world, Guid taskId)
    {
        await using var db = world.Shop.Db();
        return await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.HeldAged)
            .ToListAsync();
    }

    private static async Task<string> HostHeldAsync(ConcurrencyDispatchWorld world, Guid taskId)
    {
        await using var db = world.Shop.Db();
        return await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Held)
            .OrderByDescending(e => e.At)
            .Select(e => e.Detail)
            .FirstAsync();
    }

    private static async Task<int> ClaimBackendPidAsync(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        if (db.Database.CurrentTransaction is { } current)
            command.Transaction = current.GetDbTransaction();
        var scalar = await command.ExecuteScalarAsync();
        return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
    }

    private static async Task<int> PromptCountAsync(ConcurrencyDispatchWorld world, Guid sessionId)
    {
        await using var db = world.Shop.Db();
        return await db.TranscriptEntries.CountAsync(e =>
            e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt);
    }

    private static async Task<int> SessionCountAsync(ConcurrencyDispatchWorld world)
    {
        await using var db = world.Shop.Db();
        return await db.AgentSessions.CountAsync();
    }

    private static async Task<int> CountGoalAsync(ConcurrencyDispatchWorld world, string goal)
    {
        await using var db = world.Shop.Db();
        return await db.AgentTasks.CountAsync(t => t.Goal == goal);
    }

    private static async Task TickAsync(ConcurrencyDispatchWorld world)
    {
        var result = await world.TickAsync();
        if (result.Failures == 0)
            return;
        await using var db = world.Shop.Db();
        var reasons = await db.AgentTasks.AsNoTracking()
            .Where(t => t.Status == AgentTaskStatus.Failed)
            .Select(t => t.FailureReason)
            .ToListAsync();
        throw new InvalidOperationException(
            $"tick failures={result.Failures} sweep={result.SweepFailures}: {string.Join(" | ", reasons)}");
    }

    private static async Task WaitAsync(Task signal)
    {
        var finished = await Task.WhenAny(signal, Task.Delay(HarnessBudget));
        if (finished != signal)
            throw new InvalidOperationException("harness deadline");
        await signal;
    }

    private static async Task WaitUntilPausedAsync(PauseMatchSaveInterceptor pause)
    {
        var deadline = DateTime.UtcNow + HarnessBudget;
        while (DateTime.UtcNow < deadline)
        {
            if (pause.Crossed)
                pause.Crossed.ShouldBeFalse("claim-B-crossed=false");
            if (pause.AtSave.Task.IsCompletedSuccessfully)
                return;
            await Task.Delay(25);
        }

        throw new InvalidOperationException("harness deadline");
    }

    private static async Task ObserveCancelAsync(Task claim, PauseMatchSaveInterceptor pause)
    {
        var finished = await Task.WhenAny(claim, Task.Delay(HarnessBudget));
        pause.Release.TrySetResult();
        if (finished != claim)
            throw new InvalidOperationException("harness deadline");
        await Should.ThrowAsync<OperationCanceledException>(() => claim);
    }

    private static async Task<ConcurrencyDispatchWorld> OpenAsync()
    {
        var world = await ConcurrencyDispatchWorld.Open();
        await world.InitializeAsync();
        return world;
    }

    private static async Task PutProjectAsync(ConcurrencyDispatchWorld world, string overrides)
    {
        var project = await world.Shop.ReadProjectAsync(world.Shop.ProjectP);
        var global = await world.Shop.ReadGlobalAsync();
        await world.Shop.PutProjectAsync(
            world.Shop.ProjectP, project.Revision, global.Revision, overrides, "cap");
    }

    private static string Separate(int parallel, int? queued) =>
        queued is int limit
            ? $$"""{"schemaVersion":1,"mode":"SeparateQueues","maxParallel":{{parallel}},"maxQueued":{{limit}}}"""
            : $$"""{"schemaVersion":1,"mode":"SeparateQueues","maxParallel":{{parallel}}}""";

    private sealed record AgentSnapshot(AgentStatus Status, DateTime? PoolIdleSince, string? PersistentSessionId);

    private sealed class BusyOnCommitInterceptor(DispatchConcurrencyShop shop) : DbTransactionInterceptor
    {
        public override async Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            var sessionId = eventData.Context?.ChangeTracker.Entries<AgentTask>()
                .Select(entry => entry.Entity.AgentSessionId)
                .FirstOrDefault(id => id is not null);
            if (sessionId is Guid id)
            {
                await using var db = shop.Db();
                var next = await NextSequenceAsync(db, id);
                var now = shop.Clock.GetUtcNow().UtcDateTime;
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = id,
                    Sequence = next,
                    Kind = TranscriptKinds.AssistantText,
                    Text = "still working",
                    Timestamp = now,
                    CreatedAt = now,
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            await base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
        }
    }
}
