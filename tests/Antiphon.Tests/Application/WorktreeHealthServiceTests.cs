using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0147 S3: classify / upsert / clear stuck <c>feat/card-task-*</c> worktrees.
/// Detection only — the sweep must never prune.
/// </summary>
[Category("Integration")]
public sealed class WorktreeHealthServiceTests
{
    [Test]
    public void classify_locked_missing_open_task_emits_locked_and_unhealthy()
    {
        var id = Guid.Parse("aabbccdd-0000-0000-0000-000000000001");
        var shortId = DelegationReportFormatter.Short(id);
        var byShort = new Dictionary<string, WorktreeHealthService.TaskRow>(StringComparer.OrdinalIgnoreCase)
        {
            [shortId] = new(id, AgentTaskStatus.Working, @"C:\repo", @"C:\trees\gone", $"feat/card-task-{shortId}"),
        };
        var scan = new[]
        {
            new DelegateWorktreeScanEntry(
                @"C:\repo",
                @"C:\trees\gone",
                $"feat/card-task-{shortId}",
                shortId,
                Registered: true,
                Locked: true,
                LockReason: "initializing",
                DirectoryExists: false,
                GitFileExists: false),
        };

        var classified = WorktreeHealthService.Classify(scan, byShort).ToList();
        classified.Select(c => c.Shape).OrderBy(s => s)
            .ShouldBe([WorktreeHealthShape.LockedMissing, WorktreeHealthShape.OpenTaskUnhealthy]);
        classified.ShouldAllBe(c => c.TaskId == id);
        classified.ShouldAllBe(c => c.Detail.Contains("locked initializing", StringComparison.OrdinalIgnoreCase)
            || c.Detail.Contains("still Working", StringComparison.Ordinal));
    }

    [Test]
    public void classify_registered_with_no_task_is_registered_no_task()
    {
        var scan = new[]
        {
            new DelegateWorktreeScanEntry(
                @"C:\repo",
                @"C:\trees\orphan",
                "feat/card-task-deadbeef",
                "deadbeef",
                Registered: true,
                Locked: false,
                LockReason: null,
                DirectoryExists: true,
                GitFileExists: true),
        };

        var classified = WorktreeHealthService.Classify(scan, new Dictionary<string, WorktreeHealthService.TaskRow>())
            .ToList();
        classified.ShouldHaveSingleItem();
        classified[0].Shape.ShouldBe(WorktreeHealthShape.RegisteredNoTask);
        classified[0].TaskId.ShouldBeNull();
    }

    [Test]
    public void classify_dangling_branch_with_no_task_is_warning_shape()
    {
        var scan = new[]
        {
            new DelegateWorktreeScanEntry(
                @"C:\repo",
                Path: "",
                Branch: "feat/card-task-cafebabe",
                ShortId: "cafebabe",
                Registered: false,
                Locked: false,
                LockReason: null,
                DirectoryExists: false,
                GitFileExists: false),
        };

        var classified = WorktreeHealthService.Classify(scan, new Dictionary<string, WorktreeHealthService.TaskRow>())
            .ToList();
        classified.ShouldHaveSingleItem();
        classified[0].Shape.ShouldBe(WorktreeHealthShape.DanglingBranchNoTask);
    }

    [Test]
    public void classify_healthy_registered_open_task_emits_nothing()
    {
        var id = Guid.Parse("11111111-0000-0000-0000-000000000001");
        var shortId = DelegationReportFormatter.Short(id);
        var byShort = new Dictionary<string, WorktreeHealthService.TaskRow>(StringComparer.OrdinalIgnoreCase)
        {
            [shortId] = new(id, AgentTaskStatus.Working, @"C:\repo", @"C:\trees\live", $"feat/card-task-{shortId}"),
        };
        var scan = new[]
        {
            new DelegateWorktreeScanEntry(
                @"C:\repo",
                @"C:\trees\live",
                $"feat/card-task-{shortId}",
                shortId,
                Registered: true,
                Locked: false,
                LockReason: null,
                DirectoryExists: true,
                GitFileExists: true),
        };

        WorktreeHealthService.Classify(scan, byShort).ShouldBeEmpty();
    }

    [Test]
    public async Task sweep_upserts_then_clears_when_the_shape_is_gone_and_never_prunes()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        using var repo = new TempRepo();
        var id = Guid.NewGuid();
        var shortId = DelegationReportFormatter.Short(id);
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "stuck init",
            Goal = "stuck init",
            Role = AgentTaskRole.Debug,
            Status = AgentTaskStatus.Working,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path,
            RepoPath = repo.Path,
            WorktreePath = Path.Combine(repo.Path, "gone"),
            WorktreeBranch = $"feat/card-task-{shortId}",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var manager = new RecordingWorktreeManager
        {
            KnownRepos = [repo.Path],
            Scan =
            [
                new DelegateWorktreeScanEntry(
                    repo.Path,
                    Path.Combine(repo.Path, "gone"),
                    $"feat/card-task-{shortId}",
                    shortId,
                    Registered: true,
                    Locked: true,
                    LockReason: "initializing",
                    DirectoryExists: false,
                    GitFileExists: false),
            ],
        };
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(db, manager, clock);

        var first = await service.SweepAsync(CancellationToken.None);
        first.FindingCount.ShouldBe(2);
        first.Findings.ShouldContain(f => f.Shape == nameof(WorktreeHealthShape.LockedMissing) && f.TaskId == id);
        first.Findings.ShouldContain(f => f.Shape == nameof(WorktreeHealthShape.OpenTaskUnhealthy) && f.TaskId == id);
        manager.RemoveCalls.ShouldBe(0);
        manager.PruneCalls.ShouldBe(0);

        clock.Advance(TimeSpan.FromMinutes(1));
        var second = await service.SweepAsync(CancellationToken.None);
        second.FindingCount.ShouldBe(2);
        await using (var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var rows = await verify.WorktreeHealthFindings.ToListAsync();
            rows.Count.ShouldBe(2);
            rows.ShouldAllBe(r => r.ClearedAt == null);
            rows.ShouldAllBe(r => r.LastSeenAt > r.FirstSeenAt);
        }

        manager.Scan = [];
        clock.Advance(TimeSpan.FromMinutes(1));
        var third = await service.SweepAsync(CancellationToken.None);
        third.FindingCount.ShouldBe(0);
        await using (var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var rows = await verify.WorktreeHealthFindings.ToListAsync();
            rows.Count.ShouldBe(2);
            rows.ShouldAllBe(r => r.ClearedAt != null);
        }

        manager.RemoveCalls.ShouldBe(0, "the sweep is detection only");
        manager.PruneCalls.ShouldBe(0, "the sweep is detection only");
    }

    [Test]
    public async Task sweep_does_not_clear_findings_for_a_repo_it_did_not_scan()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        using var repo = new TempRepo();
        db.WorktreeHealthFindings.Add(new WorktreeHealthFinding
        {
            Id = Guid.NewGuid(),
            RepoPath = @"C:\other-repo",
            Branch = "feat/card-task-aaaaaaaa",
            Path = @"C:\other-repo\gone",
            Shape = WorktreeHealthShape.RegisteredNoTask,
            Detail = "registered with no AgentTask row",
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var manager = new RecordingWorktreeManager { KnownRepos = [repo.Path], Scan = [] };
        var report = await CreateService(db, manager).SweepAsync(CancellationToken.None);
        report.FindingCount.ShouldBe(1);
        (await db.WorktreeHealthFindings.SingleAsync()).ClearedAt.ShouldBeNull();
    }

    private static WorktreeHealthService CreateService(
        AppDbContext db,
        RecordingWorktreeManager manager,
        TimeProvider? clock = null) =>
        new(
            db,
            manager,
            Options.Create(new DelegationSettings { AllowedRoots = [] }),
            clock ?? TimeProvider.System,
            NullLogger<WorktreeHealthService>.Instance);

    private sealed class RecordingWorktreeManager : IWorktreeManager
    {
        public List<DelegateWorktreeScanEntry> Scan { get; set; } = [];
        public List<string> KnownRepos { get; init; } = [];
        public int RemoveCalls { get; private set; }
        public int PruneCalls { get; private set; }

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

        public Task<int> PruneStaleAsync(CancellationToken ct)
        {
            PruneCalls++;
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<DelegateWorktreeScanEntry>> ScanDelegateWorktreesAsync(
            string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DelegateWorktreeScanEntry>>(Scan);

        public Task<IReadOnlyList<string>> ListKnownDelegateRepoPathsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>(KnownRepos);
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public MutableClock(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan delta) => _utcNow += delta;
    }

    private sealed class TempRepo : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c0147-health").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
