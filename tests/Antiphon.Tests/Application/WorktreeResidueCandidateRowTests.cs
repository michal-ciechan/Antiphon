using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
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
/// CARD-1446 D-5 (S5): a non-preview residue run writes a <c>Held</c> candidate row only when the
/// candidate's verdict changed since the last non-preview run (V-11); retention prunes only old
/// <c>Held</c>/<c>Preview</c> rows and keeps run headers and action rows (V-12).
/// </summary>
[Category("Integration")]
public sealed class WorktreeResidueCandidateRowTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);

    // Three settled tasks plus one published task: four SettledTask rows and one Publication row.
    private const int SettledTasks = 3;
    private const int CandidatesPerRun = SettledTasks + 1 + 1;

    [Test]
    public async Task C1446_unchanged_held_rows_are_not_rewritten()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var clock = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        await SeedAsync(db);
        var logger = new CapturingLogger();
        var service = Build(db, clock, logger);

        await service.RunAsync(CancellationToken.None);
        var first = await LatestRunAsync(db);
        first.Candidates.ShouldBe(CandidatesPerRun, "first-run-candidates");
        first.Held.ShouldBe(CandidatesPerRun, "first-run-held");
        var firstRows = await RowsAsync(db, first.Id);
        firstRows.Count.ShouldBe(CandidatesPerRun, "first-run-rows");
        firstRows.ShouldAllBe(r => r.Outcome == nameof(WorktreeResidueCandidateOutcome.Held));
        firstRows.Select(r => r.Lane).Distinct().OrderBy(l => l).ShouldBe(
            [nameof(WorktreeResidueLane.Publication), nameof(WorktreeResidueLane.SettledTask)]);

        clock.Advance(TimeSpan.FromDays(1));
        await service.RunAsync(CancellationToken.None);
        var second = await LatestRunAsync(db);
        second.Id.ShouldNotBe(first.Id);
        // The counters still count every evaluation...
        second.Candidates.ShouldBe(CandidatesPerRun, "second-run-candidates");
        second.Held.ShouldBe(CandidatesPerRun, "second-run-held");
        // ...but no verdict changed, so no Held row is rewritten.
        (await RowsAsync(db, second.Id)).Count.ShouldBe(0, "second-run-rows");
        logger.Messages.ShouldContain(m => m.Contains($"candidates={CandidatesPerRun} rows=0 unchanged={CandidatesPerRun}"),
            "unchanged-log-line");

        // Every evaluated candidate has a cursor carrying its last verdict.
        var cursors = await db.WorktreeResidueCandidateCursors.AsNoTracking().ToListAsync();
        cursors.Count.ShouldBe(CandidatesPerRun, "cursor-per-candidate");
        cursors.ShouldAllBe(c => c.LastOutcomeKey != null && c.LastOutcomeKey.Contains("|Held|"));
        cursors.ShouldAllBe(c => c.LastEvaluatedAt == Now.AddDays(1));
    }

    [Test]
    public async Task C1446_changed_verdict_and_preview_write_rows()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var clock = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        var (tasks, _) = await SeedAsync(db);
        var service = Build(db, clock, new CapturingLogger());

        await service.RunAsync(CancellationToken.None);
        var first = await LatestRunAsync(db);
        (await RowsAsync(db, first.Id)).Count.ShouldBe(CandidatesPerRun, "first-run-rows");

        // The verdict of one task changes: release_required -> live_owner.
        var changed = tasks[0];
        changed.Status = AgentTaskStatus.Working;
        await db.SaveChangesAsync();

        // A preview writes every row (the operator asked to see them) and consumes no change.
        clock.Advance(TimeSpan.FromHours(1));
        var preview = await service.PreviewAsync(null, null, CancellationToken.None);
        preview.Preview.ShouldBeTrue();
        var previewRows = await RowsAsync(db, preview.Id);
        previewRows.Count.ShouldBe(CandidatesPerRun, "preview-rows");
        previewRows.Single(r => r.TaskId == changed.Id && r.Lane == nameof(WorktreeResidueLane.SettledTask))
            .ReasonCode.ShouldBe("live_owner");

        // The next daily run writes exactly the changed verdict.
        clock.Advance(TimeSpan.FromDays(1));
        await service.RunAsync(CancellationToken.None);
        var second = await LatestRunAsync(db);
        second.Preview.ShouldBeFalse();
        second.Candidates.ShouldBe(CandidatesPerRun, "second-run-candidates");
        var secondRows = await RowsAsync(db, second.Id);
        var only = secondRows.ShouldHaveSingleItem("changed-verdict-row");
        only.TaskId.ShouldBe(changed.Id);
        only.Lane.ShouldBe(nameof(WorktreeResidueLane.SettledTask));
        only.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        only.ReasonCode.ShouldBe("live_owner");

        // And the run after that writes nothing again.
        clock.Advance(TimeSpan.FromDays(1));
        await service.RunAsync(CancellationToken.None);
        var third = await LatestRunAsync(db);
        third.Candidates.ShouldBe(CandidatesPerRun, "third-run-candidates");
        (await RowsAsync(db, third.Id)).Count.ShouldBe(0, "third-run-rows");
    }

    [Test]
    public async Task C1446_retention_prunes_only_old_held_rows()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var clock = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        var run = new WorktreeResidueRun { Id = Guid.NewGuid(), StartedAt = Now.AddDays(-40), FinishedAt = Now.AddDays(-40), ActionBudget = 25 };
        db.WorktreeResidueRuns.Add(run);
        var old = Now.AddDays(-40);
        var fresh = Now.AddDays(-1);
        var oldHeld = Candidate(run.Id, WorktreeResidueCandidateOutcome.Held, old);
        var oldPreview = Candidate(run.Id, WorktreeResidueCandidateOutcome.Preview, old);
        var freshHeld = Candidate(run.Id, WorktreeResidueCandidateOutcome.Held, fresh);
        var oldQueued = Candidate(run.Id, WorktreeResidueCandidateOutcome.Queued, old);
        var oldRefused = Candidate(run.Id, WorktreeResidueCandidateOutcome.Refused, old);
        var oldPartial = Candidate(run.Id, WorktreeResidueCandidateOutcome.Partial, old);
        var oldRemoved = Candidate(run.Id, WorktreeResidueCandidateOutcome.Removed, old);
        db.WorktreeResidueRunCandidates.AddRange(oldHeld, oldPreview, freshHeld, oldQueued, oldRefused, oldPartial, oldRemoved);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // 0 disables the pass.
        (await Retention(db, clock, days: 0).PruneWorktreeResidueHeldRowsAsync(CancellationToken.None)).ShouldBe(0, "disabled");
        (await db.WorktreeResidueRunCandidates.CountAsync()).ShouldBe(7, "disabled-keeps-all");

        (await Retention(db, clock, days: 30).PruneWorktreeResidueHeldRowsAsync(CancellationToken.None)).ShouldBe(2, "pruned");
        var remaining = await db.WorktreeResidueRunCandidates.AsNoTracking().Select(c => c.Id).ToListAsync();
        remaining.OrderBy(id => id).ShouldBe(
            new[] { freshHeld.Id, oldQueued.Id, oldRefused.Id, oldPartial.Id, oldRemoved.Id }.OrderBy(id => id),
            "action-rows-and-fresh-held-kept");
        (await db.WorktreeResidueRuns.CountAsync(r => r.Id == run.Id)).ShouldBe(1, "run-header-kept");

        // Idempotent: nothing else is old enough.
        (await Retention(db, clock, days: 30).PruneWorktreeResidueHeldRowsAsync(CancellationToken.None)).ShouldBe(0, "second-pass");
    }

    private static DataRetentionService Retention(AppDbContext db, TimeProvider clock, int days)
    {
        var audit = Options.Create(new AuditSettings { RetentionDays = 0 });
        return new DataRetentionService(
            db,
            Options.Create(new RetentionSettings { WorktreeResidueHeldRowRetentionDays = days }),
            audit,
            clock,
            NullLogger<DataRetentionService>.Instance,
            new AuditService(db, audit));
    }

    private static WorktreeResidueRunCandidate Candidate(Guid runId, WorktreeResidueCandidateOutcome outcome, DateTime evaluatedAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            Lane = nameof(WorktreeResidueLane.SettledTask),
            Outcome = outcome.ToString(),
            ReasonCode = outcome == WorktreeResidueCandidateOutcome.Held ? "release_required" : outcome.ToString().ToLowerInvariant(),
            TaskId = Guid.NewGuid(),
            Path = @"C:\trees\card-task-" + Guid.NewGuid().ToString("N")[..8],
            EvaluatedAt = evaluatedAt,
        };

    private static WorktreeResidueSweepService Build(AppDbContext db, FakeTimeProvider clock, ILogger<WorktreeResidueSweepService> logger)
    {
        var settings = Options.Create(new WorktreeResidueSettings { Execute = false, MaxActionsPerRun = 25, MinSettledMinutes = 120 });
        var git = Options.Create(new GitSettings { DefaultBranch = "master" });
        var retirement = new TaskWorktreeRetirementService(db, clock, settings, git, NullLogger<TaskWorktreeRetirementService>.Instance);
        return new WorktreeResidueSweepService(db, new EmptyWorktrees(), settings, git, clock, logger, retirement);
    }

    private static async Task<(List<AgentTask> Tasks, AgentTaskLanding Landing)> SeedAsync(AppDbContext db)
    {
        var tasks = new List<AgentTask>();
        for (var i = 0; i < SettledTasks; i++)
        {
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id, RootTaskId = id, Title = "settled" + i, Goal = "settled",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = @"C:\trees\card-task-settled" + i, WorktreePath = @"C:\trees\card-task-settled" + i,
                WorktreeBranch = "feat/card-task-settled" + i, Status = AgentTaskStatus.Succeeded,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = Now.AddHours(-5 - i), CompletedAt = Now.AddHours(-3 - i), Result = "done",
            };
            tasks.Add(task);
            db.AgentTasks.Add(task);
        }

        var publishedId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = publishedId, RootTaskId = publishedId, Title = "published", Goal = "published",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = @"C:\trees\card-task-pub", WorktreePath = @"C:\trees\card-task-pub",
            Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = Now.AddHours(-5), CompletedAt = Now.AddHours(-3),
        });
        var landing = new AgentTaskLanding
        {
            Id = operationId, TaskId = publishedId, SchemaVersion = 1, Active = true,
            Publication = LandPublicationOutcome.Landed, Cleanup = LandCleanupStatus.Pending,
            RepositoryPath = @"C:\repo", WorktreePath = @"C:\trees\card-task-pub", CommonDirectory = @"C:\repo",
            GitDirectory = @"C:\repo\.git", SourceFullRef = "refs/heads/feat/x", TargetFullRef = "refs/heads/master",
            OriginalSourceSha = new string('a', 40), TargetBeforeSha = new string('b', 40),
            RecoveryRefPrefix = $"refs/antiphon/land/{publishedId:N}/{operationId:N}",
            CreatedAt = Now.AddHours(-3), UpdatedAt = Now.AddHours(-3),
        };
        db.AgentTaskLandings.Add(landing);
        await db.SaveChangesAsync();
        return (tasks, landing);
    }

    private static Task<WorktreeResidueRun> LatestRunAsync(AppDbContext db) =>
        db.WorktreeResidueRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).FirstAsync();

    private static Task<List<WorktreeResidueRunCandidate>> RowsAsync(AppDbContext db, Guid runId) =>
        db.WorktreeResidueRunCandidates.AsNoTracking().Where(c => c.RunId == runId).ToListAsync();

    private sealed class CapturingLogger : ILogger<WorktreeResidueSweepService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class EmptyWorktrees : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
