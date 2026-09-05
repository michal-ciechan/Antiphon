using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0328 S3: first-match-wins residue labels, plus one real-git execute gate.
/// </summary>
[Category("Integration")]
public sealed class WorktreeResidueSweepTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly WorktreeResidueSettings Settings = new() { MinSettledMinutes = 120 };

    [Test]
    public void classify_first_match_wins_for_each_label()
    {
        var unknown = Fact("aaaaaaaa", task: null, ahead: 3, tracked: true);
        var live = Fact("bbbbbbbb", TaskSnap("bbbbbbbb", AgentTaskStatus.Working, completed: null),
            ancestor: false, ahead: 5, tracked: true);
        var settling = Fact("cccccccc", TaskSnap("cccccccc", AgentTaskStatus.Succeeded, Now.AddMinutes(-30),
            AgentTaskEventType.Landed));
        var unmerged = Fact("dddddddd", TaskSnap("dddddddd", AgentTaskStatus.Succeeded, Now.AddHours(-3),
            AgentTaskEventType.Landed), ancestor: false, ahead: 2);
        var dirty = Fact("eeeeeeee", TaskSnap("eeeeeeee", AgentTaskStatus.Failed, Now.AddHours(-3)),
            untracked: true);
        var eligible = Fact("ffffffff", TaskSnap("ffffffff", AgentTaskStatus.Succeeded, Now.AddHours(-3),
            AgentTaskEventType.Landed));

        var rows = WorktreeResidueSweepService.Classify(
            [unknown, live, settling, unmerged, dirty, eligible], Settings, Now);

        rows.Select(r => r.DisplayLabel).ShouldBe([
            "Unknown",
            "Live",
            "Settling",
            "Unmerged (2 ahead)",
            "Dirty",
            "Eligible"
        ]);
        rows.Select(r => r.Keep).ShouldBe([true, true, true, true, true, false]);
        rows[1].Detail.ShouldContain("Working");
        rows[3].Detail.ShouldContain("2 commit(s) not on master");
    }

    [Test]
    public void classify_landed_succeeded_ignores_untracked_only()
    {
        var facts = Fact(
            "aaaaaaaa",
            TaskSnap("aaaaaaaa", AgentTaskStatus.Succeeded, Now.AddHours(-3), AgentTaskEventType.Landed),
            untracked: true);

        var row = WorktreeResidueSweepService.ClassifyOne(facts, Settings, Now);
        row.Label.ShouldBe(WorktreeResidueLabel.Eligible);
        row.Keep.ShouldBeFalse();
    }

    [Test]
    public void classify_failed_untracked_is_dirty()
    {
        var facts = Fact(
            "aaaaaaaa",
            TaskSnap("aaaaaaaa", AgentTaskStatus.Failed, Now.AddHours(-3)),
            untracked: true);

        var row = WorktreeResidueSweepService.ClassifyOne(facts, Settings, Now);
        row.Label.ShouldBe(WorktreeResidueLabel.Dirty);
        row.Keep.ShouldBeTrue();
    }

    [Test]
    public void validator_accepts_defaults_and_rejects_empty_cron()
    {
        var validator = new WorktreeResidueSettingsValidator();
        validator.Validate(null, new WorktreeResidueSettings()).Succeeded.ShouldBeTrue();

        var bad = new WorktreeResidueSettings { Cron = "hourly" };
        var result = validator.Validate(null, bad);
        result.Succeeded.ShouldBeFalse();
        (result.Failures ?? []).ShouldContain(f => f.Contains("five-field", StringComparison.Ordinal));
    }

    [Test]
    [Category("Integration")]
    public async Task execute_false_touches_nothing_and_execute_true_removes_only_the_eligible_row()
    {
        using var repo = new ScratchGitRepo("wt-residue");
        await repo.CommitFileAsync("README.md", "base\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

        var gitSettings = new GitSettings
        {
            WorktreeBasePath = repo.WorktreeRoot,
            DefaultBranch = "master",
            WorktreeStaleAfterDays = 7,
            WorktreeJanitorIntervalHours = 24
        };
        var manager = new WorktreeManager(
            Options.Create(gitSettings),
            TimeProvider.System,
            NullLogger<WorktreeManager>.Instance);

        var eligible = await SeedWorktreeTaskAsync(db, manager, repo, "eligible");
        await File.WriteAllTextAsync(Path.Combine(eligible.WorktreePath!, "landed.md"), "done\n");
        (await ScratchGitRepo.GitInAsync(eligible.WorktreePath!, "add", "landed.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(eligible.WorktreePath!, "commit", "-m", "landed")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(repo.Path, "merge", "--ff-only", eligible.WorktreeBranch!))
            .Ok.ShouldBeTrue();

        var unmerged = await SeedWorktreeTaskAsync(db, manager, repo, "unmerged");
        await File.WriteAllTextAsync(Path.Combine(unmerged.WorktreePath!, "ahead.md"), "keep\n");
        (await ScratchGitRepo.GitInAsync(unmerged.WorktreePath!, "add", "ahead.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(unmerged.WorktreePath!, "commit", "-m", "ahead")).Ok.ShouldBeTrue();

        var clock = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        var reportSettings = new WorktreeResidueSettings { Execute = false, MinSettledMinutes = 120 };
        var report = await new WorktreeResidueSweepService(
                db, manager, Options.Create(reportSettings), Options.Create(gitSettings), clock,
                NullLogger<WorktreeResidueSweepService>.Instance)
            .RunAsync(CancellationToken.None);

        report.Counts.Eligible.ShouldBe(1);
        report.Counts.Unmerged.ShouldBe(1);
        report.Counts.Removed.ShouldBe(0);
        Directory.Exists(eligible.WorktreePath!).ShouldBeTrue();
        Directory.Exists(unmerged.WorktreePath!).ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(
            repo.Path, "show-ref", "--verify", "--quiet", $"refs/heads/{eligible.WorktreeBranch}"))
            .Ok.ShouldBeTrue();

        var executeSettings = new WorktreeResidueSettings { Execute = true, MinSettledMinutes = 120 };
        var executed = await new WorktreeResidueSweepService(
                db, manager, Options.Create(executeSettings), Options.Create(gitSettings), clock,
                NullLogger<WorktreeResidueSweepService>.Instance)
            .RunAsync(CancellationToken.None);

        executed.Counts.Eligible.ShouldBe(1);
        executed.Counts.Unmerged.ShouldBe(1);
        executed.Removed.ShouldBe(1);
        Directory.Exists(eligible.WorktreePath!).ShouldBeFalse();
        Directory.Exists(unmerged.WorktreePath!).ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(
            repo.Path, "show-ref", "--verify", "--quiet", $"refs/heads/{eligible.WorktreeBranch}"))
            .Ok.ShouldBeFalse();
        (await ScratchGitRepo.GitInAsync(
            repo.Path, "show-ref", "--verify", "--quiet", $"refs/heads/{unmerged.WorktreeBranch}"))
            .Ok.ShouldBeTrue();
    }

    [Test]
    public async Task job_logs_information_summary_and_warning_per_kept_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var manager = new RecordingResidueWorktrees
        {
            Scan =
            [
                new WorktreeResidueScanEntry(@"C:\trees\card-task-deadbeef", "feat/card-task-deadbeef",
                    @"C:\repo", Registered: true, DirectoryExists: true)
            ]
        };
        var clock = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        var service = new WorktreeResidueSweepService(
            db,
            manager,
            Options.Create(new WorktreeResidueSettings { Execute = false }),
            Options.Create(new GitSettings { DefaultBranch = "master" }),
            clock,
            NullLogger<WorktreeResidueSweepService>.Instance);
        var logger = new ListLogger<WorktreeResidueJob>();
        var job = new WorktreeResidueJob(service, logger);

        var result = await job.ExecuteAsync(CancellationToken.None);
        result.Counts.Unknown.ShouldBe(1);
        result.Kept.ShouldHaveSingleItem();
        logger.Messages.ShouldContain(m => m.Contains("Information") && m.Contains("completed"));
        logger.Messages.ShouldContain(m => m.Contains("Warning") && m.Contains("kept"));
        manager.RemoveCalls.ShouldBe(0);
    }

    private static async Task<AgentTask> SeedWorktreeTaskAsync(
        AppDbContext db, WorktreeManager manager, ScratchGitRepo repo, string title)
    {
        var id = Guid.NewGuid();
        var identifier = $"task-{DelegationReportFormatter.Short(id)}";
        var info = await manager.CreateAsync(repo.Path, identifier, "HEAD", CancellationToken.None);
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = info.Path,
            RepoPath = repo.Path,
            WorktreePath = info.Path,
            WorktreeBranch = info.Branch,
            MergeTargetRef = "master",
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = Now.AddHours(-5),
            CompletedAt = Now.AddHours(-3)
        };
        task.Events.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.Landed,
            Detail = "landed",
            At = Now.AddHours(-3)
        });
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static WorktreeResidueFacts Fact(
        string shortId,
        WorktreeResidueTaskSnapshot? task,
        bool dir = true,
        bool branch = true,
        bool ancestor = true,
        int ahead = 0,
        bool tracked = false,
        bool untracked = false) =>
        new(
            $@"C:\trees\card-task-{shortId}",
            $"feat/card-task-{shortId}",
            @"C:\repo",
            "master",
            task,
            dir,
            branch,
            ancestor,
            ahead,
            tracked,
            untracked);

    private static WorktreeResidueTaskSnapshot TaskSnap(
        string shortId,
        AgentTaskStatus status,
        DateTime? completed,
        params AgentTaskEventType[] events)
    {
        var id = Guid.Parse($"{shortId}-0000-4000-8000-000000000001");
        return new WorktreeResidueTaskSnapshot(
            id,
            status,
            completed,
            $@"C:\trees\card-task-{shortId}",
            $"feat/card-task-{shortId}",
            "master",
            @"C:\repo",
            events);
    }

    private sealed class RecordingResidueWorktrees : Antiphon.Server.Application.Interfaces.IWorktreeManager
    {
        public List<WorktreeResidueScanEntry> Scan { get; init; } = [];
        public int RemoveCalls { get; private set; }

        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct)
        {
            RemoveCalls++;
            return Task.CompletedTask;
        }

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);

        public Task<IReadOnlyList<WorktreeResidueScanEntry>> ScanResidueCandidatesAsync(
            IReadOnlyList<string> extraRepoPaths, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeResidueScanEntry>>(Scan);

        public Task<WorktreeResidueGitState> InspectResidueAsync(
            string? repoPath, string? worktreePath, string? branch, string targetRef, CancellationToken ct) =>
            Task.FromResult(new WorktreeResidueGitState(false, true, 0, false, false));
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add($"{logLevel}: {formatter(state, exception)}");

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
