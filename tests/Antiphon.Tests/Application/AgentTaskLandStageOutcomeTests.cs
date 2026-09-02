using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0272 S1: the land operation writes one StageOutcome row per step it actually ran.
/// Real git, isolated schema — the rows are the thing under test, not the rebase itself
/// (that stays in <see cref="DelegationWorktreeTests"/>).
/// </summary>
[Category("Integration")]
public class AgentTaskLandStageOutcomeTests
{
    [Test]
    public async Task land_happy_path_writes_rebase_verify_cleanup_rows()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-happy");
        using var remote = new TemporaryDirectory("antiphon-land-so-remote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await SeedBuildableAsync(repo);
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        await repo.CommitFileAsync("README.md", "base advanced\n");
        await repo.GitAsync("push", "origin", "master");

        var result = await land.RunAsync(task.Id, null, CancellationToken.None);

        result.ShouldBe(LandRunResult.Complete);
        var rows = await RowsAsync(db, task.Id);
        rows.Select(o => (o.Stage, o.Outcome, o.Source)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean, StageOutcomeSource.Server),
            (OrchestrationStage.Verify, StageOutcomeKind.Clean, StageOutcomeSource.Server),
            (OrchestrationStage.Cleanup, StageOutcomeKind.Clean, StageOutcomeSource.Server),
        ]);
        rows.Single(o => o.Stage == OrchestrationStage.Verify).Detail.ShouldContain("build OK");
    }

    [Test]
    public async Task land_conflict_writes_rebase_found()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-conflict");
        using var remote = new TemporaryDirectory("antiphon-land-so-cremote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("shared.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "shared.md"), "task version\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "shared.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "task edit");
        await repo.CommitFileAsync("shared.md", "target version\n");
        await repo.GitAsync("push", "origin", "master");

        await land.RunAsync(task.Id, null, CancellationToken.None);

        var rows = await RowsAsync(db, task.Id);
        rows.ShouldHaveSingleItem();
        rows[0].Stage.ShouldBe(OrchestrationStage.Rebase);
        rows[0].Outcome.ShouldBe(StageOutcomeKind.Found);
        rows[0].Detail.ShouldContain("shared.md");
        rows[0].Ref.ShouldNotBeNull();
        rows[0].Ref.ShouldNotBe("merge task cap reached");
        (await db.AgentTasks.CountAsync(t => t.ParentTaskId == task.Id && t.Role == AgentTaskRole.Merge))
            .ShouldBe(1);
    }

    [Test]
    public async Task land_push_rejection_writes_cleanup_failed()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-push");
        using var remote = new TemporaryDirectory("antiphon-land-so-premote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        // Fetch URL stays the bare remote so prepare succeeds; push URL is unusable so finalize
        // is the step that fails (a rival push before RunAsync would fail prepare instead —
        // origin-ahead is a rebase refusal).
        await repo.GitAsync("remote", "set-url", "--push", "origin", Path.Combine(remote.Path, "no-such-remote.git"));

        await land.RunAsync(task.Id, null, CancellationToken.None);

        var rows = await RowsAsync(db, task.Id);
        rows.Select(o => (o.Stage, o.Outcome)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean),
            (OrchestrationStage.Verify, StageOutcomeKind.Skipped),
            (OrchestrationStage.Cleanup, StageOutcomeKind.Failed),
        ]);
        rows.Single(o => o.Stage == OrchestrationStage.Cleanup).Detail.ShouldContain("push");
    }

    [Test]
    public async Task land_verify_failure_writes_verify_found()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-red");
        using var remote = new TemporaryDirectory("antiphon-land-so-rremote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "land me\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");
        await repo.CommitFileAsync("README.md", "base advanced\n");
        await repo.GitAsync("push", "origin", "master");

        await land.RunAsync(task.Id, null, CancellationToken.None);

        var rows = await RowsAsync(db, task.Id);
        rows.Select(o => (o.Stage, o.Outcome)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean),
            (OrchestrationStage.Verify, StageOutcomeKind.Found),
        ]);
        var verify = rows.Single(o => o.Stage == OrchestrationStage.Verify);
        verify.Detail.ShouldContain("build failed");
        verify.Ref.ShouldBe(task.WorktreePath);
        Directory.Exists(task.WorktreePath).ShouldBeTrue();
    }

    [Test]
    public async Task a_clean_change_lands_and_skips_verify_when_the_base_did_not_move()
    {
        using var repo = new ScratchGitRepo("antiphon-land-so-clean");
        using var remote = new TemporaryDirectory("antiphon-land-so-clremote");
        await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (land, worktrees) = CreateLand(db, repo);
        var task = await SeedSucceededWorktreeAsync(db, worktrees, repo);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "feature.md"), "the work\n");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "feature.md");
        await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "feature");

        await land.RunAsync(task.Id, null, CancellationToken.None);

        var rows = await RowsAsync(db, task.Id);
        rows.Select(o => (o.Stage, o.Outcome)).ShouldBe([
            (OrchestrationStage.Rebase, StageOutcomeKind.Clean),
            (OrchestrationStage.Verify, StageOutcomeKind.Skipped),
            (OrchestrationStage.Cleanup, StageOutcomeKind.Clean),
        ]);
        Directory.Exists(task.WorktreePath).ShouldBeFalse();
    }

    private static async Task<AgentTask> SeedSucceededWorktreeAsync(
        AppDbContext db, DelegationWorktreeService worktrees, ScratchGitRepo repo)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "land stage-outcome task",
            Goal = "Land me.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path,
            RepoPath = repo.Path,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        };
        await worktrees.CreateForTaskAsync(task, CancellationToken.None);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static (AgentTaskLandService Land, DelegationWorktreeService Worktrees) CreateLand(
        AppDbContext db, ScratchGitRepo repo)
    {
        var manager = new WorktreeManager(
            Options.Create(new GitSettings
            {
                WorktreeBasePath = repo.WorktreeRoot,
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24,
            }),
            TimeProvider.System,
            NullLogger<WorktreeManager>.Instance);
        var worktrees = new DelegationWorktreeService(
            manager,
            new GitService(NullLogger<GitService>.Instance),
            NullLogger<DelegationWorktreeService>.Instance,
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance));
        var tasks = new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings { MaxTasksPerRoot = 40, MaxDepth = 5 }),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);
        var land = new AgentTaskLandService(
            db,
            worktrees,
            tasks,
            new AgentTaskLandQueue(),
            null!,
            new MockEventBus(),
            TimeProvider.System,
            NullLogger<AgentTaskLandService>.Instance);
        return (land, worktrees);
    }

    private static async Task SeedBuildableAsync(ScratchGitRepo repo)
    {
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "LandProbe.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "Marker.cs"),
            "namespace LandProbe; public static class Marker { public static int Value => 1; }\n");
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", "buildable");
    }

    private static async Task<List<StageOutcome>> RowsAsync(AppDbContext db, Guid taskId) =>
        await db.StageOutcomes.AsNoTracking()
            .Where(o => o.SubjectTaskId == taskId)
            .OrderBy(o => o.RecordedAt).ThenBy(o => o.Stage)
            .ToListAsync();

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory().FullName;

        public TemporaryDirectory(string prefix)
        {
            Directory.Delete(Path);
            Path = Directory.CreateTempSubdirectory(prefix).FullName;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
