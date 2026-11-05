using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
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
