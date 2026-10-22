using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1432 S2 (R2, V-3..V-5): a land deletes its task's land and progress refs at the source.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandRefRetentionTests
{
    /// <summary>V-3 (Review 9e126982 D1): a completed land of an unprotected task deletes its observation pins,
    /// its progress pins and the subtrees of superseded operations, and keeps the recovery pins of the
    /// operation that just landed (CARD-0448 V-26).</summary>
    [Test]
    public async Task C1432_completed_land_deletes_task_land_and_progress_refs()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var task = h.Git.TaskId;
        var other = Guid.NewGuid();
        var sha = h.Git.SeedSha;
        var superseded = $"refs/antiphon/land/{task:N}/{Guid.NewGuid():N}/"; // an operation the task has no row for
        string[] own =
        [
            $"refs/antiphon/land/{task:N}/{Guid.NewGuid():N}/source-observed/{Guid.NewGuid():N}", // request-id subtree
            $"refs/antiphon/land/{task:N}/{Guid.NewGuid():N}/mirror-observed", // mirror subtree
            superseded + "source", superseded + "target-before", superseded + "prepared",
            $"refs/antiphon/progress/{task:N}/baseline-1",
            $"refs/antiphon/progress/{task:N}/observe-2",
        ];
        string[] foreign =
        [
            $"refs/antiphon/land/{other:N}/{Guid.NewGuid():N}/source",
            $"refs/antiphon/progress/{other:N}/baseline-1",
            "refs/heads/feat/c1432-keep",
        ];
        foreach (var name in own.Concat(foreign)) h.Git.SetPin(name, sha);
        List<string>? beforeDelete = null;
        h.Git.BeforeCommand = async (_, args) =>
        {
            if (args is ["update-ref", "--stdin"]) beforeDelete = h.Git.Refs.Keys.ToList();
            await Task.CompletedTask;
            return null;
        };

        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        var prefix = op.RecoveryRefPrefix + "/";
        prefix.ShouldStartWith($"refs/antiphon/land/{task:N}/");
        beforeDelete.ShouldNotBeNull("the land ran its one deletion batch");
        var remaining = h.Git.Refs.Keys.Where(k => k.StartsWith($"refs/antiphon/land/{task:N}/", StringComparison.Ordinal)
                || k.StartsWith($"refs/antiphon/progress/{task:N}/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        var expected = beforeDelete.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && !LandRefRetention.IsObservation(k))
            .Order(StringComparer.Ordinal).ToArray();
        remaining.ShouldBe(expected, "only the landed operation's recovery pins survive a completed land of an unprotected task");
        remaining.ShouldContain(prefix + "source");
        remaining.ShouldContain(prefix + "target-before");
        remaining.ShouldContain(prefix + "prepared");
        foreach (var name in own) remaining.ShouldNotContain(name);
        foreach (var name in foreign) h.Git.Refs[name].ShouldBe(sha, "another task's refs are untouched");
    }

    /// <summary>Review 9e126982 D1: an unprotected task keeps the recovery pins of its latest operation and of
    /// every Refused operation (CARD-0448 V-26, the refused-retry pin rule). A superseded operation, a settled
    /// request's adopt pins, every observation and the progress pins go.</summary>
    [Test]
    public async Task C1432_unprotected_task_keeps_latest_and_refused_operation_pins()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = SeedTask(db, fixture.TaskId, AgentTaskStatus.Succeeded, fixture.Repository);
        var now = DateTime.UtcNow;
        var refused = Landing(task.Id, LandPhase.Refused, active: false);
        refused.CreatedAt = now.AddMinutes(-30);
        var superseded = Landing(task.Id, LandPhase.Complete, active: false);
        superseded.CreatedAt = now.AddMinutes(-20);
        var latest = Landing(task.Id, LandPhase.Complete);
        latest.CreatedAt = now;
        db.AgentTaskLandings.AddRange(refused, superseded, latest);
        db.AgentTaskLandRequests.Add(Request(task.Id, LandRequestState.Completed));
        await db.SaveChangesAsync();
        (await AntiphonRefProtection.QueryAsync(db, [task.Id], CancellationToken.None)).ShouldBeEmpty("the task is not protected");

        string Prefix(AgentTaskLanding o) => $"refs/antiphon/land/{task.Id:N}/{o.Id:N}/";
        var request = $"refs/antiphon/land/{task.Id:N}/{Guid.NewGuid():N}/";
        string[] kept =
        [
            Prefix(latest) + "source", Prefix(latest) + "target-before", Prefix(latest) + "prepared",
            Prefix(refused) + "source", Prefix(refused) + "target-before", Prefix(refused) + "prepared",
        ];
        string[] dropped =
        [
            Prefix(latest) + "remote-observed/" + Guid.NewGuid().ToString("N"),
            Prefix(refused) + "cleanup-observed/" + Guid.NewGuid().ToString("N"),
            Prefix(superseded) + "source", Prefix(superseded) + "target-before", Prefix(superseded) + "prepared",
            request + "adopt/source", request + "source-observed/" + Guid.NewGuid().ToString("N"),
            $"refs/antiphon/land/{task.Id:N}/{Guid.NewGuid():N}/mirror-observed",
            $"refs/antiphon/progress/{task.Id:N}/settlement-sync",
            $"refs/antiphon/progress/{task.Id:N}/baseline-1",
        ];
        foreach (var name in kept.Concat(dropped))
            await fixture.RequiredAsync(fixture.Repository, "update-ref", name, fixture.SeedSha);

        var deleted = await new LandRefRetention(db, fixture.Git, NullLogger.Instance)
            .ReleaseAsync(task, new HeldLease(fixture.Repository), CancellationToken.None);

        deleted.ShouldNotBeNull().Order(StringComparer.Ordinal).ToArray().ShouldBe(dropped.Order(StringComparer.Ordinal).ToArray());
        (await ListAsync(fixture, "refs/antiphon/")).ShouldBe(kept.Order(StringComparer.Ordinal).ToArray());
        fixture.Git.Trace.Count(a => a is ["update-ref", "--stdin"]).ShouldBe(1, "one transaction");
    }

    [Test]
    public async Task C1432_protected_task_keeps_recovery_pins_drops_observations()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = SeedTask(db, fixture.TaskId, AgentTaskStatus.Succeeded, fixture.Repository);
        db.AgentTaskSyncDebts.Add(Debt(task.Id, AgentTaskSyncDebtState.Pending));
        await db.SaveChangesAsync();
        var landing = $"refs/antiphon/land/{task.Id:N}/{Guid.NewGuid():N}/";
        var request = $"refs/antiphon/land/{task.Id:N}/{Guid.NewGuid():N}/";
        string[] kept =
        [
            landing + "source", landing + "target-before", landing + "prepared",
            request + "adopt/source", request + "adopt/local-before", request + "adopt/remote-before",
            $"refs/antiphon/progress/{task.Id:N}/settlement-sync",
            $"refs/antiphon/progress/{task.Id:N}/baseline-1",
        ];
        string[] dropped =
        [
            landing + "remote-observed/" + Guid.NewGuid().ToString("N"),
            landing + "cleanup-observed/" + Guid.NewGuid().ToString("N"),
            landing + "source-recheck/" + Guid.NewGuid().ToString("N"),
            request + "source-observed/" + Guid.NewGuid().ToString("N"),
            request + "recover-owner-observed/" + Guid.NewGuid().ToString("N"),
            $"refs/antiphon/land/{task.Id:N}/{Guid.NewGuid():N}/mirror-observed",
        ];
        foreach (var name in kept.Concat(dropped))
            await fixture.RequiredAsync(fixture.Repository, "update-ref", name, fixture.SeedSha);

        var deleted = await new LandRefRetention(db, fixture.Git, NullLogger.Instance)
            .ReleaseAsync(task, new HeldLease(fixture.Repository), CancellationToken.None);

        deleted.ShouldNotBeNull().Order(StringComparer.Ordinal).ToArray().ShouldBe(dropped.Order(StringComparer.Ordinal).ToArray());
        (await ListAsync(fixture, $"refs/antiphon/")).ShouldBe(kept.Order(StringComparer.Ordinal).ToArray());
        fixture.Git.Trace.Count(a => a is ["update-ref", "--stdin"]).ShouldBe(1, "one transaction");
    }

    [Test]
    [Arguments("task-queued", true)]
    [Arguments("task-dispatched", true)]
    [Arguments("task-working", true)]
    [Arguments("task-blocked", true)]
    [Arguments("task-succeeded", false)]
    [Arguments("task-failed", false)]
    [Arguments("task-canceled", false)]
    [Arguments("task-missing", false)]
    [Arguments("request-queued", true)]
    [Arguments("request-held", true)]
    [Arguments("request-running", true)]
    [Arguments("request-needs-resolution", true)]
    [Arguments("request-completed", false)]
    [Arguments("request-recovery-source", true)]
    [Arguments("request-completed-recovery-source", false)]
    [Arguments("adopt-witness", true)]
    [Arguments("adopt-resolved", false)]
    [Arguments("landing-interrupted", true)]
    [Arguments("landing-recovery-source", true)]
    [Arguments("landing-complete", false)]
    [Arguments("landing-refused", false)]
    [Arguments("landing-inactive", false)]
    [Arguments("debt-pending", true)]
    [Arguments("debt-ready", false)]
    [Arguments("debt-held", true)]
    [Arguments("debt-superseded", false)]
    [Arguments("retirement-released", true)]
    [Arguments("retirement-claimed", true)]
    [Arguments("retirement-command-started", true)]
    [Arguments("retirement-partial", true)]
    [Arguments("retirement-complete", false)]
    [Arguments("retirement-inactive", false)]
    public async Task C1432_protection_rule_rows(string clause, bool expected)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var status = clause switch
        {
            "task-queued" => AgentTaskStatus.Queued,
            "task-dispatched" => AgentTaskStatus.Dispatched,
            "task-working" => AgentTaskStatus.Working,
            "task-blocked" => AgentTaskStatus.Blocked,
            "task-failed" => AgentTaskStatus.Failed,
            "task-canceled" => AgentTaskStatus.Canceled,
            _ => AgentTaskStatus.Succeeded,
        };
        if (clause != "task-missing") SeedTask(db, id, status, "/repo");
        SeedTask(db, owner, AgentTaskStatus.Succeeded, "/repo");
        switch (clause)
        {
            case "request-queued": db.AgentTaskLandRequests.Add(Request(id, LandRequestState.Queued)); break;
            case "request-held": db.AgentTaskLandRequests.Add(Request(id, LandRequestState.Held)); break;
            case "request-running": db.AgentTaskLandRequests.Add(Request(id, LandRequestState.Running)); break;
            case "request-needs-resolution": db.AgentTaskLandRequests.Add(Request(id, LandRequestState.NeedsResolution)); break;
            case "request-completed": db.AgentTaskLandRequests.Add(Request(id, LandRequestState.Completed)); break;
            case "request-recovery-source":
                db.AgentTaskLandRequests.Add(Request(owner, LandRequestState.Queued, recoverySource: id)); break;
            case "request-completed-recovery-source":
                db.AgentTaskLandRequests.Add(Request(owner, LandRequestState.Completed, recoverySource: id)); break;
            case "adopt-witness":
                db.AgentTaskLandRequests.Add(Request(id, LandRequestState.Completed, LandSourceResolutionState.AdvanceStarted)); break;
            case "adopt-resolved":
            {
                var resolved = Request(id, LandRequestState.Completed, LandSourceResolutionState.AdvanceStarted);
                resolved.RecoveryAdoptedAt = DateTime.UtcNow;
                db.AgentTaskLandRequests.Add(resolved);
                break;
            }
            case "landing-interrupted": db.AgentTaskLandings.Add(Landing(id, LandPhase.PushStarted)); break;
            case "landing-recovery-source": db.AgentTaskLandings.Add(Landing(owner, LandPhase.CleanupStarted, recoverySource: id)); break;
            case "landing-complete": db.AgentTaskLandings.Add(Landing(id, LandPhase.Complete)); break;
            case "landing-refused": db.AgentTaskLandings.Add(Landing(id, LandPhase.Refused)); break;
            case "landing-inactive": db.AgentTaskLandings.Add(Landing(id, LandPhase.PushStarted, active: false)); break;
            case "debt-pending": db.AgentTaskSyncDebts.Add(Debt(id, AgentTaskSyncDebtState.Pending)); break;
            case "debt-ready": db.AgentTaskSyncDebts.Add(Debt(id, AgentTaskSyncDebtState.Ready)); break;
            case "debt-held": db.AgentTaskSyncDebts.Add(Debt(id, AgentTaskSyncDebtState.Held)); break;
            case "debt-superseded": db.AgentTaskSyncDebts.Add(Debt(id, AgentTaskSyncDebtState.Superseded)); break;
            case "retirement-released": db.TaskWorktreeRetirements.Add(Retirement(id, WorktreeRetirementState.Released)); break;
            case "retirement-claimed": db.TaskWorktreeRetirements.Add(Retirement(id, WorktreeRetirementState.Claimed)); break;
            case "retirement-command-started": db.TaskWorktreeRetirements.Add(Retirement(id, WorktreeRetirementState.CommandStarted)); break;
            case "retirement-partial": db.TaskWorktreeRetirements.Add(Retirement(id, WorktreeRetirementState.Partial)); break;
            case "retirement-complete": db.TaskWorktreeRetirements.Add(Retirement(id, WorktreeRetirementState.Complete)); break;
            case "retirement-inactive": db.TaskWorktreeRetirements.Add(Retirement(id, WorktreeRetirementState.Claimed, active: false)); break;
        }
        await db.SaveChangesAsync();

        (await AntiphonRefProtection.QueryAsync(db, [id], CancellationToken.None)).Contains(id).ShouldBe(expected, clause);
        (await AntiphonRefProtection.QueryAsync(db, null, CancellationToken.None)).Contains(id).ShouldBe(expected, clause + " (whole set)");
        // The owner is protected only by its own live request or interrupted landing; a filter for it never reports the other task.
        var ownerProtected = clause is "request-recovery-source" or "landing-recovery-source";
        (await AntiphonRefProtection.QueryAsync(db, [owner], CancellationToken.None))
            .ShouldBe(ownerProtected ? [owner] : [], clause + " (owner)");
    }

    [Test]
    public async Task C1432_update_refs_rejects_foreign_namespace()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var land = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source";
        await fixture.RequiredAsync(fixture.Repository, "update-ref", land, fixture.SeedSha);
        foreach (var foreign in new[] { fixture.SourceRef, "refs/antiphon/retirement/x/source", "refs/antiphon/landx/y", "refs/antiphon/land/../heads/master" })
        {
            var result = await fixture.Git.UpdateRefsAsync(fixture.Repository, [(land, fixture.SeedSha), (foreign, fixture.SeedSha)], CancellationToken.None);
            result.Diagnostic.ShouldBe("update_refs_foreign_namespace", foreign);
        }
        (await fixture.Git.UpdateRefsAsync(fixture.Repository, [(land, "HEAD")], CancellationToken.None))
            .Diagnostic.ShouldBe("update_refs_invalid_oid");
        fixture.Git.Trace.ShouldNotContain(a => a.Length == 2 && a[0] == "update-ref" && a[1] == "--stdin", "a refused batch never reaches git");
        (await ListAsync(fixture, "refs/antiphon/")).ShouldBe([land]);
        (await fixture.RequiredAsync(fixture.Repository, "rev-parse", fixture.SourceRef)).Trim().ShouldBe(fixture.SeedSha);
    }

    [Test]
    public async Task C1432_update_refs_old_value_mismatch_applies_nothing()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var other = (await fixture.PushIndependentAsync("c1432-other")).Trim();
        await fixture.RequiredAsync(fixture.Repository, "fetch", "origin", "master");
        var first = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source";
        var second = $"refs/antiphon/progress/{fixture.TaskId:N}/baseline-1";
        await fixture.RequiredAsync(fixture.Repository, "update-ref", first, fixture.SeedSha);
        await fixture.RequiredAsync(fixture.Repository, "update-ref", second, fixture.SeedSha);
        await fixture.RequiredAsync(fixture.Repository, "pack-refs", "--all");

        var stale = await fixture.Git.UpdateRefsAsync(fixture.Repository,
            [(first, fixture.SeedSha), (second, other)], CancellationToken.None);
        stale.Succeeded.ShouldBeFalse("one stale old value refuses the transaction");
        (await ListAsync(fixture, "refs/antiphon/")).ShouldBe([first, second], "nothing applied");

        var applied = await fixture.Git.UpdateRefsAsync(fixture.Repository,
            [(first, fixture.SeedSha), (second, fixture.SeedSha)], CancellationToken.None);
        applied.Succeeded.ShouldBeTrue(applied.Diagnostic);
        (await ListAsync(fixture, "refs/antiphon/")).ShouldBeEmpty("the BOM-free batch deletes packed refs");
    }

    private static async Task<string[]> ListAsync(LandingGitFixture fixture, string prefix) =>
        (await fixture.RequiredAsync(fixture.Repository, "for-each-ref", "--format=%(refname)", prefix))
        .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();

    private static AgentTask SeedTask(AppDbContext db, Guid id, AgentTaskStatus status, string repository)
    {
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "C1432", Goal = "C1432",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repository, RepoPath = repository, Status = status,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
            CompletedAt = status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled ? DateTime.UtcNow : null,
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static AgentTaskLandRequest Request(Guid taskId, LandRequestState state,
        LandSourceResolutionState resolution = LandSourceResolutionState.None, Guid? recoverySource = null) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, RequestedAt = DateTime.UtcNow, LastEvaluatedAt = DateTime.UtcNow,
        LastProgressAt = DateTime.UtcNow, State = state,
        IsPending = state is LandRequestState.Queued or LandRequestState.Held or LandRequestState.Running or LandRequestState.NeedsResolution,
        ExpectedSourceSha = new string('a', 40), SourceResolutionState = resolution, RecoverySourceTaskId = recoverySource,
    };

    private static AgentTaskLanding Landing(Guid taskId, LandPhase phase, bool active = true, Guid? recoverySource = null) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, SchemaVersion = 3, Active = active, Phase = phase,
        Publication = LandPublicationOutcome.Unconfirmed, RepositoryPath = "/repo", WorktreePath = "/trees/x",
        CommonDirectory = "/repo/.git", GitDirectory = "/repo/.git", SourceFullRef = "refs/heads/feat/x",
        TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
        OriginalSourceSha = new string('a', 40), TargetBeforeSha = new string('b', 40),
        RecoverySourceTaskId = recoverySource, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static AgentTaskSyncDebt Debt(Guid taskId, AgentTaskSyncDebtState state) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, Attempt = 1, SettlementEventId = Guid.NewGuid(), State = state,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static TaskWorktreeRetirement Retirement(Guid taskId, WorktreeRetirementState state, bool active = true) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, TaskAttempt = 1, TerminalStatus = AgentTaskStatus.Succeeded,
        TaskCompletedAt = DateTime.UtcNow, ReleasedTaskRevision = Guid.NewGuid(), CallerIdentity = "op",
        ReleaseReason = "x", ReleasedAt = DateTime.UtcNow, RepositoryPath = "/repo", CommonDirectory = "/repo/.git",
        WorktreePath = "/trees/x", GitDirectory = "/repo/.git", SourceFullRef = "refs/heads/feat/x",
        SourceSha = new string('a', 40), TargetFullRef = "refs/heads/master", State = state, Active = active,
        UpdatedAt = DateTime.UtcNow,
    };

    private sealed class HeldLease(string common) : RepositoryLease
    {
        public override string CommonDirectory => common;
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
