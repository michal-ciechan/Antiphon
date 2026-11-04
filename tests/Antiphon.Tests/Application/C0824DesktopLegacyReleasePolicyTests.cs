using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0824 S4a test design, class LP: the desktop legacy release policy over real git, a real origin remote
/// and an isolated schema. Recipient evidence is the filesystem and git (directory, registration, local branch,
/// origin refs), never a DB flag alone, and every refusal case keeps one eligible control task that must retire.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Timeout(45_000)]
public sealed class C0824DesktopLegacyReleasePolicyTests
{
    private const string SystemIdentity = TaskWorktreeRetirementService.SystemCallerIdentity;
    private static readonly AgentTaskService.Caller Operator = new(null, null, "");

    /// <summary>V-6 (+ M-4 next-stage case): one tick files one system release and removes the tree.</summary>
    [Test]
    [Arguments(AgentTaskStatus.Succeeded, "pushed")]
    [Arguments(AgentTaskStatus.Failed, "base-only")]
    [Arguments(AgentTaskStatus.Canceled, "pushed")]
    [Arguments(AgentTaskStatus.Succeeded, "pushed-next-stage")]
    public async Task Eligible_terminal_task_gets_exactly_one_system_release(AgentTaskStatus status, string shape)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(t =>
        {
            t.Status = status;
            if (shape == "pushed-next-stage") t.NextStage = PipelineHandoffKind.Review;
        }, shape == "base-only" ? "base-only" : "pushed");
        var origin = await w.OriginRefsAsync();

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(a, origin);
        var release = (await w.ReleasesAsync(a)).Single();
        release.CallerIdentity.ShouldBe(SystemIdentity);
        release.ReleaseReason.ShouldBe("CARD-0824 S4a terminal>=48h");
        release.DestinationFullRef.ShouldBe(shape == "base-only" ? "refs/heads/master" : a.FullRef);
        release.TargetFullRef.ShouldBe("refs/heads/master");
        if (shape == "pushed-next-stage")
            release.HandoffDispositionJson.ShouldContain("CARD-0824 S4a: terminal >= 48h; branch " + a.FullRef + " on origin at ");
        var attempts = await w.AttemptsAsync(a);
        attempts.Count.ShouldBe(1, "exactly one attempt");
        attempts[0].CommandIntentId.ShouldNotBeNull("the removal went through TryRetireAsync's command intent");

        await using (var db = w.Db())
        {
            var run = await db.WorktreeResidueRuns.AsNoTracking().SingleAsync(r => r.Id == tick.RunId);
            run.Scope.ShouldBe("desktop-legacy-release");
            run.Preview.ShouldBeFalse();
        }
        var row = await w.RowAsync(tick.RunId, a);
        row.Lane.ShouldBe("DesktopLegacyPolicy");
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Removed));
        row.RetirementId.ShouldBe(release.Id);
        row.Path.ShouldBe(a.Path);
        row.Branch.ShouldBe(a.Branch);
        await using (var scope = w.Host.Services.CreateAsyncScope())
        {
            var dto = await scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>()
                .GetRunAsync(tick.RunId!.Value, 0, Operator, CancellationToken.None);
            dto.Rows.ShouldContain(c => c.TaskId == a.Id && c.Outcome == nameof(WorktreeResidueCandidateOutcome.Removed));
        }
        tick.Filed.ShouldBe(1);
        tick.Retired.ShouldBe(1);
    }

    /// <summary>
    /// V-7/G-11/G-12: CompletedAt at least 48 h old (exactly 48 h is eligible); a null CompletedAt is settling. CARD-0824
    /// S4a survivor PC-11: CompletedAt round-trips Postgres at microsecond precision, so the clock starts on a whole
    /// second and the "48h" case is exact; one microsecond inside the window is still settling.
    /// </summary>
    [Test]
    [Arguments("47h59m")]
    [Arguments("48h")]
    [Arguments("48h-minus-1us")]
    [Arguments("null")]
    public async Task Age_floor_is_48_hours(string age)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        w.Clock.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond - w.Now.Ticks % TimeSpan.TicksPerSecond));
        var control = await w.AddTaskAsync(ageHours: 60);
        var a = await w.AddTaskAsync(t =>
        {
            if (age == "null") t.CompletedAt = null;
            if (age == "48h-minus-1us") t.CompletedAt = w.Now.AddHours(-48).AddTicks(10);
        }, ageHours: age == "47h59m" ? 47 + 59 / 60.0 : 48);
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);
        w.ClearTrace();

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(control, origin);
        if (age == "48h")
        {
            await w.AssertRetiredAsync(a, origin);
            return;
        }
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe("settling");
        await w.AssertIntactAsync(a, before);
        w.AssertNoGit(a);
    }

    /// <summary>V-8/G-13: only a terminal task is released (Blocked is not terminal, §8).</summary>
    [Test]
    [Arguments(AgentTaskStatus.Queued)]
    [Arguments(AgentTaskStatus.Dispatched)]
    [Arguments(AgentTaskStatus.Working)]
    [Arguments(AgentTaskStatus.Blocked)]
    public async Task Non_terminal_task_is_never_released(AgentTaskStatus status)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var control = await w.AddTaskAsync(ageHours: 60);
        var a = await w.AddTaskAsync(t => t.Status = status);
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);
        w.ClearTrace();

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(control, origin);
        (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe("live_owner");
        await w.AssertIntactAsync(a, before);
        w.AssertNoGit(a);
    }

    /// <summary>V-9/G-14..G-24: every server predicate keeps the tree, each with its one owner's reason.</summary>
    [Test]
    [Arguments("land-requested", "land_pending")]
    [Arguments("land-started", "land_pending")]
    [Arguments("land-request-pending", "land_pending")]
    [Arguments("landing-row", "recovery_debt")]
    [Arguments("sync-Pending", "sync_debt")]
    [Arguments("sync-Ready", "sync_debt")]
    [Arguments("sync-Held", "sync_debt")]
    [Arguments("path-shared", "path_shared")]
    [Arguments("live-session", "live_session")]
    [Arguments("live-session-event-linked", "live_session")]
    [Arguments("result-null", "report_missing")]
    [Arguments("child-open", "dependent_open")]
    [Arguments("startref-open", "dependent_open")]
    [Arguments("startref-queued", "dependent_open")]
    [Arguments("startref-queued-origin", "dependent_open")]
    [Arguments("startref-queued-refs-heads", "dependent_open")]
    [Arguments("startref-open-refs-heads", "dependent_open")]
    [Arguments("basetask-queued", "dependent_open")]
    [Arguments("mutation", "mutation_excluded")]
    [Arguments("source-landing", "source_landing_excluded")]
    [Arguments("operator-release-active", "release_active_other")]
    public async Task Server_predicates_keep_the_tree(string predicate, string reason)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var control = await w.AddTaskAsync(ageHours: 60);
        var sessionId = Guid.NewGuid();
        Guid? landingId = null;
        if (predicate == "source-landing")
        {
            // The FK needs a landing row; it belongs to a task with no tree (never a candidate).
            var owner = Guid.NewGuid();
            landingId = Guid.NewGuid();
            await using var db = w.Db();
            db.AgentTasks.Add(Bare(owner, w.Now));
            await db.SaveChangesAsync();
            db.AgentTaskLandings.Add(Landing(landingId.Value, owner, w.Now, active: false));
            await db.SaveChangesAsync();
        }
        if (predicate is "live-session" or "live-session-event-linked")
        {
            await using var db = w.Db();
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, DefinitionName = "claude", AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running,
                Cwd = Path.Combine(w.Fixture.Root, "elsewhere"), Cols = 80, Rows = 24, CreatedAt = w.Now, StartedAt = w.Now, LastSeenAt = w.Now,
            });
            await db.SaveChangesAsync();
        }
        var a = await w.AddTaskAsync(t =>
        {
            switch (predicate)
            {
                case "land-requested": t.LandRequestedAt = w.Now; break;
                case "land-started": t.LandStartedAt = w.Now; break;
                case "live-session": t.AgentSessionId = sessionId; break;
                case "result-null": t.Result = null; break;
                case "mutation": t.Role = AgentTaskRole.Mutation; break;
                case "source-landing": t.SourceLandingOperationId = landingId; break;
            }
        });
        await using (var db = w.Db())
        {
            switch (predicate)
            {
                case "land-request-pending":
                    db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
                    {
                        Id = Guid.NewGuid(), TaskId = a.Id, IsPending = true, State = LandRequestState.Queued,
                        ExpectedSourceSha = (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim(), RequestedAt = w.Now,
                    });
                    break;
                case "landing-row":
                    db.AgentTaskLandings.Add(Landing(Guid.NewGuid(), a.Id, w.Now, active: false));
                    break;
                case "sync-Pending" or "sync-Ready" or "sync-Held":
                    db.AgentTaskSyncDebts.Add(new AgentTaskSyncDebt
                    {
                        Id = Guid.NewGuid(), TaskId = a.Id, Attempt = 1, SettlementEventId = Guid.NewGuid(),
                        State = Enum.Parse<AgentTaskSyncDebtState>(predicate["sync-".Length..]),
                        ReasonCode = "runner_sync_lease_busy", CreatedAt = w.Now, UpdatedAt = w.Now,
                    });
                    break;
                case "path-shared":
                    db.AgentTasks.Add(Bare(Guid.NewGuid(), w.Now, t => { t.Status = AgentTaskStatus.Working; t.WorktreePath = a.Path; }));
                    break;
                case "child-open":
                    db.AgentTasks.Add(Bare(Guid.NewGuid(), w.Now, t => { t.Status = AgentTaskStatus.Queued; t.ParentTaskId = a.Id; }));
                    break;
                case "startref-open":
                    // WorktreeBaseRef is the base a provisioned task was cut from (CARD-0508).
                    db.AgentTasks.Add(Bare(Guid.NewGuid(), w.Now, t => { t.Status = AgentTaskStatus.Dispatched; t.WorktreeBaseRef = a.Branch; }));
                    break;
                case "live-session-event-linked":
                    // CARD-0824 S4a survivor X-13b: the task row names no session; only an event links the Running one.
                    db.AgentTaskEvents.Add(new AgentTaskEvent
                    {
                        Id = Guid.NewGuid(), AgentTaskId = a.Id, AgentSessionId = sessionId, Type = AgentTaskEventType.Dispatched,
                        Detail = "c0824 event-linked session", At = w.Now,
                    });
                    break;
                case "startref-open-refs-heads":
                    // CARD-0824 S4a survivor PC-21e: the full-ref spelling of a provisioned base.
                    db.AgentTasks.Add(Bare(Guid.NewGuid(), w.Now, t => { t.Status = AgentTaskStatus.Dispatched; t.WorktreeBaseRef = a.FullRef; }));
                    break;
                case "startref-queued-refs-heads":
                    // CARD-0824 S4a survivor PC-21e: DelegationWorktreeService accepts a start ref spelled refs/heads/<branch>.
                    db.AgentTasks.Add(Bare(Guid.NewGuid(), w.Now, t => { t.Status = AgentTaskStatus.Queued; t.WorktreeBaseRequestedRef = a.FullRef; }));
                    break;
                case "startref-queued" or "startref-queued-origin":
                    // Review 1dcb8d90 D-1: a queued delegate.ps1 -StartRef holds only WorktreeBaseRequestedRef until it is
                    // provisioned, and a branch-name start ref must still resolve locally then (CARD-0613).
                    db.AgentTasks.Add(Bare(Guid.NewGuid(), w.Now, t =>
                    {
                        t.Status = AgentTaskStatus.Queued;
                        t.WorktreeBaseRequestedRef = predicate == "startref-queued" ? a.Branch : "origin/" + a.Branch;
                    }));
                    break;
                case "basetask-queued":
                    db.AgentTasks.Add(Bare(Guid.NewGuid(), w.Now, t =>
                    {
                        t.Status = AgentTaskStatus.Queued;
                        t.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task;
                        t.RequestedWorktreeBaseTaskId = a.Id;
                    }));
                    break;
            }
            await db.SaveChangesAsync();
        }
        if (predicate == "operator-release-active")
        {
            await using var scope = w.Host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == a.Id);
            await scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>().ReleaseAsync(a.Id,
                new ReleaseWorktreeRetirementRequest(task.ConcurrencyToken, (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim(),
                    null, true, "reviewed no further use"), Operator, CancellationToken.None);
        }
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);
        w.ClearTrace();

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(control, origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe(reason);
        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue();
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
        var releases = await w.ReleasesAsync(a);
        releases.ShouldNotContain(r => r.CallerIdentity == SystemIdentity, "no system release");
        if (predicate == "operator-release-active")
            releases.Single().State.ShouldBe(WorktreeRetirementState.Released, "the operator's release is left alone");
        else
            releases.ShouldBeEmpty();
        // The policy's own predicates hold before any git; the service-owned exclusions are reached by filing.
        if (predicate is not ("landing-row" or "mutation" or "source-landing" or "operator-release-active"))
            w.AssertNoGit(a);
        w.Fixture.Git.Trace.ShouldNotContain(x => x.Length > 0 && x[0] == "update-ref" && x.Contains(a.FullRef));
    }

    /// <summary>V-10/G-25..G-28/G-32: a dirty or unpushed tree is never released; the Held row is the evidence.</summary>
    [Test]
    [Arguments("tracked-modified", "source_dirty")]
    [Arguments("untracked", "source_dirty")]
    [Arguments("staged", "source_dirty")]
    [Arguments("unpushed-commit", "unpushed")]
    [Arguments("origin-branch-deleted", "unpushed")]
    [Arguments("origin-unreachable", "remote_unverified")]
    public async Task Dirty_or_unpushed_tree_is_never_released(string shape, string reason)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var control = await w.AddTaskAsync(ageHours: 60);
        var a = await w.AddTaskAsync(shape: shape == "unpushed-commit" ? "pushed-then-commit" : "pushed");
        switch (shape)
        {
            case "tracked-modified": await File.WriteAllTextAsync(Path.Combine(a.Path, a.Id8 + ".txt"), "changed\n"); break;
            case "untracked": await File.WriteAllTextAsync(Path.Combine(a.Path, "new.txt"), "new\n"); break;
            case "staged":
                await File.WriteAllTextAsync(Path.Combine(a.Path, "staged.txt"), "staged\n");
                await w.Fixture.RequiredAsync(a.Path, "add", "--", "staged.txt");
                break;
            case "origin-branch-deleted": await w.Fixture.RequiredAsync(w.Fixture.Repository, "push", "origin", "--delete", a.Branch); break;
            case "origin-unreachable":
                // Origin unreachable for this task only (the control shares the repository): its ls-remote fails.
                w.Fixture.Git.BeforeCommand = (_, args) => Task.FromResult<LandingGitResult?>(
                    args.Count > 0 && args[0] == "ls-remote" && args.Contains(a.FullRef)
                        ? new LandingGitResult(128, "", "fatal: 'origin' does not appear to be a git repository") : null);
                break;
        }
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);
        var head = (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim();

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(control, origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe(reason);
        row.Branch.ShouldBe(a.Branch);
        row.Path.ShouldBe(a.Path);
        await w.AssertIntactAsync(a, before);
        (await w.BranchExistsAsync(a)).ShouldBeTrue("the local branch is kept");
        (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim().ShouldBe(head);
    }

    /// <summary>V-11/G-33/G-34: ignored files go through the CARD-0665 gate (evidence retained, protected refused).</summary>
    [Test]
    [Arguments("evidence-allowlisted")]
    [Arguments("protected")]
    public async Task Ignored_content_goes_through_the_evidence_step(string shape)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var control = await w.AddTaskAsync(ageHours: 60);
        var a = await w.AddTaskAsync();
        var relative = shape == "protected" ? ".claude/settings.local.json" : ".antiphon/task-" + a.Id8 + ".md";
        var classifier = w.Host.Services.GetRequiredService<WorktreeIgnoredContentClassifier>();
        var verdict = classifier.Classify([relative]);
        if (shape == "protected") verdict.Protected.ShouldContain(relative);
        else verdict.Evidence.ShouldContain(relative, "the path is in Evidence, not Disposable");
        var file = Path.Combine(a.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var bytes = Encoding.UTF8.GetBytes("c0824 evidence " + a.Id8 + "\n");
        await File.WriteAllBytesAsync(file, bytes);
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(control, origin);
        var row = await w.RowAsync(tick.RunId, a);
        if (shape == "evidence-allowlisted")
        {
            row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Removed));
            await w.AssertRetiredAsync(a, origin);
            var retained = Directory.EnumerateFiles(w.RetainedRoot, Path.GetFileName(file), SearchOption.AllDirectories)
                .Where(p => p.Contains(a.Id.ToString("N"), StringComparison.Ordinal)).ToList();
            retained.Count.ShouldBe(1, "the evidence was retained before the tree was deleted");
            (await File.ReadAllBytesAsync(retained[0])).ShouldBe(bytes);
            return;
        }
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
        row.ReasonCode.ShouldBe("ignored_content_preserved");
        (await w.SnapshotAsync(a.Path)).ShouldBe(before);
        (await File.ReadAllBytesAsync(file)).ShouldBe(bytes);
        var release = (await w.ReleasesAsync(a)).Single();
        release.Active.ShouldBeTrue();
        (await w.RetryCursorAsync(a))!.NotBefore.ShouldNotBeNull("the backoff cursor is stamped");
    }

    /// <summary>V-12/G-29..G-31: the guarded removal re-proves every fact after the policy filed.</summary>
    [Test]
    [Arguments("dirty-after-filing", "source_dirty")]
    [Arguments("commit-after-filing", "source_changed")]
    [Arguments("origin-rewound-after-filing", "remote_no_longer_contains_source")]
    public async Task Removal_re_proves_every_fact_after_filing(string change, string reason)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var control = await w.AddTaskAsync(ageHours: 50);
        var origin = await w.OriginRefsAsync();
        var fired = false;
        w.Host.RetirementCut.Cut = "release-after";
        w.Host.RetirementCut.Pause = async () =>
        {
            if (fired) return;
            fired = true;
            w.Host.RetirementCut.Cut = null;
            switch (change)
            {
                case "dirty-after-filing": await File.WriteAllTextAsync(Path.Combine(a.Path, "late.txt"), "late\n"); break;
                case "commit-after-filing": await w.CommitAsync(a, "late.txt"); break;
                case "origin-rewound-after-filing":
                    await w.Fixture.RequiredAsync(w.Fixture.Repository, "push", "--force", "origin", "master:" + a.FullRef);
                    origin = await w.OriginRefsAsync();
                    break;
            }
        };

        var tick = await w.TickAsync();

        fired.ShouldBeTrue("the cut fired on the refused task's release save");
        await w.AssertRetiredAsync(control, origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
        row.ReasonCode.ShouldBe(reason);
        Directory.Exists(a.Path).ShouldBeTrue("the directory is present");
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue("the registration is present");
        (await w.BranchExistsAsync(a)).ShouldBeTrue("the branch is present");
        var release = (await w.ReleasesAsync(a)).Single();
        release.Active.ShouldBeTrue();
        release.CallerIdentity.ShouldBe(SystemIdentity);
        var attempt = (await w.AttemptsAsync(a)).Single();
        attempt.CommandIntentId.ShouldNotBeNull();
        attempt.DirectoryRemoved.ShouldNotBe(true);
        (await w.RetryCursorAsync(a))!.NotBefore.ShouldNotBeNull("the cursor is stamped");

        if (change != "dirty-after-filing") return;
        // T-D-4: the attempt removed nothing, so the next due tick reopens it once the tree is clean again.
        File.Delete(Path.Combine(a.Path, "late.txt"));
        w.Clock.Advance(TimeSpan.FromMinutes(61));
        await w.TickAsync();
        await w.AssertRetiredAsync(a, origin);
        (await w.AttemptsAsync(a)).Count.ShouldBe(2);
    }

    /// <summary>V-13/G-46/G-47 (T-D-6): a retention-owned runner's mirror is never touched; the desktop half retires.</summary>
    [Test]
    [Arguments("listed")]
    [Arguments("unlisted")]
    [Arguments("runner-unreachable")]
    public async Task Retention_owned_runner_mirror_is_never_touched(string runner)
    {
        PhoneHomeTestHost? host = null;
        await using var w = await DesktopLegacyWorld.CreateAsync(configure: services =>
            services.AddSingleton(sp => new RemoteWorkspaceService(host!.Directory, sp.GetRequiredService<ILandingGit>(),
                NullLogger<RemoteWorkspaceService>.Instance)));
        host = await PhoneHomeTestHost.StartAsync(w.Clock, w.Host.Schema.ConnectionString);
        try
        {
            var peer = await host.ConnectPeerAsync();
            host.Directory.MarkRecovered(await host.WaitLiveAsync());
            peer.Reply = frame => frame.Operation == PhoneHomeOperation.WorkspaceRemove
                ? new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                    JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceRemoveResponse(true, null), PhoneHomeFraming.Json))
                : null;
            if (runner == "listed") w.Retention.Value.Runners.Add(host.AllowedRunnerId);
            var mirror = "/work/worktrees/task-" + Guid.NewGuid().ToString("N")[..8];
            var a = await w.AddTaskAsync(t => { t.RunnerId = host.AllowedRunnerId; t.RemoteWorktreePath = mirror; });
            var origin = await w.OriginRefsAsync();
            if (runner == "runner-unreachable") await peer.DisposeAsync();

            var tick = await w.TickAsync();

            await w.AssertRetiredAsync(a, origin);
            await using var db = w.Db();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == a.Id);
            var row = await w.RowAsync(tick.RunId, a);
            switch (runner)
            {
                case "listed":
                    peer.RequestCount(PhoneHomeOperation.WorkspaceRemove).ShouldBe(0, "a retention-owned mirror is never touched");
                    task.RemoteWorktreePath.ShouldBe(mirror);
                    row.ReasonCode.ShouldBe("runner_mirror_owned_by_retention");
                    break;
                case "unlisted":
                    peer.RequestCount(PhoneHomeOperation.WorkspaceRemove).ShouldBe(1);
                    peer.Incoming.Single(f => f.Kind == PhoneHomeFrameKind.Request && f.Operation == PhoneHomeOperation.WorkspaceRemove)
                        .Payload!.Value.Deserialize<PhoneHomeWorkspaceRemoveRequest>(PhoneHomeFraming.Json)!.Path.ShouldBe(mirror);
                    task.RemoteWorktreePath.ShouldBeNull();
                    row.ReasonCode.ShouldBe("removed");
                    break;
                default:
                    task.RemoteWorktreeResidue.ShouldBe(mirror);
                    break;
            }
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>V-14/G-35/G-42/G-44/G-45: only an ordinary card-task leaf of this task is a candidate.</summary>
    [Test]
    [Arguments("ws-01", "slot_managed")]
    [Arguments("ws-01-d2", "slot_managed")]
    [Arguments("card-task-other", "leaf_mismatch")]
    [Arguments("card-task-rc-suffix", "leaf_mismatch")]
    [Arguments("canonical-checkout", "leaf_mismatch")]
    [Arguments("canonical-checkout-named", "main_worktree")]
    [Arguments("master-branch", "outside_scope")]
    [Arguments("rc-branch", "outside_scope")]
    [Arguments("rc-candidate-dir", "outside_root")]
    [Arguments("nested-under-root", "outside_root")]
    [Arguments("outside-root-normalized", "outside_root")]
    public async Task Only_ordinary_card_task_leaves_are_candidates(string shape, string reason)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var control = await w.AddTaskAsync(ageHours: 60);
        var id = Guid.NewGuid();
        var id8 = id.ToString("N")[..8];
        DesktopLegacyWorld.Handle a;
        switch (shape)
        {
            case "ws-01" or "ws-01-d2" or "card-task-other" or "card-task-rc-suffix":
                var leaf = shape switch
                {
                    "card-task-other" => "card-task-" + Guid.NewGuid().ToString("N")[..8],
                    "card-task-rc-suffix" => "card-task-" + id8 + "-rc",
                    _ => shape,
                };
                a = await w.AddTaskAsync(id: id, path: Path.Combine(w.Root, leaf));
                break;
            case "canonical-checkout":
                a = await w.AddTaskAsync(id: id, shape: "none", path: w.Fixture.Repository);
                break;
            case "canonical-checkout-named":
                var clone = Path.Combine(w.Root, "card-task-" + id8);
                await w.Fixture.RequiredAsync(w.Fixture.Root, "clone", "--no-hardlinks", "--", w.Fixture.Remote, clone);
                a = await w.AddTaskAsync(id: id, shape: "none", path: clone);
                break;
            case "master-branch":
                a = await w.AddTaskAsync(t => t.WorktreeBranch = "master", id: id, shape: "base-only");
                break;
            case "rc-branch":
                await w.Fixture.RequiredAsync(w.Fixture.Repository, "branch", "rc/2026-11-04", "master");
                a = await w.AddTaskAsync(t => t.WorktreeBranch = "rc/2026-11-04", id: id, shape: "base-only");
                break;
            case "nested-under-root":
                // CARD-0824 S4a survivor X-11: normalized, absolute, the right leaf, but not a direct child of the root.
                a = await w.AddTaskAsync(id: id, path: Path.Combine(w.Root, "nested", "card-task-" + id8));
                break;
            case "outside-root-normalized":
                a = await w.AddTaskAsync(id: id, path: Path.Combine(w.Fixture.Root, "elsewhere", "card-task-" + id8));
                break;
            default:
                a = await w.AddTaskAsync(id: id, shape: "none",
                    path: w.Root + "/../releases/candidates/rc-2026-11-04-" + new string('a', 12));
                break;
        }
        var origin = await w.OriginRefsAsync();
        var before = shape == "canonical-checkout" ? "" : await w.SnapshotAsync(a.Path);
        w.ClearTrace();

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(control, origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe(reason);
        (await w.ReleasesAsync(a)).ShouldBeEmpty();
        if (shape != "canonical-checkout")
            (await w.SnapshotAsync(a.Path)).ShouldBe(before, "any directory involved is intact");
        Directory.Exists(w.Fixture.Repository).ShouldBeTrue();
        if (shape is not ("canonical-checkout" or "canonical-checkout-named" or "master-branch" or "rc-branch"))
            w.AssertNoGit(a);
        w.Fixture.Git.Trace.ShouldNotContain(x => x.Length > 1 && x[0] == "worktree" && x[1] == "remove"
            && x.Any(p => p.Contains(a.Path, StringComparison.Ordinal)));
        w.Fixture.Git.Trace.ShouldNotContain(x => x.Length > 0 && x[0] == "update-ref" && x.Contains("-d") && x.Contains(a.FullRef));
    }

    /// <summary>V-15/G-43: a leased runner slot keeps the task's desktop half; a released one does not.</summary>
    [Test]
    [Arguments("leased")]
    [Arguments("released")]
    public async Task Leased_runner_slot_keeps_the_desktop_half(string slot)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var control = await w.AddTaskAsync(ageHours: 60);
        var a = await w.AddTaskAsync(t =>
        {
            t.RemoteWorkspaceSlotId = "ws-03";
            t.RemoteWorkspaceSlotGeneration = 1;
            t.RemoteWorkspaceSlotReleasedAt = slot == "released" ? w.Now.AddHours(-40) : null;
        });
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);
        w.ClearTrace();

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(control, origin);
        if (slot == "released")
        {
            await w.AssertRetiredAsync(a, origin);
            return;
        }
        (await w.RowAsync(tick.RunId, a)).ReasonCode.ShouldBe("slot_leased");
        await w.AssertIntactAsync(a, before);
        w.AssertNoGit(a);
    }

    /// <summary>V-16/G-38..G-41: `..`, relative and link escapes are refused; the outside target is never touched.</summary>
    [Test]
    [Arguments("dotdot", "outside_root")]
    [Arguments("relative", "outside_root")]
    [Arguments("symlink-leaf", "reparse_point")]
    [Arguments("symlink-ancestor", "outside_root")]
    [Arguments("symlink-inside-tree", "ignored_reparse_point")]
    [Arguments("symlink-leaf-after-filing", "worktree_confinement_changed")]
    public async Task Path_escapes_are_refused(string escape, string reason)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var control = await w.AddTaskAsync(ageHours: 60);
        var id = Guid.NewGuid();
        var id8 = id.ToString("N")[..8];
        // The escape target: a real worktree outside the root, holding a marker file.
        var outsideParent = Path.Combine(w.Fixture.Root, "outside");
        var target = escape == "dotdot" ? Path.Combine(w.Fixture.Root, "card-task-" + id8) : Path.Combine(outsideParent, "card-task-" + id8);
        await w.Fixture.RequiredAsync(w.Fixture.Repository, "worktree", "add", "-b", "outside/" + id8, target, "master");
        await File.WriteAllTextAsync(Path.Combine(target, "marker.txt"), "outside\n");
        var targetBefore = await w.SnapshotAsync(target);
        DesktopLegacyWorld.Handle a;
        var leafPath = Path.Combine(w.Root, "card-task-" + id8);
        switch (escape)
        {
            case "dotdot":
                a = await w.AddTaskAsync(id: id, shape: "none", path: w.Root + "/x/../../card-task-" + id8);
                break;
            case "relative":
                a = await w.AddTaskAsync(id: id, shape: "none", path: "trees/card-task-" + id8);
                break;
            case "symlink-leaf":
                Directory.CreateSymbolicLink(leafPath, target);
                a = await w.AddTaskAsync(id: id, shape: "none", path: leafPath);
                break;
            case "symlink-ancestor":
                Directory.CreateSymbolicLink(Path.Combine(w.Root, "link"), outsideParent);
                a = await w.AddTaskAsync(id: id, shape: "none", path: Path.Combine(w.Root, "link", "card-task-" + id8));
                break;
            case "symlink-inside-tree":
                a = await w.AddTaskAsync(id: id);
                Directory.CreateDirectory(Path.Combine(a.Path, "obj"));
                Directory.CreateSymbolicLink(Path.Combine(a.Path, "obj", "link"), target);
                break;
            default:
                // Older than the control, so the cut fires on this task's release save.
                a = await w.AddTaskAsync(id: id, ageHours: 70);
                break;
        }
        var swapped = false;
        if (escape == "symlink-leaf-after-filing")
        {
            w.Host.RetirementCut.Cut = "release-after";
            w.Host.RetirementCut.Pause = () =>
            {
                if (swapped) return Task.CompletedTask;
                swapped = true;
                w.Host.RetirementCut.Cut = null;
                Directory.Move(a.Path, Path.Combine(w.Fixture.Root, "moved-" + id8));
                Directory.CreateSymbolicLink(a.Path, target);
                return Task.CompletedTask;
            };
        }
        var origin = await w.OriginRefsAsync();
        var registrationBefore = await w.RegisteredAsync(target);
        w.ClearTrace();

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(control, origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.ReasonCode.ShouldBe(reason);
        (await w.SnapshotAsync(target)).ShouldBe(targetBefore, "the outside target is byte-intact");
        (await w.RegisteredAsync(target)).ShouldBe(registrationBefore);
        if (escape is "symlink-inside-tree" or "symlink-leaf-after-filing")
        {
            row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
            if (escape == "symlink-leaf-after-filing") swapped.ShouldBeTrue();
            return;
        }
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        (await w.ReleasesAsync(a)).ShouldBeEmpty("no release");
    }

    /// <summary>V-17/G-37: a control character in the leaf or branch is refused before any pattern (built with escapes).</summary>
    [Test]
    [Arguments("leaf", "\n")]
    [Arguments("leaf", "\r")]
    [Arguments("leaf", "\u2028")]
    [Arguments("leaf", "\t")]
    [Arguments("leaf", "\u0085")]
    [Arguments("branch", "\n")]
    public async Task Control_character_names_are_refused(string where, string suffix)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var control = await w.AddTaskAsync(ageHours: 60);
        var id = Guid.NewGuid();
        var id8 = id.ToString("N")[..8];
        var a = await w.AddTaskAsync(id: id, shape: "none",
            path: Path.Combine(w.Root, "card-task-" + id8 + (where == "leaf" ? suffix : "")),
            branch: "feat/card-task-" + id8 + (where == "branch" ? suffix : ""));
        var origin = await w.OriginRefsAsync();
        w.ClearTrace();

        var tick = await w.TickAsync();

        await w.AssertRetiredAsync(control, origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe("name_control_character");
        (await w.ReleasesAsync(a)).ShouldBeEmpty();
        w.AssertNoGit(a);
    }

    /// <summary>V-18/G-2/G-3: the kill switch and Execute are re-read before each row of a running tick.</summary>
    [Test]
    [Arguments("enabled")]
    [Arguments("execute")]
    public async Task Switch_off_stops_the_tick_at_the_next_row(string setting)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var tasks = new List<DesktopLegacyWorld.Handle>();
        for (var i = 0; i < 5; i++) tasks.Add(await w.AddTaskAsync(ageHours: 60 - i));
        var origin = await w.OriginRefsAsync();
        var snapshots = new Dictionary<Guid, string>();
        foreach (var t in tasks) snapshots[t.Id] = await w.SnapshotAsync(t.Path);
        var flipped = false;

        var tick = await w.TickAsync(onRow: _ =>
        {
            if (flipped) return;
            flipped = true;
            var current = w.Settings.Value;
            w.Settings.Value = new WorktreeLegacyReleaseSettings
            {
                Enabled = setting != "enabled", Execute = setting != "execute", PauseBetweenSeconds = 0,
                MinTerminalHours = current.MinTerminalHours, MaxReleasesPerTick = current.MaxReleasesPerTick,
            };
        });

        var expected = setting == "enabled" ? "tick_stopped_disabled" : "tick_stopped_execute_off";
        await w.AssertRetiredAsync(tasks[0], origin);
        foreach (var t in tasks.Skip(1)) await w.AssertIntactAsync(t, snapshots[t.Id]);
        var rows = await w.RowsAsync(tick.RunId);
        rows.Count(r => r.Outcome == nameof(WorktreeResidueCandidateOutcome.Removed)).ShouldBe(1, "exactly 1 retired");
        rows.Count(r => r.Outcome == nameof(WorktreeResidueCandidateOutcome.Deferred)).ShouldBe(1);
        rows.Single(r => r.Outcome == nameof(WorktreeResidueCandidateOutcome.Deferred)).ReasonCode.ShouldBe(expected);
        tick.Stopped.ShouldBe(expected);
    }

    /// <summary>V-19/G-1: disabled at tick start writes no run, files nothing and runs zero git.</summary>
    [Test]
    public async Task Disabled_at_start_does_nothing()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync(s => s.Enabled = false);
        var a = await w.AddTaskAsync();
        var before = await w.SnapshotAsync(a.Path);
        w.ClearTrace();

        var tick = await w.TickAsync();

        tick.RunId.ShouldBeNull();
        tick.Stopped.ShouldBe("disabled");
        await using (var db = w.Db())
        {
            (await db.WorktreeResidueRuns.CountAsync()).ShouldBe(0, "no run row");
            (await db.WorktreeResidueRunCandidates.CountAsync()).ShouldBe(0);
        }
        await w.AssertIntactAsync(a, before);
        w.Fixture.Git.Trace.ShouldBeEmpty("zero git commands");
    }

    /// <summary>V-20/G-7/G-8/G-10: the count cap (oldest first) and the time budget bound a tick.</summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("count")]
    [Arguments("time")]
    public async Task Tick_is_bounded(string bound)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync(s => s.MaxTickMinutes = 20);
        var count = bound == "count" ? 30 : 4;
        var tasks = new List<DesktopLegacyWorld.Handle>();
        // Distinct CompletedAt; added newest first so the insert order is not the age order.
        for (var i = 0; i < count; i++) tasks.Add(await w.AddTaskAsync(ageHours: 49 + i));
        var oldest = tasks.OrderByDescending(t => tasks.IndexOf(t)).ToList();
        var origin = await w.OriginRefsAsync();

        if (bound == "time")
        {
            var tick = await w.TickAsync(onRow: _ => w.Clock.Advance(TimeSpan.FromMinutes(11)));
            var rows = await w.RowsAsync(tick.RunId);
            rows.Count(r => r.Outcome == nameof(WorktreeResidueCandidateOutcome.Removed)).ShouldBe(2);
            rows.Single(r => r.Outcome == nameof(WorktreeResidueCandidateOutcome.Deferred)).ReasonCode.ShouldBe("tick_budget_exhausted");
            tick.Stopped.ShouldBe("tick_budget_exhausted");
            return;
        }

        w.ClearTrace();
        var first = await w.TickAsync();
        foreach (var t in oldest.Take(25)) await w.AssertRetiredAsync(t, origin);
        foreach (var t in oldest.Skip(25))
        {
            (await w.ReleasesAsync(t)).ShouldBeEmpty("the 5 newest are untouched");
            Directory.Exists(t.Path).ShouldBeTrue();
            w.AssertNoGit(t);
        }
        first.Retired.ShouldBe(25);
        var second = await w.TickAsync();
        second.Retired.ShouldBe(5);
        foreach (var t in oldest.Skip(25)) await w.AssertRetiredAsync(t, origin);
    }

    /// <summary>V-21/G-4..G-6 (+ M-8): the preview and a dry-run tick file nothing and run only read-only git.</summary>
    [Test]
    [Arguments("preview")]
    [Arguments("execute-false")]
    public async Task Preview_and_dry_run_write_nothing(string mode)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync(s => s.Execute = mode != "execute-false");
        var eligible = new List<DesktopLegacyWorld.Handle>();
        for (var i = 0; i < 3; i++) eligible.Add(await w.AddTaskAsync(ageHours: 60 - i));
        var dirty = await w.AddTaskAsync(ageHours: 50);
        await File.WriteAllTextAsync(Path.Combine(dirty.Path, "dirty.txt"), "dirty\n");
        var slot = await w.AddTaskAsync(path: Path.Combine(w.Root, "ws-01"), ageHours: 50);
        var all = eligible.Append(dirty).Append(slot).ToList();
        var snapshots = new Dictionary<Guid, string>();
        foreach (var t in all) snapshots[t.Id] = await w.SnapshotAsync(t.Path);

        foreach (var enabled in mode == "preview" ? new[] { true, false } : [true])
        {
            w.Settings.Value.Enabled = enabled;
            w.ClearTrace();
            var tick = await w.TickAsync(preview: mode == "preview");

            await using (var db = w.Db())
            {
                (await db.TaskWorktreeRetirements.CountAsync()).ShouldBe(0, "zero release rows");
                (await db.TaskWorktreeRetirementAttempts.CountAsync()).ShouldBe(0, "zero attempt rows");
                (await db.WorktreeResidueRuns.AsNoTracking().SingleAsync(r => r.Id == tick.RunId)).Preview.ShouldBe(mode == "preview");
            }
            foreach (var t in all) (await w.SnapshotAsync(t.Path)).ShouldBe(snapshots[t.Id]);
            string[] allowed = ["status", "rev-parse", "show-ref", "ls-remote", "merge-base"];
            foreach (var command in w.Fixture.Git.Trace)
                (allowed.Contains(command[0]) || (command.Length > 1 && command[0] == "worktree" && command[1] == "list"))
                    .ShouldBeTrue("only read-only git: " + string.Join(' ', command));
            var rows = await w.RowsAsync(tick.RunId);
            rows.Where(r => r.Outcome == nameof(WorktreeResidueCandidateOutcome.Preview)).Select(r => r.TaskId)
                .ShouldBe(eligible.Select(t => (Guid?)t.Id), ignoreOrder: true);
            rows.ShouldAllBe(r => r.Outcome != nameof(WorktreeResidueCandidateOutcome.Preview) || r.ReasonCode == "would_release");
            rows.Single(r => r.TaskId == dirty.Id).ReasonCode.ShouldBe("source_dirty");
            rows.Single(r => r.TaskId == slot.Id).ReasonCode.ShouldBe("slot_managed");
        }
    }

    /// <summary>V-22/G-49: a replayed, concurrent or swept tick files one release, one attempt, one command.</summary>
    [Test]
    [Arguments("sequential")]
    [Arguments("concurrent")]
    [Arguments("residue-sweep-first")]
    public async Task Double_run_is_idempotent(string mode)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var b = await w.AddTaskAsync(ageHours: 55);
        var origin = await w.OriginRefsAsync();
        var runs = new List<Guid?>();
        switch (mode)
        {
            case "sequential":
                runs.Add((await w.TickAsync()).RunId);
                runs.Add((await w.TickAsync()).RunId);
                break;
            case "concurrent":
                var both = await Task.WhenAll(Task.Run(() => w.TickAsync()), Task.Run(() => w.TickAsync()));
                runs.AddRange(both.Select(t => t.RunId));
                break;
            default:
                var swept = false;
                w.Host.RetirementCut.Cut = "release-after";
                w.Host.RetirementCut.Pause = async () =>
                {
                    if (swept) return;
                    swept = true;
                    w.Host.RetirementCut.Cut = null;
                    await using var scope = w.Host.Services.CreateAsyncScope();
                    var sweep = new WorktreeResidueSweepService(
                        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                        scope.ServiceProvider.GetRequiredService<IWorktreeManager>(),
                        Options.Create(new WorktreeResidueSettings { Execute = true, MinSettledMinutes = 0, MaxActionsPerRun = 25 }),
                        Options.Create(new GitSettings { WorktreeBasePath = w.Root }),
                        w.Clock, NullLogger<WorktreeResidueSweepService>.Instance,
                        scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>());
                    await sweep.RunAsync(CancellationToken.None);
                };
                runs.Add((await w.TickAsync()).RunId);
                swept.ShouldBeTrue();
                break;
        }

        foreach (var t in new[] { a, b })
        {
            await w.AssertRetiredAsync(t, origin);
            var attempts = await w.AttemptsAsync(t);
            attempts.Count.ShouldBe(1, "one attempt per task");
            attempts.Count(x => x.CommandIntentId is not null).ShouldBe(1, "one command intent");
        }
        foreach (var run in runs)
            (await w.RowsAsync(run)).ShouldNotContain(r => r.ReasonCode == "release_conflict" || r.ReasonCode == "cleanup_command_slot_spent");
    }

    /// <summary>V-23/G-50/G-51/G-53: a crash at each handoff recovers exactly once.</summary>
    [Test]
    [Arguments("after-release-save")]
    [Arguments("after-intent-before-git")]
    [Arguments("after-move-aside")]
    public async Task Crash_at_each_handoff_recovers_once(string cut)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync();
        var origin = await w.OriginRefsAsync();
        var fired = false;
        if (cut == "after-move-aside")
        {
            var common = await w.Fixture.Git.CommonDirectoryAsync(w.Fixture.Repository, CancellationToken.None);
            var observe = w.Fixture.Git.BeforeObservedCommand;
            w.Fixture.Git.BeforeObservedCommand = async args =>
            {
                if (!fired && args.Count > 2 && args[0] == "worktree" && args[1] == "remove" && args[2] == "--registration-only")
                {
                    fired = true;
                    WorktreeSetAside.Read(common, a.Path).ShouldNotBeNull("the set-aside record exists at the cut");
                    Directory.Exists(a.Path).ShouldBeFalse("the tree was moved aside");
                    throw new InvalidOperationException("c0824 crash after move-aside");
                }
                if (observe is not null) await observe(args);
            };
        }
        else
        {
            w.Host.RetirementCut.Cut = cut == "after-release-save" ? "release-after" : "intent-after";
            w.Host.RetirementCut.Pause = () =>
            {
                if (fired) return Task.CompletedTask;
                fired = true;
                w.Host.RetirementCut.Cut = null;
                throw new InvalidOperationException("c0824 crash at " + cut);
            };
        }

        var first = await w.TickAsync();

        fired.ShouldBeTrue();
        (await w.RowAsync(first.RunId, a)).Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        Directory.Exists(a.Path).ShouldBeTrue("no unregistered directory: the tree is in place after tick 1");
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue();
        (await w.ReleasesAsync(a)).Count.ShouldBe(1);

        w.Clock.Advance(TimeSpan.FromMinutes(61));
        await w.TickAsync();

        await w.AssertRetiredAsync(a, origin);
        var attempts = await w.AttemptsAsync(a);
        attempts.Count.ShouldBe(cut == "after-release-save" ? 1 : 2);
        attempts.Count(x => x.CommandIntentId is not null).ShouldBe(attempts.Count);
    }

    /// <summary>V-24/G-54..G-59: a failing task backs off per task and never starves the others.</summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("retry-succeeds")]
    [Arguments("backoff-doubles")]
    [Arguments("exhausted")]
    [Arguments("throws")]
    [Arguments("lease-busy")]
    public async Task Failures_back_off_per_task_and_never_starve_others(string failure)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync(s =>
        {
            // Review 1dcb8d90 D-2: every pre-check counts against the cap whatever its outcome, so the two cases that need
            // A, B and C all reached in one tick run with a cap of 3.
            s.MaxReleasesPerTick = failure is "throws" or "lease-busy" ? 3 : 2;
            s.MaxRefusedAttempts = failure == "exhausted" ? 3 : failure == "backoff-doubles" ? 10 : 5;
        });
        var a = await w.AddTaskAsync(ageHours: 70);
        var b = await w.AddTaskAsync(ageHours: 60);
        var c = await w.AddTaskAsync(ageHours: 50);
        var origin = await w.OriginRefsAsync();
        var failures = failure switch { "retry-succeeds" => 1, "backoff-doubles" or "exhausted" => int.MaxValue, _ => 0 };
        w.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (failures > 0 && args.Count > 3 && args[1] == "remove" && args[2] == "--registration-only" && args[3] == a.Path)
            {
                failures--;
                return Task.FromResult<LandingGitResult?>(new LandingGitResult(1, "", "c0824 injected unregister failure"));
            }
            return Task.FromResult<LandingGitResult?>(null);
        };

        switch (failure)
        {
            case "retry-succeeds":
            {
                var before = await w.SnapshotAsync(a.Path);
                var t1 = await w.TickAsync();
                var rowA = await w.RowAsync(t1.RunId, a);
                rowA.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
                rowA.ReasonCode.ShouldBe("worktree_remove_failed");
                (await w.SnapshotAsync(a.Path)).ShouldBe(before, "A is restored and intact");
                await w.AssertRetiredAsync(b, origin);
                (await w.ReleasesAsync(c)).ShouldBeEmpty("cap 2: A's attempt counts, B is the second");

                w.Clock.Advance(TimeSpan.FromMinutes(30));
                w.ClearTrace();
                var t2 = await w.TickAsync();
                (await w.RowAsync(t2.RunId, a)).ReasonCode.ShouldBe("refused_recheck_pending");
                w.AssertNoGit(a);
                await w.AssertRetiredAsync(c, origin);

                w.Clock.Advance(TimeSpan.FromMinutes(31));
                await w.TickAsync();
                await w.AssertRetiredAsync(a, origin);
                (await w.AttemptsAsync(a)).Count.ShouldBe(2);
                break;
            }
            case "backoff-doubles":
            {
                var deltas = new List<double>();
                for (var i = 0; i < 7; i++)
                {
                    var tick = await w.TickAsync();
                    (await w.RowAsync(tick.RunId, a)).Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
                    var cursor = (await w.RetryCursorAsync(a))!;
                    deltas.Add(Math.Round((cursor.NotBefore!.Value - w.Now).TotalMinutes));
                    if (i == 1)
                    {
                        await w.AssertRetiredAsync(b, origin);
                        await w.AssertRetiredAsync(c, origin);
                    }
                    w.Clock.Advance(cursor.NotBefore!.Value - w.Now + TimeSpan.FromMinutes(1));
                }
                deltas.ShouldBe([60, 120, 240, 480, 960, 1440, 1440]);
                break;
            }
            case "exhausted":
            {
                for (var i = 0; i < 3; i++)
                {
                    var tick = await w.TickAsync();
                    (await w.RowAsync(tick.RunId, a)).Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
                    w.Clock.Advance(TimeSpan.FromHours(25));
                }
                (await w.AttemptsAsync(a)).Count.ShouldBe(3);
                w.ClearTrace();
                var held = await w.TickAsync();
                (await w.RowAsync(held.RunId, a)).ReasonCode.ShouldBe("retry_exhausted");
                w.Clock.Advance(TimeSpan.FromHours(48));
                await w.TickAsync();
                (await w.AttemptsAsync(a)).Count.ShouldBe(3, "no attempt 4");
                w.AssertNoGit(a);
                Directory.Exists(a.Path).ShouldBeTrue();
                break;
            }
            case "throws":
            {
                w.Host.LandCutInterceptor = new ThrowOnRelease(a.Id);
                var tick = await w.TickAsync();
                var rowA = await w.RowAsync(tick.RunId, a);
                rowA.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
                rowA.ReasonCode.ShouldBe("release_failed");
                await w.AssertRetiredAsync(b, origin);
                await w.AssertRetiredAsync(c, origin);
                Directory.Exists(a.Path).ShouldBeTrue();
                (await w.ReleasesAsync(a)).ShouldBeEmpty();
                break;
            }
            default:
            {
                var snapshots = new Dictionary<Guid, string>();
                foreach (var t in new[] { a, b, c }) snapshots[t.Id] = await w.SnapshotAsync(t.Path);
                await using (await w.Harness.LeaseAsync())
                {
                    var tick = await w.TickAsync();
                    foreach (var t in new[] { a, b, c })
                    {
                        var row = await w.RowAsync(tick.RunId, t);
                        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
                        row.ReasonCode.ShouldBe("repository_lease_required");
                        (await w.SnapshotAsync(t.Path)).ShouldBe(snapshots[t.Id], "nothing removed");
                    }
                    var cursor = await w.RetryCursorAsync(a);
                    (cursor?.LastOutcomeKey ?? "").ShouldNotStartWith("retry|", Case.Sensitive, "A's refused count stays 0");
                }
                await w.TickAsync();
                await w.AssertRetiredAsync(a, origin);
                await w.AssertRetiredAsync(b, origin);
                break;
            }
        }
    }

    /// <summary>
    /// V-25/G-9: a first enable over a large backlog costs at most the cap in pre-checks. Review 1dcb8d90 D-2: a missing
    /// directory is settled on the first tick (one Held row, no git, no cap) and never re-checked.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("preview-first")]
    [Arguments("execute")]
    public async Task First_enable_on_a_large_backlog_is_bounded(string mode)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var real = new List<DesktopLegacyWorld.Handle>();
        for (var i = 0; i < 27; i++) real.Add(await w.AddTaskAsync(ageHours: 24 * 30 + 27 - i));
        var absent = new List<DesktopLegacyWorld.Handle>();
        for (var i = 0; i < 273; i++) absent.Add(await w.AddTaskAsync(shape: "none", ageHours: 24 * 3 + i));
        var oldest = real.Take(25).ToList();
        var origin = await w.OriginRefsAsync();
        w.ClearTrace();

        if (mode == "preview-first")
        {
            var preview = await w.TickAsync(preview: true);
            var rows = await w.RowsAsync(preview.RunId);
            rows.Count.ShouldBe(300);
            rows.Where(r => r.ReasonCode == "would_release").Select(r => r.TaskId).ShouldBe(oldest.Select(t => (Guid?)t.Id), ignoreOrder: true);
            rows.Count(r => r.Outcome == nameof(WorktreeResidueCandidateOutcome.Deferred) && r.ReasonCode == "over_cap").ShouldBe(2);
            rows.Where(r => r.ReasonCode == "directory_absent").Select(r => r.TaskId).ShouldBe(absent.Select(t => (Guid?)t.Id), ignoreOrder: true);
            await using (var scope = w.Host.Services.CreateAsyncScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
                var paged = 0;
                for (var page = 0; page < 6; page++)
                {
                    var dto = await service.GetRunAsync(preview.RunId!.Value, page, Operator, CancellationToken.None);
                    dto.TotalRows.ShouldBe(300);
                    paged += dto.Rows.Count;
                }
                paged.ShouldBe(300);
            }
            await using (var db = w.Db()) (await db.TaskWorktreeRetirements.CountAsync()).ShouldBe(0);
            foreach (var t in real.Skip(25).Concat(absent)) w.AssertNoGit(t);
            return;
        }

        var clock = Stopwatch.StartNew();
        var first = await w.TickAsync();
        clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60), "the bounded tick's wall time");
        first.Retired.ShouldBe(25);
        foreach (var t in oldest) await w.AssertRetiredAsync(t, origin);
        foreach (var t in real.Skip(25).Concat(absent)) w.AssertNoGit(t);
        (await w.RowsAsync(first.RunId)).Count(r => r.ReasonCode == "directory_absent").ShouldBe(273);
        var second = await w.TickAsync();
        second.Retired.ShouldBe(2);
        foreach (var t in real.Skip(25)) await w.AssertRetiredAsync(t, origin);
        var rows2 = await w.RowsAsync(second.RunId);
        rows2.ShouldNotContain(r => r.ReasonCode == "directory_absent", "a settled missing directory is not rewritten");
        rows2.Count.ShouldBe(2, "the second tick writes only its two removals");
        await using (var db = w.Db())
        {
            var ids = absent.Select(t => t.Id).ToList();
            (await db.TaskWorktreeRetirements.CountAsync(r => ids.Contains(r.TaskId))).ShouldBe(0, "never a release for an absent path");
        }
    }

    /// <summary>V-26/R-3/G-52: a caller-filed release keeps the CARD-1446 one-command rule (no T-D-4 reopen).</summary>
    [Test]
    public async Task Caller_filed_release_keeps_one_command()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        // Contained in the merge target, the operator release's destination.
        var a = await w.AddTaskAsync(shape: "base-only");
        var failed = false;
        w.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (!failed && args.Count > 3 && args[1] == "remove" && args[2] == "--registration-only" && args[3] == a.Path)
            {
                failed = true;
                return Task.FromResult<LandingGitResult?>(new LandingGitResult(1, "", "c0824 injected unregister failure"));
            }
            return Task.FromResult<LandingGitResult?>(null);
        };
        await using var scope = w.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == a.Id);
        var dto = await service.ReleaseAsync(a.Id, new ReleaseWorktreeRetirementRequest(task.ConcurrencyToken,
            (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim(), null, true, "reviewed no further use"), Operator, CancellationToken.None);
        var release = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == dto.Id);
        release.CallerIdentity.ShouldBe("operator");

        var first = await service.TryRetireAsync(release, null, CancellationToken.None);
        first.Residue.ShouldBe("worktree_remove_failed");
        failed.ShouldBeTrue();
        w.Clock.Advance(TimeSpan.FromHours(2));
        var second = await service.TryRetireAsync(release, null, CancellationToken.None);

        second.Residue.ShouldBe("cleanup_command_slot_spent");
        (await w.AttemptsAsync(a)).Count.ShouldBe(1, "no attempt 2");
        Directory.Exists(a.Path).ShouldBeTrue();
    }

    /// <summary>
    /// Review 1dcb8d90 D-2 (T-D-7/G-9/G-58): a held backlog costs at most the cap in pre-checks per tick. A missing
    /// directory is settled once (one row, no git, never re-checked). A held tree backs off (60 min doubling) and the
    /// next tick pre-checks the next never-checked trees, so an eligible tree behind six held ones (cap 2) is reached
    /// on tick 4 and no tick writes more than two rows per cap. The pause follows only the one real removal, and once
    /// the cap no longer binds, a tree inside its doubled backoff is still not pre-checked.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Held_backlog_is_bounded_per_tick_and_reaches_every_candidate()
    {
        var hook = new PauseHook(TimeSpan.FromSeconds(5));
        await using var w = await DesktopLegacyWorld.CreateAsync(s => { s.MaxReleasesPerTick = 2; s.PauseBetweenSeconds = 5; },
            services => services.AddSingleton<TimeProvider>(hook));
        hook.Inner = w.Clock;
        var pauses = 0;
        hook.OnPause = () => pauses++;
        var absent = new List<DesktopLegacyWorld.Handle>();
        for (var i = 0; i < 1000; i++) absent.Add(await w.AddTaskAsync(shape: "none", ageHours: 24 * 30 + i));
        var absentIds = absent.Select(t => (Guid?)t.Id).ToHashSet();
        // dirty[0] is the oldest held tree; the eligible tree is the newest candidate of all.
        var dirty = new List<DesktopLegacyWorld.Handle>();
        for (var i = 0; i < 6; i++)
        {
            var d = await w.AddTaskAsync(ageHours: 24 * 10 - i);
            await File.WriteAllTextAsync(Path.Combine(d.Path, "dirty.txt"), "dirty\n");
            dirty.Add(d);
        }
        var eligible = await w.AddTaskAsync(ageHours: 49);
        var origin = await w.OriginRefsAsync();
        var snapshots = new Dictionary<Guid, string>();
        foreach (var d in dirty) snapshots[d.Id] = await w.SnapshotAsync(d.Path);
        var reached = 0;

        for (var tick = 1; tick <= 4; tick++)
        {
            w.ClearTrace();
            var result = await w.TickAsync();
            var rows = await w.RowsAsync(result.RunId);
            var prechecked = dirty.Where(d => w.Fixture.Git.Commands.Any(c => c.Arguments.Length > 0 && c.Arguments[0] == "status"
                && LandingGit.PathsEqual(c.Directory, d.Path))).ToList();
            prechecked.Count.ShouldBeLessThanOrEqualTo(2, $"tick {tick}: at most the cap in pre-checks");
            if (tick <= 3)
                prechecked.ShouldBe(dirty.Skip(2 * (tick - 1)).Take(2).ToList(), $"tick {tick} continues where the last one stopped");
            else
                prechecked.ShouldBe([dirty[0]], "tick 4: the eligible tree first, then the least recently checked held tree");
            foreach (var t in absent) w.AssertNoGit(t);
            rows.Count(r => absentIds.Contains(r.TaskId)).ShouldBe(tick == 1 ? 1000 : 0, $"tick {tick}: a missing directory is recorded once");
            rows.Where(r => absentIds.Contains(r.TaskId)).ShouldAllBe(r => r.ReasonCode == "directory_absent");
            if (tick > 1) rows.Count.ShouldBeLessThanOrEqualTo(4, $"tick {tick}: no unbounded row growth (2 x cap)");
            if (reached == 0 && !Directory.Exists(eligible.Path)) reached = tick;
            w.Clock.Advance(TimeSpan.FromMinutes(61));
        }

        reached.ShouldBe(4, "ceil(6 held / cap 2) + 1 ticks");
        pauses.ShouldBe(1, "one pause: after the removal on tick 4, never after a hold");

        // Tick 5, 61 min after tick 4, cap no longer binding: dirty[0] (held again on tick 4) is inside its doubled
        // 120 min backoff; the other five are due and pre-checked once each.
        w.Settings.Value.MaxReleasesPerTick = 25;
        w.ClearTrace();
        var late = await w.TickAsync();
        dirty.Where(d => w.Fixture.Git.Commands.Any(c => c.Arguments.Length > 0 && c.Arguments[0] == "status"
                && LandingGit.PathsEqual(c.Directory, d.Path))).ToList()
            .ShouldBe(dirty.Skip(1).ToList(), "a held tree inside its backoff is not pre-checked");
        var lateRows = await w.RowsAsync(late.RunId);
        lateRows.ShouldNotContain(r => r.TaskId == dirty[0].Id, "an unchanged Held row is not rewritten");
        lateRows.Where(r => dirty.Skip(1).Select(d => (Guid?)d.Id).Contains(r.TaskId)).Select(r => r.ReasonCode)
            .ShouldBe(Enumerable.Repeat("source_dirty", 5));
        await using (var db = w.Db())
        {
            var key = DesktopLegacyReleasePolicyService.CheckCursorKey(dirty[0].Id);
            var cursor = await db.WorktreeResidueCandidateCursors.AsNoTracking().SingleAsync(c => c.CandidateKey == key);
            (cursor.NotBefore!.Value - cursor.LastEvaluatedAt).ShouldBe(TimeSpan.FromMinutes(120), "the second hold doubles 60 min");
        }
        await w.AssertRetiredAsync(eligible, origin);
        foreach (var d in dirty) await w.AssertIntactAsync(d, snapshots[d.Id]);
    }

    /// <summary>Review 1dcb8d90 A-2 (G-2): the kill switch flipped during the pause stops the tick before the next row.</summary>
    [Test]
    public async Task Switch_off_during_the_pause_stops_before_the_next_row()
    {
        var hook = new PauseHook(TimeSpan.FromSeconds(5));
        await using var w = await DesktopLegacyWorld.CreateAsync(s => s.PauseBetweenSeconds = 5,
            services => services.AddSingleton<TimeProvider>(hook));
        hook.Inner = w.Clock;
        var tasks = new List<DesktopLegacyWorld.Handle>();
        for (var i = 0; i < 3; i++) tasks.Add(await w.AddTaskAsync(ageHours: 60 - i));
        var origin = await w.OriginRefsAsync();
        var snapshots = new Dictionary<Guid, string>();
        foreach (var t in tasks) snapshots[t.Id] = await w.SnapshotAsync(t.Path);
        var paused = 0;
        hook.OnPause = () =>
        {
            paused++;
            w.Settings.Value = new WorktreeLegacyReleaseSettings
            {
                Enabled = false, Execute = true, PauseBetweenSeconds = 5, MinTerminalHours = 48, MaxReleasesPerTick = 25,
            };
        };

        var tick = await w.TickAsync();

        paused.ShouldBe(1, "one pause, after the first removal");
        await w.AssertRetiredAsync(tasks[0], origin);
        foreach (var t in tasks.Skip(1)) await w.AssertIntactAsync(t, snapshots[t.Id]);
        var rows = await w.RowsAsync(tick.RunId);
        rows.Single(r => r.Outcome == nameof(WorktreeResidueCandidateOutcome.Deferred)).ReasonCode.ShouldBe("tick_stopped_disabled");
        tick.Stopped.ShouldBe("tick_stopped_disabled");
    }

    /// <summary>Review 1dcb8d90 A-3 (V-21): a dry run reports a caller-filed release as held, never would_release.</summary>
    [Test]
    [Arguments("preview")]
    [Arguments("execute-false")]
    public async Task Dry_run_holds_a_caller_filed_release(string mode)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync(s => s.Execute = mode != "execute-false");
        var control = await w.AddTaskAsync(ageHours: 60);
        var a = await w.AddTaskAsync();
        await using (var scope = w.Host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == a.Id);
            await scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>().ReleaseAsync(a.Id,
                new ReleaseWorktreeRetirementRequest(task.ConcurrencyToken, (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim(),
                    null, true, "reviewed no further use"), Operator, CancellationToken.None);
        }

        var tick = await w.TickAsync(preview: mode == "preview");

        var rows = await w.RowsAsync(tick.RunId);
        rows.Single(r => r.TaskId == control.Id).ReasonCode.ShouldBe("would_release");
        var row = rows.Single(r => r.TaskId == a.Id);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe("release_active_other");
        (await w.ReleasesAsync(a)).Single().State.ShouldBe(WorktreeRetirementState.Released);
        Directory.Exists(a.Path).ShouldBeTrue();
    }

    /// <summary>
    /// CARD-0824 S4a survivor X-26: a clean, pushed tree whose HEAD is detached at its branch tip passes the policy
    /// pre-check (which never reads symbolic-ref), so the guarded removal's identity check is its only guard. Before or
    /// after filing, the removal refuses <c>detached_head</c> and the tree, registration and branch stay.
    /// </summary>
    [Test]
    [Arguments("before-filing")]
    [Arguments("after-filing")]
    public async Task Detached_head_tree_is_never_released(string when)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var control = await w.AddTaskAsync(ageHours: 50);
        var origin = await w.OriginRefsAsync();
        var head = (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim();
        var fired = false;
        if (when == "before-filing")
            await w.Fixture.RequiredAsync(a.Path, "checkout", "--detach");
        else
        {
            w.Host.RetirementCut.Cut = "release-after";
            w.Host.RetirementCut.Pause = async () =>
            {
                if (fired) return;
                fired = true;
                w.Host.RetirementCut.Cut = null;
                await w.Fixture.RequiredAsync(a.Path, "checkout", "--detach");
            };
        }

        var tick = await w.TickAsync();

        if (when == "after-filing") fired.ShouldBeTrue("the cut fired on the detached task's release save");
        await w.AssertRetiredAsync(control, origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
        row.ReasonCode.ShouldBe("detached_head");
        Directory.Exists(a.Path).ShouldBeTrue("the directory is present");
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue("the registration is present");
        (await w.BranchExistsAsync(a)).ShouldBeTrue("the branch is present");
        (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim().ShouldBe(head);
        (await w.ReadAsync(a.Path, "rev-parse", "--abbrev-ref", "HEAD")).Trim().ShouldBe("HEAD", "HEAD is still detached");
        (await w.ReleasesAsync(a)).Single().State.ShouldNotBe(WorktreeRetirementState.Complete);
    }

    /// <summary>
    /// CARD-0824 S4a survivor M-2 (I-8): a server fact that appears after the tick's selection, before the candidate's
    /// action, holds the tree. Of these facts only the pre-act re-read sees them (the service re-checks none at filing).
    /// </summary>
    [Test]
    [Arguments("late-sync-debt", "sync_debt")]
    [Arguments("late-dependent", "dependent_open")]
    [Arguments("late-path-shared", "path_shared")]
    [Arguments("late-slot-lease", "slot_leased")]
    public async Task Fact_that_appears_after_selection_holds_the_tree(string fact, string reason)
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        // The control is older, so it acts first; its row is the moment between a's selection and a's action.
        var control = await w.AddTaskAsync(ageHours: 60);
        var a = await w.AddTaskAsync(ageHours: 49);
        var origin = await w.OriginRefsAsync();
        var before = await w.SnapshotAsync(a.Path);
        var seeded = false;

        var tick = await w.TickAsync(onRow: id =>
        {
            if (seeded || id != control.Id) return;
            seeded = true;
            using var db = w.Db();
            switch (fact)
            {
                case "late-sync-debt":
                    db.AgentTaskSyncDebts.Add(new AgentTaskSyncDebt
                    {
                        Id = Guid.NewGuid(), TaskId = a.Id, Attempt = 1, SettlementEventId = Guid.NewGuid(),
                        State = AgentTaskSyncDebtState.Pending, ReasonCode = "runner_sync_lease_busy", CreatedAt = w.Now, UpdatedAt = w.Now,
                    });
                    break;
                case "late-dependent":
                    db.AgentTasks.Add(Bare(Guid.NewGuid(), w.Now, t => { t.Status = AgentTaskStatus.Queued; t.WorktreeBaseRequestedRef = a.Branch; }));
                    break;
                case "late-path-shared":
                    db.AgentTasks.Add(Bare(Guid.NewGuid(), w.Now, t => { t.Status = AgentTaskStatus.Working; t.WorktreePath = a.Path; }));
                    break;
                default:
                    var task = db.AgentTasks.Single(t => t.Id == a.Id);
                    task.RemoteWorkspaceSlotId = "ws-03";
                    task.RemoteWorkspaceSlotGeneration = 1;
                    break;
            }
            db.SaveChanges();
        });

        seeded.ShouldBeTrue("the fact was seeded after the control's row");
        await w.AssertRetiredAsync(control, origin);
        var row = await w.RowAsync(tick.RunId, a);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe(reason);
        await w.AssertIntactAsync(a, before);
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
    }

    /// <summary>
    /// CARD-0824 S4a survivor PC-58 (G-58): backed-off rows consume no cap. Two held trees back off on tick 1 (cap 2);
    /// on tick 2, still inside their backoff, the eligible tree behind them is pre-checked and removed.
    /// </summary>
    [Test]
    public async Task Backed_off_rows_ahead_of_an_eligible_tree_consume_no_cap()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync(s => s.MaxReleasesPerTick = 2);
        var dirty = new List<DesktopLegacyWorld.Handle>();
        for (var i = 0; i < 2; i++)
        {
            var d = await w.AddTaskAsync(ageHours: 70 - i);
            await File.WriteAllTextAsync(Path.Combine(d.Path, "dirty.txt"), "dirty\n");
            dirty.Add(d);
        }
        var eligible = await w.AddTaskAsync(ageHours: 49);
        var origin = await w.OriginRefsAsync();

        var first = await w.TickAsync();
        foreach (var d in dirty) (await w.RowAsync(first.RunId, d)).ReasonCode.ShouldBe("source_dirty");
        (await w.RowAsync(first.RunId, eligible)).ReasonCode.ShouldBe("over_cap");
        Directory.Exists(eligible.Path).ShouldBeTrue();

        w.Clock.Advance(TimeSpan.FromMinutes(5));
        var second = await w.TickAsync();

        second.BackedOff.ShouldBe(2, "as many backed-off rows as the cap");
        await w.AssertRetiredAsync(eligible, origin);
        (await w.RowsAsync(second.RunId)).ShouldNotContain(r => r.ReasonCode == "over_cap");
    }

    /// <summary>
    /// CARD-0824 S4a survivor M-5 (T-D-7): due re-checks go least recently checked first. With cap 1, A is checked on
    /// tick 1 and B on tick 2; on tick 3 both are due and A, checked longest ago (and now clean), goes first.
    /// </summary>
    [Test]
    public async Task Due_rechecks_go_least_recently_checked_first()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync(s => s.MaxReleasesPerTick = 1);
        var a = await w.AddTaskAsync(ageHours: 70);
        var b = await w.AddTaskAsync(ageHours: 60);
        foreach (var d in new[] { a, b }) await File.WriteAllTextAsync(Path.Combine(d.Path, "dirty.txt"), "dirty\n");
        var origin = await w.OriginRefsAsync();

        var t1 = await w.TickAsync();
        (await w.RowAsync(t1.RunId, a)).ReasonCode.ShouldBe("source_dirty");
        (await w.RowAsync(t1.RunId, b)).ReasonCode.ShouldBe("over_cap");
        w.Clock.Advance(TimeSpan.FromMinutes(10));
        var t2 = await w.TickAsync();
        (await w.RowAsync(t2.RunId, b)).ReasonCode.ShouldBe("source_dirty", "tick 2 checks B (A is backed off)");

        File.Delete(Path.Combine(a.Path, "dirty.txt"));
        var bBefore = await w.SnapshotAsync(b.Path);
        w.Clock.Advance(TimeSpan.FromMinutes(61));
        w.ClearTrace();
        var t3 = await w.TickAsync();

        await w.AssertRetiredAsync(a, origin);
        (await w.RowAsync(t3.RunId, b)).ReasonCode.ShouldBe("over_cap");
        w.Fixture.Git.Commands.ShouldNotContain(c => c.Arguments.Length > 0 && c.Arguments[0] == "status"
            && LandingGit.PathsEqual(c.Directory, b.Path), "B, checked more recently, waits");
        (await w.SnapshotAsync(b.Path)).ShouldBe(bBefore);
    }

    /// <summary>
    /// CARD-0824 S4a survivor PC-52 (R-3/G-52): a caller-filed release whose command removed nothing (dirty at removal,
    /// no set-aside marker) is still never reopened: the second pass is cleanup_command_slot_spent with one attempt.
    /// </summary>
    [Test]
    public async Task Caller_filed_release_refused_with_nothing_moved_is_not_reopened()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(shape: "base-only");
        await using var scope = w.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<TaskWorktreeRetirementService>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == a.Id);
        var dto = await service.ReleaseAsync(a.Id, new ReleaseWorktreeRetirementRequest(task.ConcurrencyToken,
            (await w.ReadAsync(a.Path, "rev-parse", "HEAD")).Trim(), null, true, "reviewed no further use"), Operator, CancellationToken.None);
        var release = await db.TaskWorktreeRetirements.SingleAsync(r => r.Id == dto.Id);
        release.CallerIdentity.ShouldBe("operator");
        var dirt = Path.Combine(a.Path, "dirty.txt");
        await File.WriteAllTextAsync(dirt, "dirty\n");

        var first = await service.TryRetireAsync(release, null, CancellationToken.None);
        first.Residue.ShouldBe("source_dirty");
        var common = await w.Fixture.Git.CommonDirectoryAsync(w.Fixture.Repository, CancellationToken.None);
        WorktreeSetAside.Read(common, a.Path).ShouldBeNull("nothing was moved aside");
        File.Delete(dirt);
        w.Clock.Advance(TimeSpan.FromHours(2));
        var second = await service.TryRetireAsync(release, null, CancellationToken.None);

        second.Residue.ShouldBe("cleanup_command_slot_spent");
        (await w.AttemptsAsync(a)).Count.ShouldBe(1, "no attempt 2");
        Directory.Exists(a.Path).ShouldBeTrue();
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue();
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
    }

    /// <summary>
    /// CARD-0824 S4a T-D-4 retry: a reopened attempt skips the policy pre-check and relies on the guarded removal alone.
    /// Attempt 1 is refused (a tracked change after filing); the change is undone and the tree dirtied again with an
    /// untracked file; the due retry is Refused source_dirty by the removal, and the tree, registration and branch stay.
    /// </summary>
    [Test]
    public async Task Reopened_attempt_re_proves_a_tree_dirtied_after_the_refusal()
    {
        await using var w = await DesktopLegacyWorld.CreateAsync();
        var a = await w.AddTaskAsync(ageHours: 60);
        var origin = await w.OriginRefsAsync();
        var tracked = a.Id8 + ".txt";
        var fired = false;
        w.Host.RetirementCut.Cut = "release-after";
        w.Host.RetirementCut.Pause = async () =>
        {
            if (fired) return;
            fired = true;
            w.Host.RetirementCut.Cut = null;
            await File.WriteAllTextAsync(Path.Combine(a.Path, tracked), "changed\n");
        };

        var t1 = await w.TickAsync();
        fired.ShouldBeTrue();
        var row1 = await w.RowAsync(t1.RunId, a);
        row1.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused));
        row1.ReasonCode.ShouldBe("source_dirty");

        await w.Fixture.RequiredAsync(a.Path, "checkout", "--", tracked);
        await File.WriteAllTextAsync(Path.Combine(a.Path, "later.txt"), "later\n");
        var before = await w.SnapshotAsync(a.Path);
        w.Clock.Advance(TimeSpan.FromMinutes(61));
        var t2 = await w.TickAsync();

        var row2 = await w.RowAsync(t2.RunId, a);
        row2.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Refused), "the reopened attempt reached the guarded removal");
        row2.ReasonCode.ShouldBe("source_dirty");
        (await w.AttemptsAsync(a)).Count.ShouldBe(2, "the retry reopened the attempt");
        (await w.SnapshotAsync(a.Path)).ShouldBe(before, "the tree is byte-intact");
        (await w.RegisteredAsync(a.Path)).ShouldBeTrue();
        (await w.BranchExistsAsync(a)).ShouldBeTrue();
        (await w.OriginRefsAsync()).ShouldBe(origin);
    }

    /// <summary>A-2: the 5 s pause flips the settings at the moment it starts and then elapses at once.</summary>
    private sealed class PauseHook(TimeSpan pause) : TimeProvider
    {
        public TimeProvider Inner { get; set; } = TimeProvider.System;
        public Action? OnPause { get; set; }
        public override DateTimeOffset GetUtcNow() => Inner.GetUtcNow();
        public override long GetTimestamp() => Inner.GetTimestamp();
        public override long TimestampFrequency => Inner.TimestampFrequency;
        public override TimeZoneInfo LocalTimeZone => Inner.LocalTimeZone;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime != pause || OnPause is null) return Inner.CreateTimer(callback, state, dueTime, period);
            OnPause();
            return TimeProvider.System.CreateTimer(callback, state, TimeSpan.Zero, period);
        }
    }

    private static AgentTask Bare(Guid id, DateTime now, Action<AgentTask>? seed = null)
    {
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "C0824 S4a other", Goal = "fixture", Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree, Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, CompletedAt = now,
        };
        seed?.Invoke(task);
        return task;
    }

    private static AgentTaskLanding Landing(Guid id, Guid taskId, DateTime now, bool active) => new()
    {
        Id = id, TaskId = taskId, SchemaVersion = 1, Active = active,
        RepositoryPath = "/c0824/repo", WorktreePath = "/c0824/trees/x", CommonDirectory = "/c0824/repo",
        GitDirectory = "/c0824/repo/.git", SourceFullRef = "refs/heads/feat/x", TargetFullRef = "refs/heads/master",
        OriginalSourceSha = new string('a', 40), TargetBeforeSha = new string('b', 40),
        RecoveryRefPrefix = $"refs/antiphon/land/{taskId:N}/{id:N}", CreatedAt = now, UpdatedAt = now,
    };

    /// <summary>V-24 "throws": the insert of one task's release fails.</summary>
    private sealed class ThrowOnRelease(Guid taskId) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context?.ChangeTracker.Entries<TaskWorktreeRetirement>()
                    .Any(e => e.State == EntityState.Added && e.Entity.TaskId == taskId) == true)
                throw new DbUpdateException("c0824 injected release insert failure");
            return ValueTask.FromResult(result);
        }
    }
}
