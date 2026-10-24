using System.Data.Common;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0633 D-4..D-8: a runner-bound task's branch push and runner mirror run as an owned
/// background operation, never inside the claim transaction or on the serial tick. The runner is
/// a real phone-home connection on a fake clock, so a silent mirror is advanced, never slept.
/// </summary>
[Category("Integration")]
public sealed class RemoteWorkspacePreparerTests
{
    private static readonly TimeSpan TickBound = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MirrorBudget =
        PhoneHomeLiveConnection.RequestTimeoutFor(PhoneHomeOperation.WorkspaceMirror);

    [Test]
    public async Task A_mirror_request_carries_the_desktop_origin_and_a_legacy_runner_failure_names_the_missing_feature()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        await rig.TickAsync().WaitAsync(TickBound);
        var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        var mirror = request.Payload?.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json);
        mirror.ShouldNotBeNull();
        mirror.Repository.ShouldBe("https://github.com/michal-ciechan/antiphon.git");
        await rig.Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Error, request.Epoch,
            request.RequestId, request.Operation, ErrorCode: PhoneHomeProblemTypes.UnsupportedTarget,
            ErrorDetail: "legacy primary checkout", StatusCode: 409));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        var task = await rig.ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.RemotePrepFailures.ShouldBe(1);
        task.DispatchNotBeforeAt.ShouldNotBeNull();
        var warning = (await rig.EventsAsync(taskId, AgentTaskEventType.Warning)).Single().Detail;
        warning.ShouldContain("workspaceRepositoryV1");
        warning.ShouldContain("mirrors only its primary repository");
    }

    [Test]
    public async Task Mirror_runs_outside_the_claim_and_the_task_stays_queued_with_an_in_flight_hold()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();

        await rig.TickAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.RemoteWorktreePath.ShouldBeNull();
        var held = await rig.EventsAsync(taskId, AgentTaskEventType.Held);
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldStartWith(DispatchHoldDetails.RemoteMirrorRequestedPrefix);
        await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);

        await rig.TickAsync().WaitAsync(TickBound);

        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(1);
        (await rig.EventsAsync(taskId, AgentTaskEventType.Held)).Count.ShouldBe(1);
        (await rig.ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task Mirror_success_is_recorded_and_the_next_tick_launches_into_it()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        var mirror = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);

        await rig.TickAsync().WaitAsync(TickBound);
        (await rig.ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Queued);

        var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        await rig.Peer.EmitAsync(MirrorResult(request, mirror));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);

        var prepared = await rig.ReadTaskAsync(taskId);
        prepared.RemoteWorktreePath.ShouldBe(mirror);
        prepared.RemotePrepFailures.ShouldBe(0);
        prepared.DispatchNotBeforeAt.ShouldBeNull();

        await rig.TickAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        await using var db = rig.NewDb();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId);
        session.RunnerCwd.ShouldBe(mirror);
        session.Cwd.ShouldBe(rig.WorkspacePath);
        rig.Sink.Specs.Single().Cwd.ShouldBe(mirror);
        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(1);
    }

    [Test]
    public async Task Mirror_failure_backs_off_exponentially_and_resets_on_success()
    {
        await using var rig = await Rig.StartAsync(s =>
        {
            s.RemotePrepBackoffBaseSeconds = 30;
            s.RemotePrepBackoffMaxSeconds = 120;
        });
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        var mirror = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);

        // Attempt 1 times out; the retry waits 30 s.
        await rig.TickAsync().WaitAsync(TickBound);
        await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        await rig.AdvanceAsync(MirrorBudget);
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        var failed = await rig.ReadTaskAsync(taskId);
        failed.RemotePrepFailures.ShouldBe(1);
        failed.DispatchNotBeforeAt.ShouldBe((DateTime?)(rig.Now + TimeSpan.FromSeconds(30)));
        var warnings = await rig.EventsAsync(taskId, AgentTaskEventType.Warning);
        warnings.Count.ShouldBe(1);
        warnings[0].Detail.ShouldContain(PhoneHomeProblemTypes.RequestTimeout);

        await rig.TickAsync().WaitAsync(TickBound);
        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(1, "the not-before gate must hold the task");
        var backoffHold = (await rig.EventsAsync(taskId, AgentTaskEventType.Held)).Last();
        backoffHold.Detail.ShouldBe(DispatchHoldDetails.RemotePrepBackoff(
            rig.RunnerId, 1, failed.DispatchNotBeforeAt!.Value));

        // Attempts 2..4: 60 s, 120 s, then the 120 s cap.
        var expected = new[] { 60, 120, 120 };
        var previousDelay = 30;
        for (var attempt = 2; attempt <= 4; attempt++)
        {
            await rig.AdvanceAsync(TimeSpan.FromSeconds(previousDelay));
            await rig.TickAsync().WaitAsync(TickBound);
            await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, attempt);
            await rig.AdvanceAsync(MirrorBudget);
            await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
            var row = await rig.ReadTaskAsync(taskId);
            row.RemotePrepFailures.ShouldBe(attempt);
            row.DispatchNotBeforeAt.ShouldBe((DateTime?)(rig.Now + TimeSpan.FromSeconds(expected[attempt - 2])));
            previousDelay = expected[attempt - 2];
        }

        // Attempt 5 succeeds and resets both columns.
        await rig.AdvanceAsync(TimeSpan.FromSeconds(previousDelay));
        await rig.TickAsync().WaitAsync(TickBound);
        var last = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 5);
        await rig.Peer.EmitAsync(MirrorResult(last, mirror));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        var reset = await rig.ReadTaskAsync(taskId);
        reset.RemoteWorktreePath.ShouldBe(mirror);
        reset.RemotePrepFailures.ShouldBe(0);
        reset.DispatchNotBeforeAt.ShouldBeNull();
        (await rig.EventsAsync(taskId, AgentTaskEventType.Warning)).Count.ShouldBe(4);
    }

    [Test]
    public async Task Full_runner_holds_before_remote_prep_and_the_trace_is_deduped()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        await rig.SeedOccupantAsync(SessionStatus.Running);
        var taskId = await rig.SeedAsync();

        await rig.TickAsync().WaitAsync(TickBound);
        await rig.TickAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.RemoteWorktreePath.ShouldBeNull();
        var held = await rig.EventsAsync(taskId, AgentTaskEventType.Held);
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldContain($"runner '{rig.RunnerId}' at capacity 1/1");
        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0);
        (await rig.EventsAsync(taskId, AgentTaskEventType.Dispatched)).ShouldBeEmpty();
    }

    [Test]
    public async Task Failed_and_stopped_runner_sessions_do_not_reserve_a_slot()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        await rig.SeedOccupantAsync(SessionStatus.Failed);
        await rig.SeedOccupantAsync(SessionStatus.Stopped);
        var taskId = await rig.SeedAsync();

        await rig.TickAsync().WaitAsync(TickBound);

        (await rig.ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Queued);
        var held = await rig.EventsAsync(taskId, AgentTaskEventType.Held);
        held.ShouldNotContain(e => e.Detail.Contains("at capacity", StringComparison.Ordinal));
        await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
    }

    [Test]
    public async Task Runner_not_eligible_is_one_held_trace_not_a_warning_per_tick()
    {
        await using var rig = await Rig.StartAsync(recovered: false);
        var taskId = await rig.SeedAsync();

        for (var i = 0; i < 3; i++)
            await rig.TickAsync().WaitAsync(TickBound);

        var held = await rig.EventsAsync(taskId, AgentTaskEventType.Held);
        held.Count.ShouldBe(1);
        held[0].Detail.ShouldContain("RunnerUnavailable");
        held[0].Detail.ShouldContain("has not completed recovery");
        (await rig.EventsAsync(taskId, AgentTaskEventType.Warning)).ShouldBeEmpty();
        (await rig.EventsAsync(taskId, AgentTaskEventType.Dispatched)).ShouldBeEmpty();
        rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0);
        (await rig.ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task A_process_restart_re_arms_the_mirror_and_the_row_is_written_once()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        var mirror = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);

        await rig.TickAsync().WaitAsync(TickBound);
        var first = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);

        // Process death, in-process: the provider (and with it the preparer's registry) is gone.
        await rig.RestartAsync();
        rig.Preparer.IsInFlight(taskId, out _).ShouldBeFalse();

        await rig.TickAsync().WaitAsync(TickBound);
        var second = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 2);
        var a = first.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        var b = second.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        b.ShouldBe(a);

        // The first request's waiter died with provider A; only the re-armed one is answered.
        await rig.Peer.EmitAsync(MirrorResult(second, mirror));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        task.RemoteWorktreePath.ShouldBe(mirror);
        task.RemotePrepFailures.ShouldBe(0);
        task.DispatchNotBeforeAt.ShouldBeNull();
        (await rig.EventsAsync(taskId, AgentTaskEventType.Warning)).ShouldBeEmpty(
            "the cancelled operation on the dead provider must write nothing");
    }

    [Test]
    public async Task Cancel_of_a_queued_runner_task_returns_while_its_mirror_is_pending_and_a_late_success_is_still_recorded()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        var mirror = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);

        var tick = rig.TickAsync();
        var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);

        // CARD-0629: this cancel used to block on the claim's FOR UPDATE lock for the whole mirror.
        using (var scope = rig.Provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .CancelAsync(taskId, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        await tick.WaitAsync(TickBound);
        (await rig.ReadTaskAsync(taskId)).Status.ShouldBe(AgentTaskStatus.Canceled);

        await rig.Peer.EmitAsync(MirrorResult(request, mirror));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        task.Status.ShouldBe(AgentTaskStatus.Canceled);
        task.RemoteWorktreePath.ShouldBe(mirror, "a late mirror must stay known to retirement");
    }

    [Test]
    public async Task C1032_CaptureLateReassignment()
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        var runnerBPath = "/work/worktrees/runner-b-" + RemoteWorkspaceService.MirrorName(taskId);
        await rig.TickAsync().WaitAsync(TickBound);
        var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        var sent = request.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json);
        sent.ShouldNotBeNull();
        sent.Binding.ShouldNotBeNull();

        await using (var db = rig.NewDb())
        {
            await db.AgentTasks.Where(row => row.Id == taskId).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.RunnerId, "runner-b")
                .SetProperty(row => row.RemoteWorktreePath, runnerBPath));
        }

        var path = "/work/worktrees/" + sent.Name;
        var bindingId = Guid.NewGuid();
        await rig.Peer.EmitAsync(BoundResult(request, ValidBinding(sent, bindingId, path), path));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);

        await using var read = rig.NewDb();
        var saved = await read.TaskWorktreeMirrorBindings.AsNoTracking()
            .SingleAsync(row => row.TaskId == taskId && row.RunnerId == sent.Binding.RunnerId);
        var oldRunnerBindingPersisted = saved.BindingId == bindingId;
        oldRunnerBindingPersisted.ShouldBeTrue();
        var currentPath = (await rig.ReadTaskAsync(taskId)).RemoteWorktreePath;
        currentPath.ShouldBe(runnerBPath);
    }

    [Test]
    public async Task C1032_CaptureAttemptAndCancellation()
    {
        var newAttemptPath = "/work/worktrees/attempt-2";
        await using (var attemptRig = await Rig.StartAsync())
        {
            attemptRig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
            var taskId = await attemptRig.SeedAsync();
            await attemptRig.TickAsync().WaitAsync(TickBound);
            var request = await attemptRig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
            var sent = request.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
            await using (var db = attemptRig.NewDb())
            {
                await db.AgentTasks.Where(row => row.Id == taskId).ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.Attempt, 2)
                    .SetProperty(row => row.RemoteWorktreePath, newAttemptPath));
            }

            var path = "/work/worktrees/" + sent.Name;
            await attemptRig.Peer.EmitAsync(BoundResult(request, ValidBinding(sent, Guid.NewGuid(), path), path));
            await attemptRig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
            var currentPath = (await attemptRig.ReadTaskAsync(taskId)).RemoteWorktreePath;
            currentPath.ShouldBe(newAttemptPath);
        }

        await using var cancelRig = await Rig.StartAsync();
        cancelRig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var canceledTask = await cancelRig.SeedAsync();
        var tick = cancelRig.TickAsync();
        var cancelRequest = await cancelRig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        var cancelSent = cancelRequest.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        using (var scope = cancelRig.Provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .CancelAsync(canceledTask, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        await tick.WaitAsync(TickBound);
        var bindingId = Guid.NewGuid();
        var cancelPath = "/work/worktrees/" + cancelSent.Name;
        await cancelRig.Peer.EmitAsync(BoundResult(cancelRequest, ValidBinding(cancelSent, bindingId, cancelPath), cancelPath));
        await cancelRig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        await using var read = cancelRig.NewDb();
        var canceledAttemptBindingPersisted = await read.TaskWorktreeMirrorBindings.AsNoTracking()
            .AnyAsync(row => row.TaskId == canceledTask && row.BindingId == bindingId);
        canceledAttemptBindingPersisted.ShouldBeTrue();
    }

    [Test]
    public async Task C1032_CaptureLostReplyRecovery()
    {
        await using var rig = await Rig.StartAsync(settings =>
        {
            settings.RemotePrepBackoffBaseSeconds = 1;
            settings.RemotePrepBackoffMaxSeconds = 1;
        });
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        await rig.TickAsync().WaitAsync(TickBound);
        var first = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        var firstSent = first.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        var firstPreparationId = firstSent.Binding!.PreparationId;

        await rig.RestartAsync();
        await rig.TickAsync().WaitAsync(TickBound);
        var second = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 2);
        var secondSent = second.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        var recoveryPreparationId = secondSent.Binding!.PreparationId;
        recoveryPreparationId.ShouldBe(firstPreparationId);

        var current = second;
        var count = 2;
        async Task RejectAsync(CompletedCardWorkspaceBinding returned, string responsePath, string label)
        {
            await rig.Peer.EmitAsync(BoundResult(current, returned, responsePath));
            await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
            await using var db = rig.NewDb();
            var pending = await db.TaskWorktreeMirrorBindings.AsNoTracking()
                .SingleAsync(row => row.PreparationId == firstPreparationId);
            var leaked = pending.BindingId != Guid.Empty || pending.WorktreePath.Length > 0 || pending.CommonDirectory.Length > 0;
            var foreignId = returned.BindingId != Guid.Empty
                && await db.TaskWorktreeMirrorBindings.AnyAsync(row => row.BindingId == returned.BindingId);
            var foreignPreparation = returned.PreparationId != firstPreparationId
                && await db.TaskWorktreeMirrorBindings.AnyAsync(row => row.PreparationId == returned.PreparationId);
            var invalidBindingPersisted = leaked || foreignId || foreignPreparation;
            invalidBindingPersisted.ShouldBeFalse(label);
            await rig.AdvanceAsync(TimeSpan.FromSeconds(1));
            await rig.TickAsync().WaitAsync(TickBound);
            count++;
            current = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, count);
        }

        var basis = secondSent;
        var path = "/work/worktrees/" + basis.Name;
        var valid = ValidBinding(basis, Guid.NewGuid(), path);
        await RejectAsync(valid with { TaskId = Guid.NewGuid() }, path, "capture.TaskId");
        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        valid = ValidBinding(basis, Guid.NewGuid(), path);
        await RejectAsync(valid with { Attempt = basis.Binding!.Attempt + 1 }, path, "capture.Attempt");
        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        valid = ValidBinding(basis, Guid.NewGuid(), path);
        await RejectAsync(valid with { PreparationId = Guid.NewGuid() }, path, "capture.PreparationId");
        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        valid = ValidBinding(basis, Guid.NewGuid(), path);
        await RejectAsync(valid with { RunnerId = "runner-other" }, path, "capture.RunnerId");
        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        valid = ValidBinding(basis, Guid.NewGuid(), path);
        await RejectAsync(valid with { RunnerStoreId = Guid.NewGuid().ToString("D") }, path, "capture.RunnerStoreId");
        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        valid = ValidBinding(basis, Guid.Empty, path);
        await RejectAsync(valid, path, "capture.BindingId");
        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        valid = ValidBinding(basis, Guid.NewGuid(), path);
        await RejectAsync(valid with { RepositoryIdentity = "https://github.com/other/repo.git" }, path, "capture.RepositoryIdentity");
        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        valid = ValidBinding(basis, Guid.NewGuid(), "");
        await RejectAsync(valid, "", "capture.WorktreePath");
        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        valid = ValidBinding(basis, Guid.NewGuid(), path);
        await RejectAsync(valid with { SourceFullRef = "refs/heads/other" }, path, "capture.SourceFullRef");
        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        valid = ValidBinding(basis, Guid.NewGuid(), path);
        await RejectAsync(valid with { CreationBaselineSha = new string('a', 40) }, path, "capture.CreationBaselineSha");

        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        var acceptedId = Guid.NewGuid();
        var accepted = ValidBinding(basis, acceptedId, path);
        await rig.Peer.EmitAsync(BoundResult(current, accepted, path));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        await using (var saved = rig.NewDb())
        {
            var row = await saved.TaskWorktreeMirrorBindings.AsNoTracking()
                .SingleAsync(candidate => candidate.PreparationId == firstPreparationId);
            row.BindingId.ShouldBe(acceptedId);
            await saved.AgentTasks.Where(candidate => candidate.Id == taskId).ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.RemoteWorktreePath, (string?)null));
        }

        await rig.TickAsync().WaitAsync(TickBound);
        count++;
        current = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, count);
        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        basis.Binding!.PreparationId.ShouldBe(firstPreparationId);
        var changedId = Guid.NewGuid();
        await rig.Peer.EmitAsync(BoundResult(current, ValidBinding(basis, changedId, path), path));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        await using (var afterId = rig.NewDb())
        {
            var invalidBindingPersisted = await afterId.TaskWorktreeMirrorBindings.AnyAsync(row => row.BindingId == changedId);
            invalidBindingPersisted.ShouldBeFalse("capture.BindingId");
            (await afterId.TaskWorktreeMirrorBindings.SingleAsync(row => row.PreparationId == firstPreparationId))
                .BindingId.ShouldBe(acceptedId);
        }

        await rig.AdvanceAsync(TimeSpan.FromSeconds(1));
        await rig.TickAsync().WaitAsync(TickBound);
        count++;
        current = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, count);
        basis = current.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        var changedPath = path + "-changed";
        await rig.Peer.EmitAsync(BoundResult(current, ValidBinding(basis, acceptedId, changedPath), changedPath));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        await using var afterPath = rig.NewDb();
        var pathPersisted = await afterPath.TaskWorktreeMirrorBindings.AnyAsync(row => row.WorktreePath == changedPath);
        pathPersisted.ShouldBeFalse("capture.WorktreePath");
    }

    [Test]
    public async Task C1032_StoreReplacementKeepsDebt()
    {
        // case: a preparation is recorded against the live store, then that runner's store is replaced and the task retries.
        // mutation: reuse the task/attempt/runner row even when its store id is not the live store, and send that old store to the replacement runner.
        // red: reusedOldStore.ShouldBeFalse() — the retry repeats the old preparation and can complete it against the new store.
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        await rig.TickAsync().WaitAsync(TickBound);
        var first = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        var firstSent = first.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;
        var oldStore = firstSent.Binding!.RunnerStoreId!;
        var oldPreparation = firstSent.Binding.PreparationId;

        await rig.RestartAsync();
        var newStore = await rig.ReplaceRunnerStoreAsync();
        rig.Host.Directory.GetLiveStoreId(rig.RunnerId).ShouldBe(newStore);
        await rig.TickAsync().WaitAsync(TickBound);
        var second = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        var secondSent = second.Payload!.Value.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json)!;

        await using var read = rig.NewDb();
        var debt = await read.TaskWorktreeMirrorBindings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.PreparationId == oldPreparation);
        var reusedOldStore = secondSent.Binding!.PreparationId == oldPreparation
            || string.Equals(secondSent.Binding.RunnerStoreId, oldStore, StringComparison.Ordinal)
            || debt is null
            || !string.Equals(debt.RunnerStoreId, oldStore, StringComparison.Ordinal)
            || debt.BindingId != Guid.Empty
            || debt.WorktreePath.Length > 0;
        reusedOldStore.ShouldBeFalse();
        secondSent.Binding.RunnerStoreId.ShouldBe(newStore.ToString("D"));
        secondSent.Binding.RunnerId.ShouldBe(firstSent.Binding.RunnerId);
        secondSent.Binding.PreparationId.ShouldNotBe(oldPreparation);
        var current = await read.TaskWorktreeMirrorBindings.AsNoTracking()
            .SingleAsync(row => row.PreparationId == secondSent.Binding.PreparationId);
        current.RunnerStoreId.ShouldBe(newStore.ToString("D"));
        current.BindingId.ShouldBe(Guid.Empty);
        (await read.TaskWorktreeMirrorBindings.CountAsync(row => row.TaskId == taskId)).ShouldBe(2);
    }

    [Test]
    [Arguments("prepared")]
    [Arguments("unprepared")]
    [Arguments("repair-source")]
    [Arguments("snapshot")]
    [Arguments("interim")]
    [Arguments("rearm")]
    // PC-7 witness: absent baseline, configuration-identical to unprepared on purpose.
    [Arguments("rearm-no-baseline")]
    public async Task C672_prepared_task_launches_while_a_land_holds_the_lease(string arm)
    {
        await using var rig = await Rig.StartAsync();
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var mirror = "/work/worktrees/c672-" + arm;
        Guid? repairOwner = arm == "repair-source" ? await rig.SeedOwnerAsync() : null;
        Guid? snapshotOperation = arm == "snapshot" ? await rig.SeedLandingAsync(await rig.SeedOwnerAsync()) : null;
        var taskId = await rig.SeedAsync(t =>
        {
            t.RemoteWorktreePath = arm is "unprepared" or "rearm" or "rearm-no-baseline" ? null : mirror;
            t.RepairSourceTaskId = repairOwner;
            t.SourceLandingOperationId = snapshotOperation;
            if (arm == "interim")
                t.VerificationRound = VerificationRound.Interim;
            if (arm == "rearm")
                t.ProgressBaselineJson = "{\"schemaVersion\":1}";
            if (arm == "rearm-no-baseline")
                t.ProgressBaselineJson = null;
        });
        rig.Lease.Held = true;

        var tick = await rig.TickAsync().WaitAsync(TickBound);

        var task = await rig.ReadTaskAsync(taskId);
        var held = await rig.EventsAsync(taskId, AgentTaskEventType.Held);
        if (arm == "prepared")
        {
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
            rig.Sink.Specs.ShouldHaveSingleItem().Cwd.ShouldBe(mirror);
            held.ShouldNotContain(e => e.Detail.Contains("repository mutation lease", StringComparison.Ordinal));
            rig.Waiters.IsEmpty.ShouldBeTrue();
        }
        else if (arm == "rearm")
        {
            // Captured baseline: the claim runs no git, so a land's lease does not queue the re-arm.
            tick.HeldOnLease.ShouldBe(0);
            task.Status.ShouldBe(AgentTaskStatus.Queued);
            held.Last().Detail.ShouldStartWith(DispatchHoldDetails.RemoteMirrorRequestedPrefix);
            held.ShouldNotContain(e => e.Detail.Contains("repository mutation lease", StringComparison.Ordinal));
            rig.Waiters.IsEmpty.ShouldBeTrue();
            var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
            rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(1);
            await rig.Peer.EmitAsync(MirrorResult(request, mirror));
            await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        }
        else
        {
            tick.HeldOnLease.ShouldBe(1);
            task.Status.ShouldBe(AgentTaskStatus.Queued);
            held.ShouldHaveSingleItem().Detail.ShouldBe(DispatchHoldDetails.LeaseHeldByOwner(
                rig.Lease.HolderTaskId, RepositoryLeasePurposes.Land, rig.Lease.AcquiredAt));
            rig.Sink.Specs.ShouldBeEmpty();
            rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0);
        }
    }

    [Test]
    public async Task C672_three_queued_runner_tasks_cross_the_lease_once_and_launch_behind_the_next_land()
    {
        await using var rig = await Rig.StartAsync(capacity: 3);
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            // Each its own desktop worktree and branch, as three real tasks would have.
            var worktree = Directory.CreateDirectory(Path.Combine(rig.WorkspacePath, $"wt-{i}")).FullName;
            ids.Add(await rig.SeedAsync(t =>
            {
                t.WorktreePath = worktree;
                t.WorktreeBranch = RemoteWorkspaceService.OwnedBranch(t.Id);
            }));
        }
        var common = Path.GetFullPath(rig.WorkspacePath);

        // (1) A land holds the lease: every first crossing is refused and waits for it.
        rig.Lease.Held = true;
        (await rig.TickAsync().WaitAsync(TickBound)).HeldOnLease.ShouldBe(3);
        foreach (var id in ids)
        {
            (await rig.EventsAsync(id, AgentTaskEventType.Held)).ShouldHaveSingleItem().Detail
                .ShouldContain("repository mutation lease");
        }
        var waiting = rig.Waiters.Snapshot(common);
        waiting.Select(w => w.TaskId).ShouldBe(ids, ignoreOrder: true);
        waiting.ShouldAllBe(w => w.Purpose == RepositoryLeasePurposes.Dispatch);

        // (2) The gap: each crosses once, the preparer mirrors all three off the tick. The clock
        // moves one tick cadence so this tick's Held rows sort after the first tick's.
        rig.Lease.Held = false;
        await rig.AdvanceAsync(TimeSpan.FromSeconds(5));
        await rig.TickAsync().WaitAsync(TickBound);
        rig.Waiters.IsEmpty.ShouldBeTrue();
        foreach (var id in ids)
        {
            (await rig.EventsAsync(id, AgentTaskEventType.Held)).Last().Detail
                .ShouldStartWith(DispatchHoldDetails.RemoteMirrorRequestedPrefix);
        }
        for (var n = 1; n <= 3; n++)
        {
            var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, n);
            await rig.Peer.EmitAsync(MirrorResult(request, "/work/worktrees/c672-mirror-" + n));
        }
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        foreach (var id in ids)
            (await rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldNotBeNull();
        var heldBefore = new Dictionary<Guid, int>();
        foreach (var id in ids)
            heldBefore[id] = (await rig.EventsAsync(id, AgentTaskEventType.Held)).Count;

        // (3) The next land holds the lease again: the prepared launches do not need it.
        rig.Lease.Held = true;
        await rig.AdvanceAsync(TimeSpan.FromSeconds(5));
        var launched = await rig.TickAsync().WaitAsync(TickBound);
        launched.Dispatched.ShouldBe(3);
        launched.HeldOnLease.ShouldBe(0);
        foreach (var id in ids)
        {
            var task = await rig.ReadTaskAsync(id);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
            (await rig.EventsAsync(id, AgentTaskEventType.Held)).Count.ShouldBe(heldBefore[id]);
        }
        rig.Sink.Specs.Count.ShouldBe(3);
        rig.Waiters.IsEmpty.ShouldBeTrue();
    }

    /// <summary>
    /// CARD-1076 D-2. The production push uses the options overload with the configured minutes
    /// and the task tag, and does not set the desktop upstream.
    /// </summary>
    [Test]
    public async Task C1076_prep_push_carries_the_task_tag_and_the_configured_budget()
    {
        await using var rig = await Rig.StartAsync(s => s.RemotePrepPushBudgetMinutes = 7);
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var taskId = await rig.SeedAsync();
        var mirror = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);

        await rig.TickAsync().WaitAsync(TickBound);
        var git = (PushOnlyGit)rig.Provider.GetRequiredService<ILandingGit>();
        var seen = DateTime.UtcNow + TickBound;
        while (git.Pushes.Count == 0 && DateTime.UtcNow < seen)
            await Task.Delay(20);
        var push = git.Pushes.ShouldHaveSingleItem();
        push.Options.Budget.ShouldBe(TimeSpan.FromMinutes(7));
        push.Options.Child.ShouldBe(new RepositoryChildTag(taskId, RepositoryChildPurposes.RemotePrepPush));
        push.Arguments.ShouldBe(new[] { "push", "origin", RemoteWorkspaceService.OwnedBranch(taskId) });

        var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, 1);
        await rig.Peer.EmitAsync(MirrorResult(request, mirror));
        await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
        (await rig.ReadTaskAsync(taskId)).RemoteWorktreePath.ShouldBe(mirror);
    }

    /// <summary>
    /// CARD-1076 D-4. One push at a time per repository. Each waiter names the task holding the
    /// turn. CARD-1093: after the first pusher releases, the remaining waiter names the task that
    /// took the turn, not the holder recorded when that waiter entered the gate.
    /// PC-1 (pending Mutation): make <c>Progress</c> return the holder captured at entry instead
    /// of the gate's current holder. This method goes red because the remaining waiter still
    /// names the finished pusher.
    /// </summary>
    [Test]
    public async Task C1076_pushes_on_one_repository_run_one_at_a_time_and_the_waiter_names_the_pusher()
    {
        await using var rig = await Rig.StartAsync(capacity: 3);
        rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var git = (PushOnlyGit)rig.Provider.GetRequiredService<ILandingGit>();
        git.HoldFirstPush = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        git.HoldSecondPush = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var worktree = Directory.CreateDirectory(Path.Combine(rig.WorkspacePath, $"wt-{i}")).FullName;
            ids.Add(await rig.SeedAsync(t =>
            {
                t.WorktreePath = worktree;
                t.WorktreeBranch = RemoteWorkspaceService.OwnedBranch(t.Id);
            }));
        }

        try
        {
            await rig.TickAsync().WaitAsync(TickBound);
            foreach (var id in ids)
            {
                (await rig.EventsAsync(id, AgentTaskEventType.Held)).Last().Detail
                    .ShouldStartWith(DispatchHoldDetails.RemoteMirrorRequestedPrefix);
            }

            var deadline = DateTime.UtcNow + TickBound;
            Guid firstPusher = Guid.Empty;
            var firstWaiters = 0;
            string? last = null;
            while (DateTime.UtcNow < deadline)
            {
                var rows = ids.Select(id => (id, progress: rig.Preparer.Progress(id))).ToArray();
                last = Describe(rows, git);
                var pushing = rows.Where(row => row.progress?.Phase == RemotePrepPhase.Pushing).ToArray();
                var queued = rows.Where(row => row.progress?.Phase == RemotePrepPhase.WaitingForPushTurn).ToArray();
                if (pushing.Length == 1 && queued.Length == 2
                    && queued.All(row => row.progress!.Value.BehindTaskId == pushing[0].id)
                    && git.MaxConcurrentPushes == 1 && git.Pushes.Count == 1)
                {
                    firstPusher = pushing[0].id;
                    firstWaiters = queued.Length;
                    break;
                }

                await Task.Delay(20);
            }

            firstWaiters.ShouldBe(2, last);
            git.MaxConcurrentPushes.ShouldBe(1);

            git.HoldFirstPush.TrySetResult();
            deadline = DateTime.UtcNow + TickBound;
            Guid secondPusher = Guid.Empty;
            RemotePrepProgress? stillWaiting = null;
            while (DateTime.UtcNow < deadline)
            {
                var rows = ids.Select(id => (id, progress: rig.Preparer.Progress(id))).ToArray();
                last = Describe(rows, git);
                var pushing = rows.Where(row => row.progress?.Phase == RemotePrepPhase.Pushing).ToArray();
                var queued = rows.Where(row => row.progress?.Phase == RemotePrepPhase.WaitingForPushTurn).ToArray();
                if (pushing.Length == 1 && pushing[0].id != firstPusher
                    && queued.Length == 1
                    && queued[0].progress!.Value.BehindTaskId == pushing[0].id
                    && queued[0].progress!.Value.BehindTaskId != firstPusher
                    && git.MaxConcurrentPushes == 1 && git.Pushes.Count == 2)
                {
                    secondPusher = pushing[0].id;
                    stillWaiting = queued[0].progress;
                    break;
                }

                await Task.Delay(20);
            }

            stillWaiting.ShouldNotBeNull(
                $"remaining waiter did not name the new pusher; {last}");
            stillWaiting!.Value.Phase.ShouldBe(RemotePrepPhase.WaitingForPushTurn);
            stillWaiting.Value.BehindTaskId.ShouldBe(secondPusher);
            stillWaiting.Value.BehindTaskId.ShouldNotBe(firstPusher);

            git.HoldSecondPush.TrySetResult();
            for (var n = 1; n <= 3; n++)
            {
                var request = await rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceMirror, n);
                await rig.Peer.EmitAsync(MirrorResult(request, "/work/worktrees/c1076-mirror-" + n));
            }

            await rig.Preparer.WhenIdleAsync().WaitAsync(TickBound);
            git.MaxConcurrentPushes.ShouldBe(1);
            git.Pushes.Count.ShouldBe(3);
            foreach (var id in ids)
            {
                (await rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldNotBeNull();
                (await rig.EventsAsync(id, AgentTaskEventType.Warning)).ShouldBeEmpty();
            }
        }
        finally
        {
            git.HoldFirstPush.TrySetResult();
            git.HoldSecondPush.TrySetResult();
        }
    }

    private static string Describe(
        IEnumerable<(Guid id, RemotePrepProgress? progress)> rows, PushOnlyGit git) =>
        $"pushes={git.Pushes.Count} max={git.MaxConcurrentPushes} "
        + string.Join("; ", rows.Select(row =>
            $"{row.id:N} phase={row.progress?.Phase} behind={row.progress?.BehindTaskId:N}"));

    /// <summary>
    /// Review 3488192e (2): V-1 and V-12 carried to the recipient. The card's timeline for one
    /// runner task - refused at crossing 1 while a land holds the lease, mirrored in the gap, then
    /// launched lease-free while the NEXT land holds it - through the real launch queue and the
    /// real session message queue to a runtime-produced, complete UserPrompt. Busy and already
    /// eligible recipients, and one injected failure per named handoff (review c1c0dd1a (3)):
    /// <c>queue-inserted</c> faults the enqueue boundary - the brief's row commits, then the
    /// dispatcher's enqueue call throws - and the SAME incarnation's boot flush must still deliver
    /// it; <c>lost-wakeup</c> crashes at the boot-flush boundary - the incarnation that queued the
    /// brief never passes ready, so its wakeup is lost - and a restarted queue service delivers it.
    /// Every step is joined by durable
    /// identity: task -> Dispatched event -> AgentSessionId -> queued row (ExecutionTaskId) ->
    /// the recipient's UserPrompt. The phone-home host owns the fake clock; the queue harness stays
    /// on the system clock, as in DispatcherRemotePrepStarvationTests.
    /// </summary>
    [Test]
    [Arguments("after-receipt", false)]
    [Arguments("after-receipt", true)]
    [Arguments("queue-inserted", false)]
    [Arguments("lost-wakeup", false)]
    public async Task C672_prepared_runner_brief_reaches_its_session_while_a_land_holds_the_lease(string cut, bool busy)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        host.Capacity = 2;
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        var lease = new HoldableLease();
        var slot = new AdapterSlot();
        var taskId = Guid.NewGuid();
        var enqueueFault = cut == "queue-inserted" ? new BriefEnqueueFault(taskId) : null;
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            Delegation = new DelegationSettings { MaxConcurrentTasks = 32, AllowedRoots = ["C:\\", "/"] },
            ConfigureServices = services => ConfigureDelivery(services, host, slot, lease),
            ConfigureDbContext = enqueueFault is null ? null : o => o.AddInterceptors(enqueueFault),
        });
        // Hold boot-ready for the deferred arms, so the brief cannot be typed before the cut.
        var deferDelivery = busy || cut is "queue-inserted" or "lost-wakeup";
        var ready = deferDelivery
            ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        slot.Adapter = harness.Adapter;
        harness.Adapter.RegisterOnStart = harness.Runtime;
        harness.Adapter.ReadyHold = ready;
        harness.Adapter.OnSubmitted = async submitted =>
        {
            if (harness.Adapter.StartedSessionId is not Guid sid)
                return;
            await BridgeQueueHarness.InsertEntryAsync(
                sid, TranscriptKinds.UserPrompt, submitted, timestamp: DateTime.UtcNow,
                connectionString: schema.ConnectionString);
            await BridgeQueueHarness.InsertEntryAsync(
                sid, TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.EndTurn,
                connectionString: schema.ConnectionString);
        };

        var workspace = Path.Combine(harness.TempRoot, "workspace");
        var mirror = "/work/worktrees/" + RemoteWorkspaceService.MirrorName(taskId);
        await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            seed.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "remote recipient", Goal = "c672-brief-sentinel",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Custom, AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = workspace, RepoPath = workspace, WorktreePath = workspace,
                WorktreeBranch = RemoteWorkspaceService.OwnedBranch(taskId), RunnerId = host.AllowedRunnerId,
                Status = AgentTaskStatus.Queued, ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = DateTime.UtcNow, ConcurrencyToken = Guid.NewGuid(),
            });
            await seed.SaveChangesAsync();
        }

        var waiters = harness.Provider.GetRequiredService<RepositoryLeaseWaiters>();
        var common = Path.GetFullPath(workspace);
        async Task<AgentTaskDispatcher.TickResult> TickAsync()
        {
            using var scope = harness.Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .TickAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
        }
        async Task<AgentTask> ReadAsync()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        }

        // (1) A land holds the lease: crossing 1 is refused and the task waits for it.
        lease.Held = true;
        (await TickAsync()).HeldOnLease.ShouldBe(1);
        (await ReadAsync()).Status.ShouldBe(AgentTaskStatus.Queued);
        waiters.Snapshot(common).ShouldHaveSingleItem().TaskId.ShouldBe(taskId);

        // (2) The gap: crossing 1, then the mirror off the tick.
        lease.Held = false;
        peer.Reply = frame => frame.Operation == PhoneHomeOperation.WorkspaceMirror
            ? MirrorResult(frame, mirror)
            : null;
        (await TickAsync()).Dispatched.ShouldBe(0);
        await harness.Provider.GetRequiredService<RemoteWorkspacePreparer>().WhenIdleAsync()
            .WaitAsync(TimeSpan.FromSeconds(15));
        var prepared = await ReadAsync();
        prepared.Status.ShouldBe(AgentTaskStatus.Queued);
        prepared.RemoteWorktreePath.ShouldBe(mirror);
        waiters.IsEmpty.ShouldBeTrue();

        // (3) The next land holds the lease: crossing 2 launches without it.
        lease.Held = true;
        var launched = await TickAsync();
        launched.Dispatched.ShouldBe(1);
        launched.HeldOnLease.ShouldBe(0);
        waiters.IsEmpty.ShouldBeTrue();

        await using var read = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await read.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
        var sessionId = task.AgentSessionId.ShouldNotBeNull();
        var session = await read.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        session.RunnerId.ShouldBe(host.AllowedRunnerId);
        session.RunnerCwd.ShouldBe(mirror);
        (await read.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Dispatched)
                .ToListAsync())
            .ShouldHaveSingleItem();
        (await read.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Held)
                .Select(e => e.Detail)
                .ToListAsync())
            .Count(d => d.Contains("repository mutation lease", StringComparison.Ordinal))
            .ShouldBe(1, "only crossing 1 waited for the lease");
        var queued = await read.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.ExecutionTaskId == taskId);
        queued.AgentSessionId.ShouldBe(sessionId);
        queued.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        queued.Body.ShouldContain(DelegationReportFormatter.TaskMarker(taskId));

        var launchQueue = harness.Provider.GetRequiredService<AgentSessionLaunchQueue>();
        async Task<int> PromptCountAsync() => await read.TranscriptEntries.AsNoTracking()
            .CountAsync(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt);
        if (cut is "queue-inserted" or "lost-wakeup")
        {
            // Both cuts start from a committed, undelivered brief with the launch parked at ready.
            queued.Status.ShouldBe(QueuedMessageStatus.Pending);
            queued.DeliveryAttempts.ShouldBe(0);
            harness.Adapter.SubmittedBodies.ShouldBeEmpty();
            (await PromptCountAsync()).ShouldBe(0);
        }

        if (busy)
        {
            await QueuedReceiptAssertions.HoldRecipientBusyAsync(schema.ConnectionString, sessionId);
            ready!.TrySetResult(true);
        }
        else if (cut == "queue-inserted")
        {
            // The enqueue boundary: the row committed and then the enqueue call threw. The
            // dispatcher kept the task Dispatched (asserted above); this incarnation's own boot
            // flush is the recovery - no restart, no second queue service.
            enqueueFault!.Faulted.ShouldBe(1, "the dispatcher's brief enqueue took the injected fault");
            ready!.TrySetResult(true);
        }
        else if (cut == "lost-wakeup")
        {
            // The crash boundary: the incarnation that queued the brief dies before its boot flush,
            // so its launch never passes ready. Only a restarted queue service can deliver the row.
            ready!.TrySetResult(false);
            await launchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            await using (var failedRead = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var failedSession = await failedRead.AgentSessions.AsNoTracking()
                    .SingleAsync(s => s.Id == sessionId);
                failedSession.Status.ShouldBe(SessionStatus.Failed);
                failedSession.FailureReason.ShouldContain(AgentSessionService.NotReadyBase);
                var pendingBrief = await failedRead.SessionQueuedMessages.AsNoTracking()
                    .SingleAsync(m => m.Id == queued.Id);
                pendingBrief.Status.ShouldBe(QueuedMessageStatus.Pending);
                (await failedRead.TranscriptEntries.AsNoTracking()
                    .CountAsync(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt))
                    .ShouldBe(0);
            }
            await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
                schema.ConnectionString, harness, queued, sessionId, busy, cut);
        }

        if (cut != "lost-wakeup")
        {
            await launchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            // Check launch before the receipt helper can attach an adapter or mark the session live.
            harness.Adapter.StartedSessionId.ShouldBe(sessionId);
            var started = await read.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
            started.Status.ShouldBe(SessionStatus.Running, started.FailureReason);
            started.InteractiveLaunchCompletedAt.ShouldNotBeNull();
            if (cut == "queue-inserted")
                (await PromptCountAsync()).ShouldBe(1, "this incarnation's boot flush must deliver the committed brief");
            await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
                schema.ConnectionString, harness, queued, sessionId, busy, busyBeforeDelivery: busy);
        }

        // The one complete UserPrompt on the task's own session carries the task's own brief.
        var prompts = await read.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt)
            .ToListAsync();
        prompts.ShouldHaveSingleItem().Text.ShouldNotBeNull()
            .ShouldContain(DelegationReportFormatter.TaskMarker(taskId));
        harness.Adapter.StartedSessionId.ShouldBe(sessionId);
        if (cut == "lost-wakeup")
        {
            (await PromptCountAsync()).ShouldBe(1);
        }
    }

    private static void ConfigureDelivery(
        IServiceCollection services, PhoneHomeTestHost host, AdapterSlot slot, HoldableLease lease)
    {
        services.RemoveAll<IOptionsMonitor<AgentRegistrySettings>>();
        services.RemoveAll<AgentRegistry>();
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
            new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
            {
                DefaultDefinition = "claude",
                GrokCredentialProbeEnabled = false,
                Definitions =
                {
                    ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" },
                },
            }));
        services.AddSingleton<AgentRegistry>();
        services.RemoveAll<IAgentProtocolAdapterFactory>();
        services.AddSingleton<IAgentProtocolAdapterFactory>(slot);
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), $"antiphon-c672-delivery-{Guid.NewGuid():N}"),
        });
        services.AddSingleton<IRepositoryMutationLease>(lease);
        services.RemoveAll<ILandingGit>();
        services.AddSingleton<ILandingGit, PushOnlyGit>();
        services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true, AllowedRunnerId = host.AllowedRunnerId, AllowDelegatedTasks = true,
            HostWorkspaceRoot = @"C:\src\Antiphon", CallbackOrigin = "https://antiphon.desktop.codeperf.net",
            SharedSecret = "x", ClaudeAuthProbeEnabled = false,
        }));
        services.AddSingleton<PhoneHomeLaunchPolicy>();
        services.AddSingleton<ISessionRunnerDirectory>(host.Directory);
        services.AddScoped<RemoteWorkspaceService>();
        services.AddSingleton<RemoteWorkspacePreparer>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
    }

    /// <summary>
    /// The enqueue-boundary fault: the delegation brief's queue row for one task commits, and then
    /// the enqueue call throws, as a transport or process error after the insert would.
    /// CARD-1150 saves that insert inside an explicit transaction, so SavedChanges runs before
    /// CommitAsync and a throw there rolls the row back. The fault waits until the commit is
    /// acknowledged.
    /// </summary>
    private sealed class BriefEnqueueFault(Guid taskId) : SaveChangesInterceptor, IDbTransactionInterceptor
    {
        private DbTransaction? _briefTransaction;
        private int _faulted;

        public int Faulted => Volatile.Read(ref _faulted);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Faulted == 0 && eventData.Context!.ChangeTracker.Entries<SessionQueuedMessage>()
                .Any(e => e.State == EntityState.Added && e.Entity.ExecutionTaskId == taskId
                    && e.Entity.Origin == QueuedMessageOrigin.Delegation))
                Volatile.Write(ref _briefTransaction,
                    eventData.Context.Database.CurrentTransaction?.GetDbTransaction()
                    ?? throw new InvalidOperationException("The brief insert must own an explicit transaction."));
            return ValueTask.FromResult(result);
        }

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            // This interceptor is shared by every DbContext in the harness. A concurrent launch
            // admission commit must not consume the brief fault before the adapter starts.
            if (ReferenceEquals(transaction, Volatile.Read(ref _briefTransaction))
                && Interlocked.CompareExchange(ref _faulted, 1, 0) == 0)
            {
                throw new IOException("injected enqueue-boundary fault: the brief row committed, the enqueue call failed");
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>The harness's fake TUI as the launch factory, filled once the harness exists.</summary>
    private sealed class AdapterSlot : IAgentProtocolAdapterFactory
    {
        public FakeAgentProtocolAdapter? Adapter { get; set; }

        public IAgentProtocolAdapter Create(AgentKind kind) =>
            Adapter ?? throw new InvalidOperationException("The receipt adapter is not installed yet.");
    }

    internal static PhoneHomeFrame MirrorResult(PhoneHomeFrame request, string path) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceMirrorResponse(path), PhoneHomeFraming.Json));

    internal static PhoneHomeFrame BoundResult(PhoneHomeFrame request, CompletedCardWorkspaceBinding binding, string path) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceMirrorResponse(path, binding), PhoneHomeFraming.Json));

    internal static CompletedCardWorkspaceBinding ValidBinding(
        PhoneHomeWorkspaceMirrorRequest sent, Guid bindingId, string path)
    {
        var binding = sent.Binding!;
        return new CompletedCardWorkspaceBinding(
            binding.TaskId,
            binding.Attempt,
            bindingId,
            binding.PreparationId,
            binding.RunnerId,
            binding.RunnerStoreId!,
            sent.Repository!,
            "/common/c1032",
            "/git/c1032",
            path,
            binding.SourceFullRef!,
            sent.Sha);
    }

    internal sealed class Rig : IAsyncDisposable
    {
        private readonly List<IServiceScope> _scopes = [];
        private Action<DelegationSettings>? _tweak;
        private bool _recovered;
        private Action<IServiceCollection>? _configure;

        public IsolatedTestSchema Schema { get; private set; } = null!;
        public PhoneHomeTestHost Host { get; private set; } = null!;
        public PhoneHomeScriptedPeer Peer { get; private set; } = null!;
        public PhoneHomeLiveConnection Live { get; private set; } = null!;
        public FakeTimeProvider Clock { get; } =
            new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        public ServiceProvider Provider { get; private set; } = null!;
        public RecordingLaunchSink Sink { get; } = new();
        public string WorkspacePath { get; } =
            Directory.CreateTempSubdirectory("antiphon-remote-prep-ws").FullName;
        public string RunnerId => Host.AllowedRunnerId;
        public DateTime Now => Clock.GetUtcNow().UtcDateTime;
        public RemoteWorkspacePreparer Preparer => Provider.GetRequiredService<RemoteWorkspacePreparer>();

        /// <summary>CARD-0672: the repository mutation lease every dispatch in this rig sees.</summary>
        public HoldableLease Lease { get; } = new();
        public RepositoryLeaseWaiters Waiters => Provider.GetRequiredService<RepositoryLeaseWaiters>();

        public static async Task<Rig> StartAsync(
            Action<DelegationSettings>? tweak = null, bool recovered = true, int capacity = 1,
            Action<IServiceCollection>? configure = null)
        {
            var rig = new Rig { _tweak = tweak, _recovered = recovered, _configure = configure };
            rig.Schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            rig.Host = await PhoneHomeTestHost.StartAsync(rig.Clock);
            rig.Host.Capacity = capacity;
            rig.Peer = await rig.Host.ConnectPeerAsync();
            rig.Live = await rig.Host.WaitLiveAsync();
            if (recovered)
                rig.Host.Directory.MarkRecovered(rig.Live);
            rig.Provider = rig.BuildProvider();
            return rig;
        }

        public AppDbContext NewDb() =>
            new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

        /// <summary>One tick on a fresh scope, as the hosted service resolves one per tick.</summary>
        public Task<AgentTaskDispatcher.TickResult> TickAsync()
        {
            var scope = Provider.CreateScope();
            _scopes.Add(scope);
            return scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
        }

        /// <summary>
        /// Moves the shared fake clock. The runner's lease is on the same clock, so the heartbeat
        /// is renewed with it: this test is about the mirror budget, not lease expiry.
        /// </summary>
        public async Task AdvanceAsync(TimeSpan by)
        {
            // The request's budget timer is armed right after its frame is written; let the server
            // reach it before moving the clock (as PhoneHomeConnectionTests does).
            await Task.Delay(100);
            Clock.Advance(by);
            Live.NoteHeartbeat(Clock.GetUtcNow());
            if (_recovered)
                Host.Directory.MarkRecovered(Live);
        }

        public async Task<PhoneHomeFrame> WaitForRequestsAsync(PhoneHomeOperation operation, int count)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (Peer.RequestCount(operation) >= count)
                {
                    return Peer.Incoming.ToArray()
                        .Where(f => f.Kind == PhoneHomeFrameKind.Request && f.Operation == operation)
                        .ElementAt(count - 1);
                }

                await Task.Delay(20);
            }

            throw new TimeoutException($"Expected {count} {operation} request(s); saw {Peer.RequestCount(operation)}.");
        }

        public async Task SeedOccupantAsync(SessionStatus status)
        {
            var now = Now;
            await using var db = NewDb();
            db.AgentSessions.Add(new AgentSession
            {
                Id = Guid.NewGuid(),
                DefinitionName = "grok",
                AgentKind = AgentKind.Grok,
                Status = status,
                Cwd = WorkspacePath,
                Cols = 80,
                Rows = 24,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
                EndedAt = status is SessionStatus.Failed or SessionStatus.Stopped ? now : null,
                RunnerId = RunnerId,
                RunnerStoreId = Host.StoreId,
                RunnerCwd = "/work",
            });
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedAsync(Action<AgentTask>? tweak = null)
        {
            var now = Now;
            var id = Guid.NewGuid();
            await using var db = NewDb();
            // CARD-0672: RepoPath makes the claim consult the rig's lease, as production does.
            var task = new AgentTask
            {
                Id = id, RootTaskId = id, Title = "remote task", Goal = "reply",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Custom, AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Frontier, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = WorkspacePath, RepoPath = WorkspacePath, WorktreePath = WorkspacePath,
                WorktreeBranch = RemoteWorkspaceService.OwnedBranch(id),
                RunnerId = RunnerId, Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
            };
            tweak?.Invoke(task);
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return id;
        }

        /// <summary>A settled Worktree task a repair or a verification snapshot can name.</summary>
        public async Task<Guid> SeedOwnerAsync()
        {
            var id = Guid.NewGuid();
            await using var db = NewDb();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, Title = "owner", Goal = "owner", Role = AgentTaskRole.Code,
                Workspace = WorkspaceMode.Worktree, WorkingDirectory = WorkspacePath, RepoPath = WorkspacePath,
                WorktreeBranch = RemoteWorkspaceService.OwnedBranch(id), Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = Now, CompletedAt = Now,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<Guid> SeedLandingAsync(Guid ownerId)
        {
            var id = Guid.NewGuid();
            await using var db = NewDb();
            db.AgentTaskLandings.Add(new AgentTaskLanding
            {
                Id = id, TaskId = ownerId, CreatedAt = Now, UpdatedAt = Now,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<AgentTask> ReadTaskAsync(Guid id)
        {
            await using var db = NewDb();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        }

        public async Task<List<AgentTaskEvent>> EventsAsync(Guid id, AgentTaskEventType type)
        {
            await using var db = NewDb();
            return await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == id && e.Type == type)
                .OrderBy(e => e.At).ThenBy(e => e.Id)
                .ToListAsync();
        }

        /// <summary>
        /// Authorizes a replacement store the way a cleared retirement does, then reconnects.
        /// The previous preparation row is left untouched.
        /// </summary>
        public async Task<Guid> ReplaceRunnerStoreAsync()
        {
            Host.Directory.Disconnect(Live, "store-replaced");
            await Peer.DisposeAsync();
            Clock.Advance(TimeSpan.FromSeconds(91));
            var now = Clock.GetUtcNow();
            Host.Directory.ApplyState(RunnerId, new RunnerState(
                true, now, "retire-for-store", null, false, null, now, "store-replaced"));
            Host.Directory.ApplyState(RunnerId, new RunnerState(
                false, null, null, null, false, null, null, null));
            var store = Guid.NewGuid();
            Peer = await Host.ConnectPeerAsync(bootId: Guid.NewGuid(), storeId: store);
            Peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
            Live = await Host.WaitLiveAsync();
            Host.Directory.MarkRecovered(Live);
            return store;
        }

        /// <summary>Drops the whole provider (registry included) and builds a new one.</summary>
        public async Task RestartAsync()
        {
            foreach (var scope in _scopes)
                scope.Dispose();
            _scopes.Clear();
            await Provider.DisposeAsync();
            Provider = BuildProvider();
        }

        private ServiceProvider BuildProvider()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(Schema.ConnectionString, n =>
            {
                n.MigrationsAssembly("Antiphon.Server");
                n.SetPostgresVersion(16, 0);
            }));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            var delegation = new DelegationSettings { MaxConcurrentTasks = 512, AllowedRoots = ["C:\\", "/"] };
            _tweak?.Invoke(delegation);
            services.AddSingleton(Options.Create(delegation));
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddOptions<AgentRegistrySettings>().Configure(s =>
            {
                s.DefaultDefinition = "grok";
                s.GrokCredentialProbeEnabled = false;
                s.Definitions["grok"] = new AgentDefinition
                {
                    Kind = "Grok", Exe = "grok.exe", ArgsTemplate = ["--always-approve", "--no-alt-screen"],
                };
            });
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), $"antiphon-remote-prep-{Guid.NewGuid():N}"),
            });
            services.AddSingleton<IRepositoryMutationLease>(Lease);
            services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings
            {
                Enabled = true, AllowedRunnerId = Host.AllowedRunnerId, AllowDelegatedTasks = true,
                HostWorkspaceRoot = @"C:\src\Antiphon", CallbackOrigin = "https://antiphon.desktop.codeperf.net",
                SharedSecret = "x", ClaudeAuthProbeEnabled = false,
            }));
            services.AddSingleton<PhoneHomeLaunchPolicy>();
            services.AddSingleton<ISessionRunnerDirectory>(Host.Directory);
            services.AddSingleton<ILandingGit, PushOnlyGit>();
            services.AddScoped<RemoteWorkspaceService>();
            services.AddSingleton<RemoteWorkspacePreparer>();
            services.AddSingleton<IAgentTaskLaunchSink>(Sink);
            services.AddScoped<AgentTaskService>();
            services.AddScoped<AgentTaskDispatcher>();
            _configure?.Invoke(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var scope in _scopes)
                scope.Dispose();
            await Provider.DisposeAsync();
            await Peer.DisposeAsync();
            await Host.DisposeAsync();
            await Schema.DisposeAsync();
            try { Directory.Delete(WorkspacePath, recursive: true); } catch (IOException) { }
        }
    }

    internal sealed class RecordingLaunchSink : IAgentTaskLaunchSink
    {
        public List<AgentLaunchSpec> Specs { get; } = [];
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Specs.Add(spec);
    }

    /// <summary>
    /// CARD-0672: a lease a test holds on behalf of a running land. While held, every acquire is
    /// refused and the owner reads as a Known <c>land</c> holder, as the real lease reports one.
    /// </summary>
    internal sealed class HoldableLease : IRepositoryMutationLease
    {
        public bool Held { get; set; }
        public Guid HolderTaskId { get; } = Guid.NewGuid();
        public DateTimeOffset AcquiredAt { get; } = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);

        public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
            Task.FromResult(Held ? null : (RepositoryLease?)new TrivialLease(repository));

        public bool Owns(RepositoryLease lease, string commonDirectory) => lease is TrivialLease;

        public Task<RepositoryLeaseOwner?> FindOwnerAsync(string repository, CancellationToken ct) =>
            Task.FromResult<RepositoryLeaseOwner?>(Held
                ? new RepositoryLeaseOwner(RepositoryLeaseOwnerState.Known, HolderTaskId, RepositoryLeasePurposes.Land,
                    Guid.NewGuid(), AcquiredAt)
                : RepositoryLeaseOwner.Unknown);

        private sealed class TrivialLease(string repository) : RepositoryLease
        {
            public override string CommonDirectory { get; } = Path.GetFullPath(repository);
            public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class PushOnlyGit : ILandingGit
    {
        private readonly object _sync = new();
        private int _inPush;
        private int _maxConcurrent;
        private int _pushOrdinal;

        public TaskCompletionSource? HoldFirstPush { get; set; }
        public TaskCompletionSource? HoldSecondPush { get; set; }
        public int MaxConcurrentPushes { get { lock (_sync) return _maxConcurrent; } }
        public List<RecordedPush> Pushes { get; } = [];

        public sealed record RecordedPush(IReadOnlyList<string> Arguments, LandingGitRunOptions Options);

        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            if (arguments[0] == "push")
                throw new NotSupportedException("push must carry run options");
            return Task.FromResult(arguments[0] switch
            {
                "rev-parse" => new LandingGitResult(0, new string('1', 40), ""),
                "remote" => new LandingGitResult(0, "https://github.com/michal-ciechan/Antiphon.git", ""),
                _ => throw new NotSupportedException(arguments[0]),
            });
        }

        public async Task<LandingGitResult> RunAsync(
            string repository, IReadOnlyList<string> arguments, LandingGitRunOptions options, CancellationToken ct)
        {
            if (arguments[0] != "push")
                return await RunAsync(repository, arguments, ct);

            int ordinal;
            lock (_sync)
            {
                Pushes.Add(new RecordedPush(arguments.ToArray(), options));
                _inPush++;
                if (_inPush > _maxConcurrent)
                    _maxConcurrent = _inPush;
                ordinal = _pushOrdinal++;
            }

            try
            {
                if (HoldFirstPush is not null && ordinal == 0)
                    await HoldFirstPush.Task.WaitAsync(ct);
                if (HoldSecondPush is not null && ordinal == 1)
                    await HoldSecondPush.Task.WaitAsync(ct);
            }
            finally
            {
                lock (_sync)
                    _inPush--;
            }

            return new LandingGitResult(0, "", "");
        }
        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) =>
            Task.FromResult(Path.GetFullPath(repository));
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) => throw new NotSupportedException();
    }
}
