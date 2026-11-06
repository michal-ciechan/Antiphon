using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1717 D-1. <c>WorktreeLegacyRelease:Enabled</c> and <c>Execute</c> are read at the destructive
/// step. A candidate already inside its pre-check, a system release whose command already started, a
/// residue-sweep retry (including a Partial tree), and the scheduled policy tick all keep the bytes when
/// the switch is off. A caller-filed release is not gated by that switch. The retry cursor's
/// <c>NotBefore</c> is the policy pace: the sweep does not jump it.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Timeout(180_000)]
public sealed class C0824DesktopLegacyReleaseSwitchTests
{
    /// <summary>
    /// (a) The pre-check status has already returned. Turning the switch off still files and deletes on
    /// the unfixed executor because the tick captured Execute before the git call.
    /// </summary>
    [Test]
    [Arguments("enabled", "tick_stopped_disabled")]
    [Arguments("execute", "tick_stopped_execute_off")]
    public async Task Switch_off_after_precheck_does_not_file_or_delete(string which, string reason)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var before = await w.SnapshotAsync(a.Path);
        var origin = await w.OriginRefsAsync();
        var flipped = false;
        var previous = w.Fixture.Git.AfterCommand;
        w.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (previous is not null) await previous(directory, args, result);
            if (flipped || DesktopLegacyWorld.Subcommand(args) != "status" || !LandingGit.PathsEqual(directory, a.Path))
                return;
            flipped = true;
            Apply(w, which);
        };

        var tick = await w.TickAsync();

        flipped.ShouldBeTrue("the pre-check status ran");
        tick.Stopped.ShouldBe(reason);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        row.ReasonCode.ShouldBe(reason);
        (await w.ReleasesAsync(a)).ShouldBeEmpty("no system release was filed");
        await w.AssertIntactAsync(a, before);

        Apply(w, "on");
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
    }

    /// <summary>Filing itself reads the switch. A direct system release cannot insert a row while it is off.</summary>
    [Test]
    [Arguments("enabled", TaskWorktreeRetirementService.LegacyReleaseDisabled)]
    [Arguments("execute", TaskWorktreeRetirementService.LegacyReleaseExecuteOff)]
    public async Task System_filing_refuses_when_the_switch_is_already_off(string which, string reason)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var before = await w.SnapshotAsync(a.Path);
        Apply(w, which);
        await using var scope = w.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == a.Id);
        var head = (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim();

        var ex = await Should.ThrowAsync<ConflictException>(() => service.ReleaseSystemAsync(a.Id,
            new SystemReleaseRequest(task.ConcurrencyToken, head, DesktopLegacyReleasePolicyService.ReleaseReason),
            CancellationToken.None));

        ex.Code.ShouldBe(reason);
        (await w.ReleasesAsync(a)).ShouldBeEmpty();
        await w.AssertIntactAsync(a, before);
    }

    /// <summary>
    /// (b) The command intent is already stored. A later TryRetire, which is the retry the policy tick and a
    /// restarted host both use, reads the switch again and does not delete.
    /// </summary>
    [Test]
    [Arguments("enabled", TaskWorktreeRetirementService.LegacyReleaseDisabled)]
    [Arguments("execute", TaskWorktreeRetirementService.LegacyReleaseExecuteOff)]
    [Arguments("both", TaskWorktreeRetirementService.LegacyReleaseDisabled)]
    public async Task Started_command_retry_honours_the_switch(string which, string reason)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await StartCommandAsync(w);
        var before = await w.SnapshotAsync(a.Path);
        w.Clock.Advance(TimeSpan.FromMinutes(61));
        Apply(w, which);

        await using var scope = w.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var release = await db.TaskWorktreeRetirements.SingleAsync(r => r.TaskId == a.Id);
        var removed = await scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>()
            .TryRetireAsync(release, null, CancellationToken.None);

        removed.Residue.ShouldBe(reason);
        removed.IsClean.ShouldBeFalse();
        removed.DirectoryGone.ShouldBeFalse();
        await AssertBytesAsync(w, a, before);
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
    }

    /// <summary>(d) The scheduled policy job is a resume. Disabled, it does not finish a started command.</summary>
    [Test]
    public async Task Disabled_policy_job_does_not_resume_a_started_command()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await StartCommandAsync(w);
        var before = await w.SnapshotAsync(a.Path);
        w.Clock.Advance(TimeSpan.FromMinutes(61));
        Apply(w, "both");
        await using var scope = w.Host.Services.CreateAsyncScope();
        var job = new DesktopLegacyReleasePolicyJob(
            scope.ServiceProvider.GetRequiredService<DesktopLegacyReleasePolicyService>(),
            NullLogger<DesktopLegacyReleasePolicyJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        await AssertBytesAsync(w, a, before);
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
    }

    /// <summary>
    /// (c) A refused system release, backoff already elapsed, is still deleted by the residue sweep unless the
    /// legacy switch is read inside the retirement executor.
    /// </summary>
    [Test]
    [Arguments("enabled", TaskWorktreeRetirementService.LegacyReleaseDisabled)]
    [Arguments("execute", TaskWorktreeRetirementService.LegacyReleaseExecuteOff)]
    [Arguments("both", TaskWorktreeRetirementService.LegacyReleaseDisabled)]
    public async Task Residue_sweep_leaves_a_refused_system_release_when_the_switch_is_off(string which, string reason)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await RefuseUnregisterAsync(w);
        var before = await w.SnapshotAsync(a.Path);
        w.Clock.Advance(TimeSpan.FromMinutes(61));
        Apply(w, which);

        var swept = await SweepAsync(w, a);

        swept.ReasonCode.ShouldBe(reason);
        swept.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
        await AssertBytesAsync(w, a, before);
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
    }

    /// <summary>
    /// (c) Partial: unregister succeeded and the set-aside still holds the only copy. The sweep must not finish
    /// that delete while the legacy switch is off.
    /// </summary>
    [Test]
    public async Task Residue_sweep_leaves_partial_system_bytes_when_the_switch_is_off()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var aside = WorktreeSetAside.SetAsidePath(a.Path);
        var marker = Path.Combine(aside, "pending-only.txt");
        var armed = false;
        FileStream? windowsHold = null;
        w.Fixture.Git.AfterUnregister = _ =>
        {
            if (armed) return Task.CompletedTask;
            armed = true;
            File.WriteAllText(marker, "only-copy-pending");
            if (OperatingSystem.IsWindows())
                windowsHold = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.None);
            else
                File.SetUnixFileMode(aside, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            return Task.CompletedTask;
        };
        DesktopLegacyReleasePolicyService.TickResult tick;
        try
        {
            tick = await w.TickAsync();
        }
        finally
        {
            w.Fixture.Git.AfterUnregister = null;
            windowsHold?.Dispose();
            if (!OperatingSystem.IsWindows() && Directory.Exists(aside))
                File.SetUnixFileMode(aside, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        armed.ShouldBeTrue("unregister completed and the set-aside was armed");
        (await w.RowAsync(tick.RunId, a)).Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Partial));
        (await w.ReleasesAsync(a)).Single().State.ShouldBe(WorktreeRetirementState.Partial);
        File.ReadAllText(marker).ShouldBe("only-copy-pending");
        w.Clock.Advance(TimeSpan.FromMinutes(61));
        Apply(w, "both");

        var swept = await SweepAsync(w, a);

        swept.ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleaseDisabled);
        swept.Outcome.ShouldNotBe(nameof(WorktreeResidueCandidateOutcome.Removed));
        File.ReadAllText(marker).ShouldBe("only-copy-pending");
        (await w.ReleasesAsync(a)).Single().State.ShouldBe(WorktreeRetirementState.Partial);
    }

    /// <summary>Policy pace: while the legacy retry cursor is still in the future, the sweep does not delete.</summary>
    [Test]
    public async Task Residue_sweep_does_not_jump_a_pending_legacy_backoff()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await RefuseUnregisterAsync(w);
        var before = await w.SnapshotAsync(a.Path);
        var cursor = await w.RetryCursorAsync(a);
        cursor.ShouldNotBeNull();
        cursor!.NotBefore.ShouldNotBeNull();
        cursor.NotBefore!.Value.ShouldBeGreaterThan(w.Now);

        var swept = await SweepAsync(w, a);

        swept.ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleasePaced);
        await AssertBytesAsync(w, a, before);
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
    }

    /// <summary>Once that backoff has elapsed and the switch is still on, the sweep may finish the system release.</summary>
    [Test]
    public async Task Due_residue_sweep_still_finishes_a_system_release()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await RefuseUnregisterAsync(w);
        var origin = await w.OriginRefsAsync();
        w.Clock.Advance(TimeSpan.FromMinutes(61));

        var swept = await SweepAsync(w, a);

        swept.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Removed));
        await w.AssertRetiredAsync(a, origin);
    }

    /// <summary>The legacy switch does not apply to a release someone else filed.</summary>
    [Test]
    public async Task Residue_sweep_still_removes_a_caller_filed_release_when_legacy_is_off()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(shape: "base-only", ageHours: 60);
        var origin = await w.OriginRefsAsync();
        await using (var scope = w.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == a.Id);
            var head = (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim();
            var dto = await service.ReleaseAsync(a.Id, new ReleaseWorktreeRetirementRequest(
                task.ConcurrencyToken, head, null, true, "reviewed no further use"),
                new AgentTaskService.Caller(null, null, ""), CancellationToken.None);
            (await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == dto.Id)).CallerIdentity.ShouldBe("operator");
        }
        Apply(w, "both");

        var swept = await SweepAsync(w, a);

        swept.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Removed));
        swept.ReasonCode.ShouldBe("removed");
        Directory.Exists(a.Path).ShouldBeFalse("the caller-filed tree is gone");
        (await w.RegisteredAsync(a.Path)).ShouldBeFalse("the registration is gone");
        (await w.BranchExistsAsync(a)).ShouldBeFalse("the local branch is gone");
        (await w.OriginRefsAsync()).ShouldBe(origin, "origin is never changed");
        (await w.ReleasesAsync(a)).Single().CallerIdentity.ShouldBe("operator");
    }

    /// <summary>
    /// Review 5242d604. The command intent has already committed. Disabling either switch at that
    /// await must keep the tree, registration and local branch. A later tick with the switch on finishes.
    /// </summary>
    [Test]
    [Arguments("enabled", TaskWorktreeRetirementService.LegacyReleaseDisabled)]
    [Arguments("execute", TaskWorktreeRetirementService.LegacyReleaseExecuteOff)]
    public async Task Late_switch_after_intent_preserves_the_tree(string which, string reason)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var before = await w.SnapshotAsync(a.Path);
        var origin = await w.OriginRefsAsync();
        var fired = false;
        w.Host.RetirementCut.Cut = "intent-after";
        w.Host.RetirementCut.Pause = () =>
        {
            if (fired) return Task.CompletedTask;
            fired = true;
            w.Host.RetirementCut.Cut = null;
            Apply(w, which);
            return Task.CompletedTask;
        };

        var tick = await w.TickAsync();

        fired.ShouldBeTrue("the intent commit is the gate");
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        row.ReasonCode.ShouldBe(reason);
        await AssertBytesAsync(w, a, before);
        (await w.OriginRefsAsync()).ShouldBe(origin);
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
        (await w.AttemptsAsync(a)).ShouldContain(x => x.CommandIntentId != null, "the committed intent stays for resume");

        Apply(w, "on");
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
    }

    /// <summary>
    /// Review 5242d604. The fourth status on the tree is the guarded remover's final inspection.
    /// Disabling Execute there, after that await returns, must not move or delete.
    /// </summary>
    [Test]
    public async Task Late_switch_at_final_inspection_preserves_the_tree()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var before = await w.SnapshotAsync(a.Path);
        var origin = await w.OriginRefsAsync();
        var statuses = 0;
        var fired = false;
        var previous = w.Fixture.Git.AfterCommand;
        w.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (previous is not null) await previous(directory, args, result);
            if (fired || DesktopLegacyWorld.Subcommand(args) != "status" || !LandingGit.PathsEqual(directory, a.Path))
                return;
            if (++statuses != 4) return;
            result.ExitCode.ShouldBe(0);
            fired = true;
            Apply(w, "execute");
        };

        var tick = await w.TickAsync();

        fired.ShouldBeTrue($"the fourth status is the final inspection (saw {statuses})");
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        row.ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleaseExecuteOff);
        await AssertBytesAsync(w, a, before);
        (await w.OriginRefsAsync()).ShouldBe(origin);
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);

        w.Fixture.Git.AfterCommand = previous;
        Apply(w, "on");
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
    }

    /// <summary>
    /// Review 5242d604. Unregister has finished and the only bytes are the set-aside. Disabling Execute
    /// at that await must not delete the set-aside or the local branch. A later tick finishes both.
    /// </summary>
    [Test]
    public async Task Late_switch_after_unregister_preserves_set_aside()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var origin = await w.OriginRefsAsync();
        var aside = WorktreeSetAside.SetAsidePath(a.Path);
        var marker = Path.Combine(aside, a.Id8 + ".txt");
        var fired = false;
        w.Fixture.Git.AfterUnregister = _ =>
        {
            if (fired) return Task.CompletedTask;
            fired = true;
            Apply(w, "execute");
            return Task.CompletedTask;
        };

        var tick = await w.TickAsync();

        fired.ShouldBeTrue("unregister completed");
        Directory.Exists(aside).ShouldBeTrue("the set-aside still holds the tree");
        File.ReadAllText(marker).ShouldBe(a.Id8 + ".txt\n");
        (await w.BranchExistsAsync(a)).ShouldBeTrue("the local branch is intact");
        (await w.OriginRefsAsync()).ShouldBe(origin);
        var release = (await w.ReleasesAsync(a)).Single();
        release.State.ShouldNotBe(WorktreeRetirementState.Complete);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        row.ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleaseExecuteOff);

        w.Fixture.Git.AfterUnregister = null;
        Apply(w, "on");
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
        Directory.Exists(aside).ShouldBeFalse("the resumed pass deleted the set-aside");
    }

    /// <summary>
    /// Review 5242d604. Execute is turned off inside the release-row save, after filing admission.
    /// The insert must roll back: no system row, and the tree stays.
    /// </summary>
    [Test]
    public async Task Late_switch_during_release_insert_inserts_nothing()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var before = await w.SnapshotAsync(a.Path);
        var origin = await w.OriginRefsAsync();
        var fired = false;
        w.Host.RetirementCut.Cut = "release-before";
        w.Host.RetirementCut.Pause = () =>
        {
            if (fired) return Task.CompletedTask;
            fired = true;
            w.Host.RetirementCut.Cut = null;
            Apply(w, "execute");
            return Task.CompletedTask;
        };

        var tick = await w.TickAsync();

        fired.ShouldBeTrue("the release insert is the gate");
        (await w.ReleasesAsync(a)).ShouldBeEmpty("a disabled switch inserts no system release");
        await w.AssertIntactAsync(a, before);
        (await w.OriginRefsAsync()).ShouldBe(origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleaseExecuteOff);
    }

    /// <summary>Settings that cannot be read fail closed: the sweep does not delete a system release.</summary>
    [Test]
    public async Task Residue_sweep_refuses_a_system_release_when_legacy_settings_cannot_be_read()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await RefuseUnregisterAsync(w);
        var before = await w.SnapshotAsync(a.Path);
        w.Clock.Advance(TimeSpan.FromMinutes(61));
        w.Settings.Fault = new OptionsValidationException(
            WorktreeLegacyReleaseSettings.SectionName,
            typeof(WorktreeLegacyReleaseSettings),
            ["WorktreeLegacyRelease:Enabled could not be bound."]);

        var swept = await SweepAsync(w, a);

        swept.ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleaseDisabled);
        swept.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
        await AssertBytesAsync(w, a, before);
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
    }

    /// <summary>
    /// CARD-1717 B-B-1 (Review 11a6d8bb). A genuine system release reaches the remover without its live gate, after
    /// Execute was turned off. Absence is not permission: the tree, registration and local branch stay.
    /// </summary>
    [Test]
    public async Task Missing_live_gate_refuses_a_system_release()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var before = await w.SnapshotAsync(a.Path);
        var head = (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim();
        await using var scope = w.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == a.Id);
        await scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>().ReleaseSystemAsync(a.Id,
            new SystemReleaseRequest(task.ConcurrencyToken, head, DesktopLegacyReleasePolicyService.ReleaseReason), CancellationToken.None);
        var r = await db.TaskWorktreeRetirements.AsNoTracking().SingleAsync(x => x.TaskId == a.Id);
        r.CallerIdentity.ShouldBe(TaskWorktreeRetirementService.SystemCallerIdentity);
        Apply(w, "execute");
        await using var lease = await scope.ServiceProvider.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(r.RepositoryPath, CancellationToken.None);
        lease.ShouldNotBeNull();
        var request = new WorktreeRemovalRequest(WorktreeRemovalPurpose.SettledTask,
            new LandSourceCoordinates(r.TaskId, r.RepositoryPath, r.WorktreePath, r.SourceFullRef, r.TargetFullRef),
            r.CommonDirectory, r.GitDirectory, r.SourceSha, r.ObservedTargetSha ?? new string('0', 40), null, lease,
            ManagedRoot: w.Root, RetirementId: r.Id, HasDeletionIntent: true);
        request.LiveAuthorization.ShouldBeNull();

        var result = await scope.ServiceProvider.GetRequiredService<GuardedWorktreeRemoval>().RemoveAsync(request, CancellationToken.None);

        result.Residue.ShouldBe("legacy_release_authorization_required");
        result.DirectoryGone.ShouldBeFalse();
        result.Unregistered.ShouldBeFalse();
        result.BranchDeleted.ShouldBeFalse();
        await AssertBytesAsync(w, a, before);
    }

    /// <summary>
    /// CARD-1717 B-B-1 (Review 11a6d8bb). The mirror continuation re-read the switch before two more awaits (the task
    /// read and the runner description). Execute turned off after the description must send no WorkspaceRemove and
    /// keep the mirror path.
    /// </summary>
    [Test]
    public async Task Late_switch_after_runner_describe_sends_no_mirror_remove()
    {
        var proxy = DispatchProxy.Create<ISessionRunnerDirectory, DescribeProbe>();
        var control = (DescribeProbe)(object)proxy;
        await using var w = await DesktopLegacyWorld.CreateAsync(configure: services => services.AddSingleton(sp =>
            new RemoteWorkspaceService(proxy, sp.GetRequiredService<ILandingGit>(), NullLogger<RemoteWorkspaceService>.Instance)));
        await using var phone = await PhoneHomeTestHost.StartAsync(w.Clock, w.Host.Schema.ConnectionString);
        control.Inner = phone.Directory;
        var fired = 0;
        var removes = 0;
        var removesAfterOff = 0;
        control.After = () =>
        {
            if (fired++ != 0) return;
            w.Settings.Value.Execute.ShouldBeTrue("the switch is on until the description returns");
            Apply(w, "execute");
        };
        var peer = await phone.ConnectPeerAsync();
        phone.Directory.MarkRecovered(await phone.WaitLiveAsync());
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.WorkspaceRemove) return null;
            removes++;
            if (!w.Settings.Value.Execute) removesAfterOff++;
            return new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceRemoveResponse(true, null), PhoneHomeFraming.Json));
        };
        var mirror = Path.Combine(w.Fixture.Root, "c1717-mirror");
        var a = await w.AddTaskAsync(seed: t => { t.RunnerId = phone.AllowedRunnerId; t.RemoteWorktreePath = mirror; });

        await w.TickAsync();

        fired.ShouldBe(1, "the mirror continuation described the runner once");
        removesAfterOff.ShouldBe(0, "no WorkspaceRemove after the switch closed");
        removes.ShouldBe(0);
        await using var db = w.Db();
        var saved = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == a.Id);
        saved.RemoteWorktreePath.ShouldBe(mirror, "the mirror path is kept for a later pass");
    }

    /// <summary>
    /// CARD-0824 S4a round 3 Review B-1. Unregister reads the common directory and the entry's <c>gitdir</c> (awaits)
    /// before it moves Git's administrative entry. Execute turned off after that read must put the tree back
    /// registered, not drop the registration.
    /// </summary>
    [Test]
    public async Task Late_switch_inside_unregister_keeps_the_registration()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var before = await w.SnapshotAsync(a.Path);
        var origin = await w.OriginRefsAsync();
        var aside = WorktreeSetAside.SetAsidePath(a.Path);
        var armed = false;
        var fired = 0;
        var previous = w.Fixture.Git.AfterCommand;
        w.Fixture.Git.BeforeUnregister = _ => { armed = true; return Task.CompletedTask; };
        w.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (previous is not null) await previous(directory, args, result);
            if (!armed || fired != 0 || !args.Contains("--git-common-dir")) return;
            result.Succeeded.ShouldBeTrue("the unregister's own common-directory read returned");
            w.Settings.Value.Execute.ShouldBeTrue("the switch is on until the inner read returns");
            fired++;
            Apply(w, "execute");
        };

        var tick = await w.TickAsync();

        fired.ShouldBe(1, "the unregister's common-directory read was reached once");
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        row.ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleaseExecuteOff);
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue("Git's entry was never dropped");
        Directory.Exists(aside).ShouldBeFalse("the tree was put back");
        await AssertBytesAsync(w, a, before);
        w.Fixture.Git.RegistrationDrops.ShouldBeEmpty();
        (await w.OriginRefsAsync()).ShouldBe(origin);

        w.Fixture.Git.BeforeUnregister = null;
        w.Fixture.Git.AfterCommand = previous;
        Apply(w, "on");
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
    }

    /// <summary>
    /// CARD-0824 S4a round 3 Review B-1. The local branch delete writes its child journal (awaits) before the git
    /// child starts. Execute turned off after the journal is flushed must keep the branch.
    /// </summary>
    [Test]
    public async Task Late_switch_before_branch_delete_start_keeps_the_branch()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var origin = await w.OriginRefsAsync();
        var armed = false;
        var fired = 0;
        var previousCommand = w.Fixture.Git.BeforeCommand;
        var previousHook = RepositoryChildJournal.BeforeReplaceForTests;
        w.Fixture.Git.BeforeCommand = async (directory, args) =>
        {
            if (args.Count > 0 && args[0] == "update-ref" && args.Contains("-d") && args.Contains(a.FullRef)) armed = true;
            return previousCommand is null ? null : await previousCommand(directory, args);
        };
        RepositoryChildJournal.BeforeReplaceForTests = async temporary =>
        {
            if (previousHook is not null) await previousHook(temporary);
            // The hook is process-wide: only this world's repository journal counts, and only its first write.
            if (!armed || !temporary.StartsWith(w.Fixture.Root, StringComparison.Ordinal)
                || Interlocked.CompareExchange(ref fired, 1, 0) != 0) return;
            File.Exists(temporary).ShouldBeTrue("the journal is flushed before the child starts");
            w.Settings.Value.Execute.ShouldBeTrue("the switch is on until the journal await");
            Apply(w, "execute");
        };
        DesktopLegacyReleasePolicyService.TickResult tick;
        try { tick = await w.TickAsync(); }
        finally { RepositoryChildJournal.BeforeReplaceForTests = previousHook; }

        fired.ShouldBe(1, "the branch delete's journal was written once");
        (await w.BranchExistsAsync(a)).ShouldBeTrue("the local branch is intact");
        (await w.OriginRefsAsync()).ShouldBe(origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleaseExecuteOff);
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);

        w.Fixture.Git.BeforeCommand = previousCommand;
        Apply(w, "on");
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
    }

    /// <summary>
    /// CARD-0824 S4a round 3 Review B-2. A WorkspaceRemove already queued behind the connection's send gate was
    /// authorized before it waited. Execute turned off while it waits must send nothing and keep the mirror path.
    /// </summary>
    [Test]
    public async Task Late_switch_while_mirror_remove_waits_to_send_keeps_the_mirror()
    {
        var proxy = DispatchProxy.Create<ISessionRunnerDirectory, DescribeProbe>();
        var control = (DescribeProbe)(object)proxy;
        await using var w = await DesktopLegacyWorld.CreateAsync(configure: services => services.AddSingleton(sp =>
            new RemoteWorkspaceService(proxy, sp.GetRequiredService<ILandingGit>(), NullLogger<RemoteWorkspaceService>.Instance)));
        await using var phone = await PhoneHomeTestHost.StartAsync(w.Clock, w.Host.Schema.ConnectionString);
        control.Inner = phone.Directory;
        var peer = await phone.ConnectPeerAsync();
        var live = await phone.WaitLiveAsync();
        phone.Directory.MarkRecovered(live);
        var send = (SemaphoreSlim)typeof(PhoneHomeLiveConnection)
            .GetField("_send", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(live)!;
        var waiters = (System.Collections.IDictionary)typeof(PhoneHomeLiveConnection)
            .GetField("_waiters", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(live)!;
        var held = false;
        var described = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        control.After = () =>
        {
            if (held) return;
            w.Settings.Value.Execute.ShouldBeTrue("the switch is on at the description");
            send.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the test holds the send gate");
            held = true;
            described.TrySetResult();
        };
        var removes = 0;
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.WorkspaceRemove) return null;
            removes++;
            return new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceRemoveResponse(true, null), PhoneHomeFraming.Json));
        };
        var mirror = Path.Combine(w.Fixture.Root, "c0824-queued-mirror");
        var a = await w.AddTaskAsync(seed: t => { t.RunnerId = phone.AllowedRunnerId; t.RemoteWorktreePath = mirror; });

        var pending = w.TickAsync();
        try
        {
            await described.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var queued = false;
            for (var until = DateTime.UtcNow.AddSeconds(30); !queued && DateTime.UtcNow < until;)
            {
                foreach (var waiter in waiters.Values.Cast<object>().ToList())
                    if ((PhoneHomeOperation)waiter.GetType().GetProperty("Operation")!.GetValue(waiter)! == PhoneHomeOperation.WorkspaceRemove)
                        queued = true;
                if (!queued) await Task.Delay(10);
            }
            queued.ShouldBeTrue("the WorkspaceRemove is queued behind the held send gate");
            Apply(w, "execute");
        }
        finally
        {
            if (held) { held = false; send.Release(); }
        }
        await pending.WaitAsync(TimeSpan.FromSeconds(90));

        removes.ShouldBe(0, "no WorkspaceRemove left after the switch closed");
        await using var db = w.Db();
        var saved = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == a.Id);
        saved.RemoteWorktreePath.ShouldBe(mirror, "the mirror path is kept for a later pass");
    }

    /// <summary>
    /// CARD-0824 S4a integration Review B-1 (d8af9f97). A RefOnly task's system release reaches the S5b-1 no-checkout
    /// mirror path with the switch on. Execute turned off after that admission, at the runner description and while
    /// the WorkspaceRemove waits behind the send gate, must send nothing and keep the mirror path; the same release
    /// with the switch on sends exactly one remove.
    /// </summary>
    [Test]
    public async Task Late_switch_on_ref_only_mirror_continuation_sends_no_remove()
    {
        var proxy = DispatchProxy.Create<ISessionRunnerDirectory, DescribeProbe>();
        var control = (DescribeProbe)(object)proxy;
        await using var w = await DesktopLegacyWorld.CreateAsync(configure: services => services.AddSingleton(sp =>
            new RemoteWorkspaceService(proxy, sp.GetRequiredService<ILandingGit>(), NullLogger<RemoteWorkspaceService>.Instance)));
        await using var phone = await PhoneHomeTestHost.StartAsync(w.Clock, w.Host.Schema.ConnectionString);
        control.Inner = phone.Directory;
        var peer = await phone.ConnectPeerAsync();
        var live = await phone.WaitLiveAsync();
        phone.Directory.MarkRecovered(live);
        var send = (SemaphoreSlim)typeof(PhoneHomeLiveConnection)
            .GetField("_send", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(live)!;
        var waiters = (System.Collections.IDictionary)typeof(PhoneHomeLiveConnection)
            .GetField("_waiters", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(live)!;
        var removes = 0;
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.WorkspaceRemove) return null;
            removes++;
            return new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceRemoveResponse(true, null), PhoneHomeFraming.Json));
        };
        var mirror = Path.Combine(w.Fixture.Root, "c0824-ref-only-mirror");
        var a = await w.AddTaskAsync(seed: t => { t.RunnerId = phone.AllowedRunnerId; t.RemoteWorktreePath = mirror; });
        var sha = (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim();
        var bound = await C0824RefOnlyCleanupTests.BindAsync(w.Host, a.Id, removeAuthoring: true);
        Guid releaseId;
        await using (var db = w.Db())
        {
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == a.Id);
            task.DesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
            var release = new TaskWorktreeRetirement
            {
                Id = Guid.NewGuid(), TaskId = task.Id, TaskAttempt = task.Attempt, TerminalStatus = task.Status,
                TaskCompletedAt = task.CompletedAt!.Value, ReportDigest = CardDoneArtifactPreservation.Digest(task.Result),
                ReleasedTaskRevision = task.ConcurrencyToken, CallerIdentity = TaskWorktreeRetirementService.SystemCallerIdentity,
                ReleaseReason = "fixture ref-only mirror release", ReleasedAt = w.Now, HandoffDispositionJson = "[]",
                Active = true, RepositoryPath = task.RepoPath!, CommonDirectory = bound.Common, WorktreePath = bound.Identity,
                GitDirectory = "", SourceFullRef = bound.FullRef, SourceSha = sha, TargetFullRef = "refs/heads/master",
                State = WorktreeRetirementState.Released, UpdatedAt = w.Now,
            };
            db.TaskWorktreeRetirements.Add(release);
            await db.SaveChangesAsync();
            releaseId = release.Id;
        }

        async Task<WorktreeRemoval> RetireAsync()
        {
            await using var scope = w.Host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var release = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == releaseId);
            var result = await scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>()
                .TryRetireAsync(release, null, default);
            result.DesktopCleanupDisposition.ShouldBe(DesktopCleanupDisposition.NotRequired, "the RefOnly no-checkout branch");
            result.TaskRefWorkspaceBindingId.ShouldBe(bound.Id);
            return result;
        }
        async Task AssertKeptAsync(string cut)
        {
            removes.ShouldBe(0, cut + ": no WorkspaceRemove after the switch closed");
            await using var db = w.Db();
            var saved = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == a.Id);
            saved.RemoteWorktreePath.ShouldBe(mirror, cut + ": the mirror path is kept for a later pass");
            saved.RemoteWorktreeResidue.ShouldNotBeNull(cut).ShouldContain(TaskWorktreeRetirementService.LegacyReleaseExecuteOff);
            saved.TaskRefWorkspaceBindingId.ShouldBe(bound.Id, cut);
            (await db.TaskWorktreeRetirementAttempts.CountAsync(x => x.RetirementId == releaseId)).ShouldBe(0, cut + ": no desktop attempt");
            (await File.ReadAllTextAsync(bound.Sentinel)).ShouldBe(bound.SentinelBytes, cut);
            foreach (var (repository, reference) in new[] { (w.Fixture.Repository, bound.FullRef), (w.Fixture.Repository, bound.Pin), (w.Fixture.Remote, bound.FullRef) })
                (await w.ReadAsync(repository, "rev-parse", "--verify", reference)).Trim().ShouldBe(sha, cut + ": source ref retained");
        }

        // (a) Admission passed (the description is reached only after it); Execute closes once the description returns.
        var described = 0;
        control.After = () =>
        {
            if (described++ != 0) return;
            w.Settings.Value.Execute.ShouldBeTrue("the switch is on until the description returns");
            Apply(w, "execute");
        };
        await RetireAsync();
        described.ShouldBe(1, "the RefOnly mirror continuation described the runner once");
        await AssertKeptAsync("describe");

        // (b) The remove was authorized before it queued behind the held send gate; Execute closes while it waits.
        Apply(w, "on");
        var held = false;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        control.After = () =>
        {
            if (held) return;
            w.Settings.Value.Execute.ShouldBeTrue("the switch is on at the description");
            send.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue("the test holds the send gate");
            held = true;
            reached.TrySetResult();
        };
        var pending = RetireAsync();
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var queued = false;
            for (var until = DateTime.UtcNow.AddSeconds(30); !queued && DateTime.UtcNow < until;)
            {
                foreach (var waiter in waiters.Values.Cast<object>().ToList())
                    if ((PhoneHomeOperation)waiter.GetType().GetProperty("Operation")!.GetValue(waiter)! == PhoneHomeOperation.WorkspaceRemove)
                        queued = true;
                if (!queued) await Task.Delay(10);
            }
            queued.ShouldBeTrue("the WorkspaceRemove is queued behind the held send gate");
            Apply(w, "execute");
        }
        finally
        {
            if (held) send.Release();
        }
        await pending.WaitAsync(TimeSpan.FromSeconds(90));
        await AssertKeptAsync("queued-send");

        // Control: the same release with the switch on throughout sends exactly one remove.
        control.After = () => { };
        Apply(w, "on");
        await RetireAsync();
        removes.ShouldBe(1, "the RefOnly mirror continuation sends its remove while the switch is on");
        await using (var db = w.Db())
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == a.Id)).RemoteWorktreePath.ShouldBeNull("the mirror is retired");
    }

    /// <summary>
    /// CARD-1717 A+B integration. The content binding (A) is the last await before the move-aside. Execute turned off
    /// while it reads the index must still keep the tree: the switch is read after the binding, not before it.
    /// </summary>
    [Test]
    public async Task Late_switch_during_content_binding_preserves_the_tree()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var before = await w.SnapshotAsync(a.Path);
        var origin = await w.OriginRefsAsync();
        var fired = false;
        var previous = w.Fixture.Git.AfterCommand;
        w.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (previous is not null) await previous(directory, args, result);
            if (fired || !IsStageListing(args) || !LandingGit.PathsEqual(directory, a.Path)) return;
            fired = true;
            Apply(w, "execute");
        };

        var tick = await w.TickAsync();

        fired.ShouldBeTrue("the binding read the index of the tree");
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        row.ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleaseExecuteOff);
        await AssertBytesAsync(w, a, before);
        var aside = WorktreeSetAside.SetAsidePath(a.Path);
        Directory.Exists(aside).ShouldBeFalse("nothing was moved aside");
        // A later re-read would put a moved tree back; no command may have run in a set-aside at all.
        w.Fixture.Git.Commands.ShouldNotContain(c => LandingGit.PathsEqual(c.Directory, aside), "the tree never moved");
        (await w.OriginRefsAsync()).ShouldBe(origin);

        w.Fixture.Git.AfterCommand = previous;
        Apply(w, "on");
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
    }

    /// <summary>
    /// CARD-1717 A+B integration. After the move-aside the census re-reads the set-aside tree (awaits) before Git's
    /// entry is dropped. Execute turned off there must put the tree back registered, not unregister and hold it.
    /// </summary>
    [Test]
    public async Task Late_switch_after_move_aside_restores_the_registered_tree()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var before = await w.SnapshotAsync(a.Path);
        var origin = await w.OriginRefsAsync();
        var aside = WorktreeSetAside.SetAsidePath(a.Path);
        var fired = false;
        var previous = w.Fixture.Git.AfterCommand;
        w.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (previous is not null) await previous(directory, args, result);
            if (fired || !IsStageListing(args) || !LandingGit.PathsEqual(directory, aside)) return;
            fired = true;
            Apply(w, "execute");
        };

        var tick = await w.TickAsync();

        fired.ShouldBeTrue("the post-move census read the set-aside index");
        var row = await w.RowAsync(tick.RunId, a);
        row.ReasonCode.ShouldBe(TaskWorktreeRetirementService.LegacyReleaseExecuteOff);
        Directory.Exists(aside).ShouldBeFalse("the tree was put back");
        await AssertBytesAsync(w, a, before);
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue("Git's entry was never dropped");
        (await w.OriginRefsAsync()).ShouldBe(origin);

        w.Fixture.Git.AfterCommand = previous;
        Apply(w, "on");
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
    }

    /// <summary>
    /// CARD-1717 A+B integration. With the switch on throughout, the content seal still decides: a tracked write
    /// after the final clean status is refused and its bytes stay.
    /// </summary>
    [Test]
    public async Task Content_seal_refuses_a_late_tracked_write_with_the_switch_on()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var tracked = Path.Combine(a.Path, a.Id8 + ".txt");
        var origin = await w.OriginRefsAsync();
        var wrote = false;
        var previous = w.Fixture.Git.AfterCommand;
        w.Fixture.Git.AfterCommand = async (directory, args, result) =>
        {
            if (previous is not null) await previous(directory, args, result);
            // The first plain ls-files in the tree is the late-name census, after the final clean status.
            if (wrote || args.Count != 2 || args[0] != "ls-files" || args[1] != "-z" || !LandingGit.PathsEqual(directory, a.Path))
                return;
            wrote = true;
            await File.WriteAllTextAsync(tracked, "unpushed bytes after the clean status\n");
        };

        var tick = await w.TickAsync();

        wrote.ShouldBeTrue();
        w.Settings.Value.Execute.ShouldBeTrue();
        var row = await w.RowAsync(tick.RunId, a);
        row.ReasonCode.ShouldBe("tracked_bytes_changed");
        (await File.ReadAllTextAsync(tracked)).ShouldBe("unpushed bytes after the clean status\n");
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
        (await w.OriginRefsAsync()).ShouldBe(origin);
    }

    private static bool IsStageListing(IReadOnlyList<string> args) =>
        args.Count >= 2 && args[0] == "ls-files" && args[1] == "--stage";

    /// <summary>Forwards to the real directory and runs <see cref="After"/> once each description has returned.</summary>
    public class DescribeProbe : DispatchProxy
    {
        public ISessionRunnerDirectory Inner { get; set; } = null!;
        public Action After { get; set; } = () => { };

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var value = method!.Invoke(Inner, args);
            return method.Name == "DescribeAsync" ? AfterDescribe((Task<RunnerDescriptor?>)value!) : value;
        }

        private async Task<RunnerDescriptor?> AfterDescribe(Task<RunnerDescriptor?> pending)
        {
            var result = await pending;
            await Task.Yield();
            After();
            return result;
        }
    }

    private static async Task<DesktopLegacyWorld.Handle> StartCommandAsync(DesktopLegacyWorld w)
    {
        var a = await w.AddTaskAsync(ageHours: 60);
        var fired = false;
        w.Host.RetirementCut.Cut = "intent-after";
        w.Host.RetirementCut.Pause = () =>
        {
            if (fired) return Task.CompletedTask;
            fired = true;
            w.Host.RetirementCut.Cut = null;
            throw new InvalidOperationException("c1717 cut after command intent");
        };
        var tick = await w.TickAsync();
        fired.ShouldBeTrue();
        (await w.RowAsync(tick.RunId, a)).Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        var release = (await w.ReleasesAsync(a)).Single();
        release.CallerIdentity.ShouldBe(TaskWorktreeRetirementService.SystemCallerIdentity);
        // The intent-after cut commits on the attempt. The retirement row's CommandIntentId is a later save
        // that this cut never reaches.
        var attempts = await w.AttemptsAsync(a);
        attempts.ShouldContain(x => x.CommandIntentId != null, "the command intent is committed on the attempt");
        Directory.Exists(a.Path).ShouldBeTrue();
        return a;
    }

    private static async Task<DesktopLegacyWorld.Handle> RefuseUnregisterAsync(DesktopLegacyWorld w)
    {
        var a = await w.AddTaskAsync(ageHours: 60);
        var failed = false;
        w.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (!failed && args.Count > 3 && args[1] == "remove" && args[2] == "--registration-only")
            {
                failed = true;
                return Task.FromResult<LandingGitResult?>(new LandingGitResult(1, "", "c1717 injected unregister failure"));
            }
            return Task.FromResult<LandingGitResult?>(null);
        };
        var tick = await w.TickAsync();
        failed.ShouldBeTrue();
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
        Directory.Exists(a.Path).ShouldBeTrue();
        (await w.ReleasesAsync(a)).Single().CallerIdentity.ShouldBe(TaskWorktreeRetirementService.SystemCallerIdentity);
        w.Fixture.Git.BeforeCommand = null;
        return a;
    }

    private static async Task<WorktreeResidueRunCandidate> SweepAsync(DesktopLegacyWorld w, DesktopLegacyWorld.Handle handle)
    {
        await using var scope = w.Host.Services.CreateAsyncScope();
        var sweep = new WorktreeResidueSweepService(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IWorktreeManager>(),
            Options.Create(new WorktreeResidueSettings { Execute = true, MinSettledMinutes = 0, MaxActionsPerRun = 25 }),
            Options.Create(new GitSettings { WorktreeBasePath = w.Root }),
            w.Clock, NullLogger<WorktreeResidueSweepService>.Instance,
            scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>());
        var job = new WorktreeResidueJob(sweep, NullLogger<WorktreeResidueJob>.Instance);
        await job.ExecuteAsync(CancellationToken.None);
        await using var db = w.Db();
        return await db.WorktreeResidueRunCandidates.AsNoTracking()
            .Where(c => c.TaskId == handle.Id && c.Lane == nameof(WorktreeResidueLane.SettledTask))
            .OrderByDescending(c => c.EvaluatedAt).ThenByDescending(c => c.Id)
            .FirstAsync();
    }

    private static async Task AssertBytesAsync(DesktopLegacyWorld w, DesktopLegacyWorld.Handle handle, string snapshot)
    {
        (await w.SnapshotAsync(handle.Path)).ShouldBe(snapshot, "the tree is byte-intact");
        if (Directory.Exists(handle.Path))
            (await w.RegisteredAsync(handle.Path)).ShouldBeTrue("the registration is intact");
        (await w.BranchExistsAsync(handle)).ShouldBeTrue("the local branch is intact");
    }

    /// <summary><paramref name="which"/> is <c>enabled</c>, <c>execute</c>, <c>both</c>, or <c>on</c>.</summary>
    private static void Apply(DesktopLegacyWorld w, string which)
    {
        var current = w.Settings.Value;
        var enabled = which is "on" or "execute";
        var execute = which is "on" or "enabled";
        w.Settings.Value = new WorktreeLegacyReleaseSettings
        {
            Enabled = enabled,
            Execute = execute,
            PauseBetweenSeconds = current.PauseBetweenSeconds,
            MinTerminalHours = current.MinTerminalHours,
            MaxReleasesPerTick = current.MaxReleasesPerTick,
            MaxTickMinutes = current.MaxTickMinutes,
            RefusedRecheckMinutes = current.RefusedRecheckMinutes,
            RefusedRecheckMaxHours = current.RefusedRecheckMaxHours,
            MaxRefusedAttempts = current.MaxRefusedAttempts,
            RecurringJobId = current.RecurringJobId,
            Cron = current.Cron,
        };
    }
}
