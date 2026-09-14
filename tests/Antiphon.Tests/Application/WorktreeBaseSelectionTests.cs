using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Slow")]
public class WorktreeBaseSelectionTests
{
    [Test]
    public async Task C508_UnresolvedDefaultRetainsName()
    {
        var first = "missing-" + Guid.NewGuid().ToString("N")[..8];
        var second = "other-" + Guid.NewGuid().ToString("N")[..8];
        first.ShouldNotBe(second);

        var a = WorktreeBaseResolver.Resolve(null, null, null, new DefaultBranchProbe(first, false));
        a.Ref.ShouldBe("HEAD");
        a.Source.ShouldBe(WorktreeBaseSource.RepoHead);
        a.UnresolvedDefault.ShouldBe(first);

        var b = WorktreeBaseResolver.Resolve(null, null, null, new DefaultBranchProbe(second, false));
        b.Ref.ShouldBe("HEAD");
        b.Source.ShouldBe(WorktreeBaseSource.RepoHead);
        b.UnresolvedDefault.ShouldBe(second);

        WorktreeBaseResolver.Resolve("deadbeef", null, null, new DefaultBranchProbe(first, false))
            .ShouldBe(new ResolvedBase("deadbeef", WorktreeBaseSource.Repair, null));
        WorktreeBaseResolver.Resolve(null, "explicit", null, new DefaultBranchProbe(first, false))
            .ShouldBe(new ResolvedBase("explicit", WorktreeBaseSource.Explicit, null));
        WorktreeBaseResolver.Resolve(null, null, "target", new DefaultBranchProbe(first, false))
            .ShouldBe(new ResolvedBase("target", WorktreeBaseSource.MergeTarget, null));
        WorktreeBaseResolver.Resolve(null, null, null, new DefaultBranchProbe("trunk", true))
            .ShouldBe(new ResolvedBase("trunk", WorktreeBaseSource.DefaultBranch, null));
        WorktreeBaseResolver.Resolve(null, null, null, new DefaultBranchProbe("trunk", false))
            .ShouldBe(new ResolvedBase("HEAD", WorktreeBaseSource.RepoHead, "trunk"));
        await Task.CompletedTask;
    }

    [Test]
    public void C508_DefaultCandidateSelection()
    {
        WorktreeBaseResolver.ChooseConfiguredDefault("release", "trunk").ShouldBe("release");
        WorktreeBaseResolver.ChooseConfiguredDefault("  ", "trunk").ShouldBe("trunk");
        WorktreeBaseResolver.ChooseConfiguredDefault(null, "trunk").ShouldBe("trunk");
        WorktreeBaseResolver.ChooseConfiguredDefault(null, "  ").ShouldBe("master");
        WorktreeBaseResolver.ChooseConfiguredDefault(null, null).ShouldBe("master");
        WorktreeBaseResolver.ChooseConfiguredDefault("", "").ShouldBe("master");
        WorktreeBaseResolver.ChooseConfiguredDefault("\t", "trunk").ShouldBe("trunk");
    }

    [Test]
    public async Task C508_ResolverAndCommitProbe()
    {
        using var repo = new ScratchGitRepo("c508-probe");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("tag", "commit-tag");
        var blob = (await repo.GitReadAsync("hash-object", "-w", "README.md")).Trim();
        await repo.GitAsync("tag", "blob-tag", blob);

        var (service, _) = CreateService(repo, new GitSettings { DefaultBranch = "master", WorktreeBasePath = repo.WorktreeRoot });
        (await service.RefExistsAsync(repo.Path, "master", CancellationToken.None)).ShouldBeTrue();
        (await service.RefExistsAsync(repo.Path, "commit-tag", CancellationToken.None)).ShouldBeTrue();
        (await service.RefExistsAsync(repo.Path, "blob-tag", CancellationToken.None)).ShouldBeFalse();
        (await service.RefExistsAsync(repo.Path, "missing-ref", CancellationToken.None)).ShouldBeFalse();
        (await service.RefExistsAsync(Path.Combine(repo.Path, "no-such-dir"), "master", CancellationToken.None))
            .ShouldBeFalse();

        var task = NewTask(repo.Path);
        var probe = await service.ProbeConfiguredDefaultAsync(task, CancellationToken.None);
        probe.Ref.ShouldBe("master");
        probe.ResolvesToCommit.ShouldBeTrue();

        WorktreeBaseResolver.Resolve(null, "explicit-e", "merge-c", probe)
            .ShouldBe(new ResolvedBase("explicit-e", WorktreeBaseSource.Explicit, null));
        WorktreeBaseResolver.Resolve(null, null, "merge-c", probe)
            .ShouldBe(new ResolvedBase("merge-c", WorktreeBaseSource.MergeTarget, null));
        WorktreeBaseResolver.Resolve("repair-sha", "explicit-e", "merge-c", probe)
            .ShouldBe(new ResolvedBase("repair-sha", WorktreeBaseSource.Repair, null));
        WorktreeBaseResolver.Resolve(null, null, null, probe)
            .ShouldBe(new ResolvedBase("master", WorktreeBaseSource.DefaultBranch, null));
    }

    [Test]
    public async Task C508_DefaultProbeCancellation()
    {
        using var repo = new ScratchGitRepo("c508-cancel");
        await repo.CommitFileAsync("README.md", "base\n");
        var (service, _) = CreateService(repo, new GitSettings { DefaultBranch = "master", WorktreeBasePath = repo.WorktreeRoot });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(
            () => service.ProbeConfiguredDefaultAsync(NewTask(repo.Path), cts.Token));
    }

    [Test]
    public async Task C508_DefaultBranchMatrix()
    {
        using var repo = new ScratchGitRepo("c508-matrix");
        await repo.CommitFileAsync("README.md", "seed-B\n");
        var seedB = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.CommitFileAsync("master.txt", "M\n");
        var masterM = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("branch", "trunk");
        await repo.GitAsync("checkout", "trunk");
        await repo.CommitFileAsync("trunk.txt", "T\n");
        var trunkT = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "-b", "release");
        await repo.CommitFileAsync("release.txt", "P\n");
        var releaseP = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "-b", "feature-H");
        await repo.CommitFileAsync("head.txt", "H\n");
        var headH = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        new[] { seedB, masterM, trunkT, releaseP, headH }.Distinct().Count().ShouldBe(5);

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = "c508", GitRepositoryUrl = "https://example.test/c508.git",
            BaseBranch = "release", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var rows = new (string Name, Guid? ProjectId, GitSettings? Settings, string ExpectedRef, string ExpectedSha, WorktreeBaseSource Source)[]
        {
            ("card-project", project.Id, new GitSettings { DefaultBranch = "trunk", WorktreeBasePath = repo.WorktreeRoot }, "release", releaseP, WorktreeBaseSource.DefaultBranch),
            ("noncard-project", project.Id, new GitSettings { DefaultBranch = "trunk", WorktreeBasePath = repo.WorktreeRoot }, "release", releaseP, WorktreeBaseSource.DefaultBranch),
            ("card-global", null, new GitSettings { DefaultBranch = "trunk", WorktreeBasePath = repo.WorktreeRoot }, "trunk", trunkT, WorktreeBaseSource.DefaultBranch),
            ("noncard-global", null, new GitSettings { DefaultBranch = "trunk", WorktreeBasePath = repo.WorktreeRoot }, "trunk", trunkT, WorktreeBaseSource.DefaultBranch),
            ("card-hard", null, null, "master", masterM, WorktreeBaseSource.DefaultBranch),
            ("noncard-hard", null, null, "master", masterM, WorktreeBaseSource.DefaultBranch),
            ("project-missing-global-present", project.Id, new GitSettings { DefaultBranch = "trunk", WorktreeBasePath = repo.WorktreeRoot }, "release", releaseP, WorktreeBaseSource.DefaultBranch),
        };

        foreach (var row in rows.Take(6))
        {
            var (service, _) = row.Settings is null
                ? CreateServiceWithoutOptions(repo)
                : CreateService(repo, row.Settings, db);
            var task = NewTask(repo.Path);
            task.ProjectId = row.ProjectId;
            if (row.Name.StartsWith("card", StringComparison.Ordinal))
                task.CardId = Guid.NewGuid();
            await service.CreateForTaskAsync(task, CancellationToken.None);
            var head = (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim();
            head.ShouldBe(row.ExpectedSha, row.Name);
            head.ShouldNotBe(headH, row.Name);
            task.WorktreeBaseRef.ShouldBe(row.ExpectedRef, row.Name);
            task.WorktreeBaseSource.ShouldBe(row.Source, row.Name);
            task.WorktreeBaseSha.ShouldBe(head, row.Name);
            task.WorktreeBaseTaskId.ShouldBeNull(row.Name);
            task.MergeTargetRef.ShouldBeNull();
        }

        var missingProject = new Project
        {
            Id = Guid.NewGuid(), Name = "c508-missing", GitRepositoryUrl = "https://example.test/c508m.git",
            BaseBranch = "no-such-branch", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Projects.Add(missingProject);
        await db.SaveChangesAsync();
        var (missingService, _) = CreateService(repo, new GitSettings { DefaultBranch = "trunk", WorktreeBasePath = repo.WorktreeRoot }, db);
        var missingTask = NewTask(repo.Path);
        missingTask.ProjectId = missingProject.Id;
        var missingDecision = await missingService.CreateForTaskAsync(missingTask, CancellationToken.None);
        var missingHead = (await ScratchGitRepo.GitInAsync(missingTask.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim();
        missingHead.ShouldBe(headH);
        missingTask.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.RepoHead);
        missingTask.WorktreeBaseRef.ShouldBe("HEAD");
        missingDecision!.Resolved.UnresolvedDefault.ShouldBe("no-such-branch");
        missingDecision.NewlyRecorded.ShouldBeTrue();
    }

    [Test]
    public async Task C508_BasePrecedence()
    {
        using var repo = new ScratchGitRepo("c508-prec");
        await repo.CommitFileAsync("README.md", "B\n");
        await repo.CommitFileAsync("master.txt", "M\n");
        await repo.GitAsync("branch", "parent-C");
        await repo.GitAsync("checkout", "parent-C");
        await repo.CommitFileAsync("c.txt", "C\n");
        var shaC = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "-b", "explicit-E");
        await repo.CommitFileAsync("e.txt", "E\n");
        var shaE = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "master");
        var shaM = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        shaE.ShouldNotBe(shaC);
        shaC.ShouldNotBe(shaM);

        var (service, manager) = CreateService(repo, new GitSettings { DefaultBranch = "master", WorktreeBasePath = repo.WorktreeRoot });

        var eAndC = NewTask(repo.Path);
        eAndC.WorktreeBaseRequestedRef = "explicit-E";
        eAndC.MergeTargetRef = "parent-C";
        await service.CreateForTaskAsync(eAndC, CancellationToken.None);
        (await ScratchGitRepo.GitInAsync(eAndC.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(shaE);
        eAndC.MergeTargetRef.ShouldBe("parent-C");
        eAndC.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Explicit);

        var onlyC = NewTask(repo.Path);
        onlyC.MergeTargetRef = "parent-C";
        await service.CreateForTaskAsync(onlyC, CancellationToken.None);
        (await ScratchGitRepo.GitInAsync(onlyC.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(shaC);
        onlyC.MergeTargetRef.ShouldBe("parent-C");
        onlyC.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.MergeTarget);

        var onlyE = NewTask(repo.Path);
        onlyE.WorktreeBaseRequestedRef = "explicit-E";
        await service.CreateForTaskAsync(onlyE, CancellationToken.None);
        (await ScratchGitRepo.GitInAsync(onlyE.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(shaE);
        onlyE.MergeTargetRef.ShouldBeNull();
        onlyE.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Explicit);

        var invalidE = NewTask(repo.Path);
        invalidE.WorktreeBaseRequestedRef = "no-such-E";
        invalidE.MergeTargetRef = "parent-C";
        var failedE = await Should.ThrowAsync<ValidationException>(() => service.CreateForTaskAsync(invalidE, CancellationToken.None));
        string.Join(" ", failedE.Errors.SelectMany(e => e.Value)).ShouldContain("no-such-E");
        invalidE.WorktreePath.ShouldBeNull();
        (await manager.ListAsync(repo.Path, CancellationToken.None)).Count.ShouldBe(3);

        var invalidC = NewTask(repo.Path);
        invalidC.MergeTargetRef = "no-such-C";
        var failedC = await Should.ThrowAsync<ValidationException>(() => service.CreateForTaskAsync(invalidC, CancellationToken.None));
        string.Join(" ", failedC.Errors.SelectMany(e => e.Value)).ShouldContain("no-such-C");
        invalidC.WorktreePath.ShouldBeNull();
    }

    [Test]
    public async Task C508_CherryContainmentMatrix()
    {
        using var repo = new ScratchGitRepo("c508-cherry");
        await repo.CommitFileAsync("README.md", "B\n");
        await repo.CommitFileAsync("master.txt", "M extra\n");
        var master = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "-b", "topic");
        await repo.CommitFileAsync("topic.txt", "unique\n");
        var topicTip = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var topicTree = (await repo.GitReadAsync("rev-parse", "topic^{tree}")).Trim();
        await repo.GitAsync("checkout", "master");
        (await repo.GitReadAsync("rev-parse", "--abbrev-ref", "HEAD")).Trim().ShouldBe("master");
        var published = (await repo.GitReadAsync("commit-tree", topicTree, "-p", master, "-m", "published equivalent")).Trim();
        published.ShouldNotBeNullOrWhiteSpace();
        published.ShouldNotBe(topicTip);
        await repo.GitAsync("update-ref", "refs/heads/master", published);
        var rebasedOnMaster = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        rebasedOnMaster.ShouldBe(published);
        rebasedOnMaster.ShouldNotBe(topicTip);
        rebasedOnMaster.ShouldNotBe(master);
        (await ScratchGitRepo.GitInAsync(repo.Path, "merge-base", "--is-ancestor", "topic", "master"))
            .Ok.ShouldBeFalse();
        var cherry = await ScratchGitRepo.GitInAsync(repo.Path, "cherry", "master", "topic");
        cherry.Ok.ShouldBeTrue();
        var cherryLines = cherry.StdOut.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        cherryLines.ShouldNotBeEmpty();
        cherryLines.ShouldAllBe(line => line.StartsWith('-'));

        var (service, _) = CreateService(repo, new GitSettings { DefaultBranch = "master", WorktreeBasePath = repo.WorktreeRoot });
        (await service.ContainsPatchesAsync(repo.Path, "master", "master", CancellationToken.None)).ShouldBeTrue();
        (await service.ContainsPatchesAsync(repo.Path, "topic", "master", CancellationToken.None)).ShouldBeTrue();

        await repo.GitAsync("checkout", "-b", "plus-only");
        await repo.CommitFileAsync("plus.txt", "plus\n");
        (await service.ContainsPatchesAsync(repo.Path, "plus-only", "master", CancellationToken.None)).ShouldBeFalse();

        await repo.GitAsync("checkout", "topic");
        await repo.CommitFileAsync("mixed.txt", "mixed\n");
        (await service.ContainsPatchesAsync(repo.Path, "topic", "master", CancellationToken.None)).ShouldBeFalse();

        (await service.ContainsPatchesAsync(repo.Path, "", "master", CancellationToken.None)).ShouldBeFalse();
        (await service.ContainsPatchesAsync(repo.Path, "topic", "", CancellationToken.None)).ShouldBeFalse();
        (await service.ContainsPatchesAsync(repo.Path, "missing-branch", "master", CancellationToken.None)).ShouldBeFalse();
        (await service.ContainsPatchesAsync(repo.Path, "topic", "missing-base", CancellationToken.None)).ShouldBeFalse();
        (await service.ContainsPatchesAsync(Path.Combine(repo.Path, "gone"), "topic", "master", CancellationToken.None))
            .ShouldBeFalse();
    }

    [Test]
    public async Task C508_ProvisioningPreservesFailedDefault()
    {
        using var repo = new ScratchGitRepo("c508-failname");
        await repo.CommitFileAsync("README.md", "B\n");
        await repo.GitAsync("checkout", "-b", "feature-H");
        await repo.CommitFileAsync("h.txt", "H\n");
        var first = "gone-" + Guid.NewGuid().ToString("N")[..8];
        var second = "away-" + Guid.NewGuid().ToString("N")[..8];
        var (serviceA, _) = CreateService(repo, new GitSettings { DefaultBranch = first, WorktreeBasePath = repo.WorktreeRoot });
        var taskA = NewTask(repo.Path);
        var decisionA = await serviceA.CreateForTaskAsync(taskA, CancellationToken.None);
        decisionA!.Resolved.UnresolvedDefault.ShouldBe(first);
        decisionA.Resolved.Source.ShouldBe(WorktreeBaseSource.RepoHead);

        var (serviceB, _) = CreateService(repo, new GitSettings { DefaultBranch = second, WorktreeBasePath = repo.WorktreeRoot });
        var taskB = NewTask(repo.Path);
        var decisionB = await serviceB.CreateForTaskAsync(taskB, CancellationToken.None);
        decisionB!.Resolved.UnresolvedDefault.ShouldBe(second);
        first.ShouldNotBe(second);
    }

    [Test]
    public async Task C508_BaseFieldsUpgradeAndRoundTrip()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        var cut = Array.FindIndex(migrations, m => m.EndsWith("_AddWorktreeBaseSelection", StringComparison.Ordinal));
        cut.ShouldBeGreaterThan(0);
        await db.GetService<IMigrator>().MigrateAsync(migrations[cut - 1]);
        var id = Guid.NewGuid();
        var old = DateTime.UtcNow.AddDays(-2);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTasks" ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role", "ModelLevel", "Attempt", "MaxAttempts",
                "WorkingDirectory", "Ephemeral", "Status", "ReplyTo", "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut", "CostUsd",
                "Workspace")
            VALUES ({id}, {id}, 0, 'legacy', 'legacy fixture', 0, 0, 0, 0, 1, 'fixture', false, 0, 0, {Guid.NewGuid()}, {old}, 0, 0, 0, 0)
            """);
        await db.GetService<IMigrator>().MigrateAsync();
        await using var observer = new AppDbContext(options);
        var legacy = await observer.AgentTasks.SingleAsync(t => t.Id == id);
        legacy.WorktreeBaseRequestedRef.ShouldBeNull();
        legacy.WorktreeBaseRef.ShouldBeNull();
        legacy.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Unset);
        legacy.WorktreeBaseTaskId.ShouldBeNull();

        var longRef = new string('b', 300);
        legacy.WorktreeBaseRequestedRef = longRef;
        legacy.WorktreeBaseRef = "master";
        legacy.WorktreeBaseSource = WorktreeBaseSource.DefaultBranch;
        legacy.WorktreeBaseSha = new string('a', 40);
        await observer.SaveChangesAsync();
        await using var round = new AppDbContext(options);
        var stored = await round.AgentTasks.SingleAsync(t => t.Id == id);
        stored.WorktreeBaseRequestedRef.ShouldBe(longRef);
        stored.WorktreeBaseRef.ShouldBe("master");
        stored.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.DefaultBranch);
        stored.WorktreeBaseTaskId.ShouldBeNull();
        stored.MergeTargetRef.ShouldBeNull();
    }

    private static AgentTask NewTask(string repoPath) => new()
    {
        Id = Guid.NewGuid(),
        RootTaskId = Guid.NewGuid(),
        Title = "c508 task",
        Goal = "test",
        Workspace = WorkspaceMode.Worktree,
        WorkingDirectory = repoPath,
        RepoPath = repoPath,
        CreatedAt = DateTime.UtcNow,
    };

    private static (DelegationWorktreeService Service, Antiphon.Server.Infrastructure.Git.WorktreeManager Manager)
        CreateService(ScratchGitRepo repo, GitSettings settings, AppDbContext? db = null)
    {
        var graph = DelegationTestServices.CreateGitGraph(settings, db);
        return (graph.Worktrees, graph.Manager);
    }

    private static (DelegationWorktreeService Service, Antiphon.Server.Infrastructure.Git.WorktreeManager Manager)
        CreateServiceWithoutOptions(ScratchGitRepo repo)
    {
        var settings = new GitSettings { WorktreeBasePath = repo.WorktreeRoot, DefaultBranch = "main" };
        var git = new Antiphon.Server.Infrastructure.Git.LandingGit();
        var leases = new Antiphon.Server.Infrastructure.Git.RepositoryMutationLease(git);
        var guarded = new Antiphon.Server.Infrastructure.Git.GuardedWorktreeRemoval(
            git, leases, new NullRemovalEvidence());
        var manager = new Antiphon.Server.Infrastructure.Git.WorktreeManager(
            Options.Create(settings), TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Antiphon.Server.Infrastructure.Git.WorktreeManager>.Instance,
            guarded, leases, git);
        var worktrees = new DelegationWorktreeService(
            manager,
            new Antiphon.Server.Infrastructure.Git.GitService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Antiphon.Server.Infrastructure.Git.GitService>.Instance),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DelegationWorktreeService>.Instance,
            new GitWorkspaceService(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<GitWorkspaceService>.Instance),
            leases, git);
        return (worktrees, manager);
    }

    private sealed class NullRemovalEvidence : Antiphon.Server.Application.Interfaces.IWorktreeRemovalEvidence
    {
        public Task<AgentTaskLanding?> ReadAsync(Guid operationId, CancellationToken ct) =>
            Task.FromResult<AgentTaskLanding?>(null);
    }
}
