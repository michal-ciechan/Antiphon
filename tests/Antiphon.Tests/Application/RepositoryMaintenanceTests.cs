using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1450 (CARD-1432 F-5): the maintenance ref sweep uses the S2 retained-operation rule
/// and runs its update-ref batch under the child journal.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RepositoryMaintenanceTests
{
    /// <summary>An unprotected terminal task keeps the recovery pins of its latest operation and of
    /// every Refused operation. A protected terminal task loses only observations. A missing task's
    /// refs go. A task that is not terminal, another namespace, and refs/heads stay.</summary>
    [Test]
    [Timeout(60_000)]
    public async Task C1450_sweep_keeps_latest_and_refused_operation_pins()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow;
        var open = SeedTask(db, Guid.NewGuid(), AgentTaskStatus.Queued, fixture.Repository);
        var terminal = SeedTask(db, fixture.TaskId, AgentTaskStatus.Succeeded, fixture.Repository);
        var protectedTask = SeedTask(db, Guid.NewGuid(), AgentTaskStatus.Succeeded, fixture.Repository);
        var missing = Guid.NewGuid();
        var refused = Landing(terminal.Id, LandPhase.Refused, active: false);
        refused.CreatedAt = now.AddMinutes(-30);
        var superseded = Landing(terminal.Id, LandPhase.Complete, active: false);
        superseded.CreatedAt = now.AddMinutes(-20);
        var latest = Landing(terminal.Id, LandPhase.Complete);
        latest.CreatedAt = now;
        terminal.ActiveLandingId = latest.Id;
        db.AgentTaskLandings.AddRange(refused, superseded, latest);
        db.AgentTaskSyncDebts.Add(Debt(protectedTask.Id, AgentTaskSyncDebtState.Pending));
        await db.SaveChangesAsync();

        string Prefix(Guid taskId, Guid operation) => $"refs/antiphon/land/{taskId:N}/{operation:N}/";
        var latestPrefix = Prefix(terminal.Id, latest.Id);
        var refusedPrefix = Prefix(terminal.Id, refused.Id);
        var protectedOp = Guid.NewGuid();
        string[] kept =
        [
            latestPrefix + "source", latestPrefix + "target-before", latestPrefix + "prepared",
            refusedPrefix + "source", refusedPrefix + "target-before", refusedPrefix + "prepared",
            Prefix(protectedTask.Id, protectedOp) + "source",
            Prefix(protectedTask.Id, protectedOp) + "target-before",
            Prefix(protectedTask.Id, protectedOp) + "prepared",
            $"refs/antiphon/progress/{protectedTask.Id:N}/settlement-sync",
            $"refs/antiphon/land/{open.Id:N}/{Guid.NewGuid():N}/source",
            $"refs/antiphon/land/{open.Id:N}/{Guid.NewGuid():N}/remote-observed/{Guid.NewGuid():N}",
            $"refs/antiphon/progress/{open.Id:N}/baseline-1",
            $"refs/antiphon/retirement/{Guid.NewGuid():N}/source",
            "refs/heads/feat/c1450-keep",
        ];
        string[] dropped =
        [
            latestPrefix + "remote-observed/" + Guid.NewGuid().ToString("N"),
            refusedPrefix + "cleanup-observed/" + Guid.NewGuid().ToString("N"),
            Prefix(terminal.Id, superseded.Id) + "source",
            $"refs/antiphon/land/{terminal.Id:N}/{Guid.NewGuid():N}/adopt/source",
            $"refs/antiphon/land/{terminal.Id:N}/{Guid.NewGuid():N}/mirror-observed",
            $"refs/antiphon/progress/{terminal.Id:N}/settlement-sync",
            $"refs/antiphon/progress/{terminal.Id:N}/baseline-1",
            Prefix(protectedTask.Id, protectedOp) + "remote-observed/" + Guid.NewGuid().ToString("N"),
            $"refs/antiphon/land/{missing:N}/{Guid.NewGuid():N}/source",
            $"refs/antiphon/progress/{missing:N}/baseline-1",
        ];
        foreach (var name in kept.Concat(dropped))
            await fixture.RequiredAsync(fixture.Repository, "update-ref", name, fixture.SeedSha);

        var deleted = await new RepositoryMaintenanceService(db, fixture.Git, NullLogger.Instance)
            .SweepRefsAsync(fixture.Repository, CancellationToken.None);

        deleted.ShouldNotBeNull().Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(dropped.Order(StringComparer.Ordinal).ToArray());
        (await ListAsync(fixture, "refs/antiphon/")).Concat(await ListAsync(fixture, "refs/heads/feat/c1450-keep"))
            .Order(StringComparer.Ordinal).ToArray()
            .ShouldBe(kept.Order(StringComparer.Ordinal).ToArray());
        fixture.Git.Trace.Count(a => a is ["update-ref", "--stdin"]).ShouldBe(1, "one transaction");
    }

    /// <summary>The sweep's update-ref --stdin batch is a mutating child: a journal record names its
    /// process while the reference-transaction hook holds the batch, and the record is gone after exit.</summary>
    [Test]
    [Timeout(60_000)]
    public async Task C1450_sweep_update_ref_batch_runs_under_the_child_journal(CancellationToken ct)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = SeedTask(db, fixture.TaskId, AgentTaskStatus.Succeeded, fixture.Repository);
        await db.SaveChangesAsync();
        var pin = $"refs/antiphon/land/{task.Id:N}/{Guid.NewGuid():N}/source";
        await fixture.RequiredAsync(fixture.Repository, "update-ref", pin, fixture.SeedSha);
        var hooks = Path.Combine(fixture.Repository, ".git", "hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "reference-transaction");
        await File.WriteAllTextAsync(hook, "#!/bin/sh\n[ \"$1\" = prepared ] && sleep 8\nexit 0\n", ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        fixture.Git.HooksPathOverride = hooks;

        var common = await fixture.Git.CommonDirectoryAsync(fixture.Repository, ct);
        var children = Path.Combine(common, "antiphon", "children");
        var sweeping = new RepositoryMaintenanceService(db, fixture.Git, NullLogger.Instance)
            .SweepRefsAsync(fixture.Repository, ct);

        string? path = null;
        RepositoryChildJournal.ChildRecord? record = null;
        using (var poll = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            poll.CancelAfter(TimeSpan.FromSeconds(15));
            while (record?.ProcessId is null)
            {
                if (Directory.Exists(children))
                    path = Directory.EnumerateFiles(children, "*.json").FirstOrDefault();
                if (path is not null)
                    record = await RepositoryChildJournalReader.ReadAsync(path, ct);
                if (record?.ProcessId is not null) break;
                if (sweeping.IsCompleted) break;
                try { await Task.Delay(50, poll.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
            }
        }

        record.ShouldNotBeNull("a fencing record exists while the batch runs");
        record.ProcessId.ShouldNotBeNull("the started child is recorded");
        record.CommonDirectory.ShouldBe(common);
        record.Purpose.ShouldBeNull("the batch is the untagged journal, like every other update-ref");
        var deleted = await sweeping;
        deleted.ShouldNotBeNull().ShouldBe([pin]);
        File.Exists(path!).ShouldBeFalse("the owner deletes its record after the exact handle exited");
        Directory.EnumerateFiles(children).ShouldBeEmpty();
        var gone = await fixture.Git.RunAsync(fixture.Repository, ["show-ref", "--verify", pin], ct);
        gone.Succeeded.ShouldBeFalse("the batch applied");
    }

    private static async Task<string[]> ListAsync(LandingGitFixture fixture, string prefix) =>
        (await fixture.RequiredAsync(fixture.Repository, "for-each-ref", "--format=%(refname)", prefix))
        .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static AgentTask SeedTask(AppDbContext db, Guid id, AgentTaskStatus status, string repository)
    {
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = "C1450", Goal = "C1450",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repository, RepoPath = repository, Status = status,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
            CompletedAt = status is AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled ? DateTime.UtcNow : null,
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static AgentTaskLanding Landing(Guid taskId, LandPhase phase, bool active = true) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, SchemaVersion = 3, Active = active, Phase = phase,
        Publication = LandPublicationOutcome.Unconfirmed, RepositoryPath = "/repo", WorktreePath = "/trees/x",
        CommonDirectory = "/repo/.git", GitDirectory = "/repo/.git", SourceFullRef = "refs/heads/feat/x",
        TargetFullRef = "refs/heads/master", DestinationFullRef = "refs/heads/master",
        OriginalSourceSha = new string('a', 40), TargetBeforeSha = new string('b', 40),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static AgentTaskSyncDebt Debt(Guid taskId, AgentTaskSyncDebtState state) => new()
    {
        Id = Guid.NewGuid(), TaskId = taskId, Attempt = 1, SettlementEventId = Guid.NewGuid(), State = state,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };
}
