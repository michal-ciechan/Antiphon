using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AgentTaskWorktreeContinuityTests
{
    [Test]
    [Arguments("implicit_master")]
    [Arguments("explicit_master")]
    [Arguments("inherited_parent")]
    [Timeout(180_000)]
    public async Task T0442_V01_create_dispatch_and_land_inherited_history(string scenario, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-full-cycle");
        using var remote = new TemporaryDirectory("c442-full-cycle-origin");
        (await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare")).Ok.ShouldBeTrue();
        await AgentTaskLandStageOutcomeTests.SeedBuildableAsync(repo);
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");
        var targetBefore = (await repo.GitReadAsync("rev-parse", "master")).Trim();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var source = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(
            db, repo, card.Id, "A", startRef: "master");
        AgentTask? parent = null;
        if (scenario == "inherited_parent")
        {
            var parentId = Guid.NewGuid();
            parent = new AgentTask
            {
                Id = parentId, RootTaskId = parentId, Title = "orchestrator",
                Goal = "Continue the card", Kind = AgentTaskKind.Orchestrator,
                Role = AgentTaskRole.Plan, Workspace = WorkspaceMode.Shared,
                WorkingDirectory = repo.Path, RepoPath = repo.Path, CardId = card.Id,
                Status = AgentTaskStatus.Working, CreatedAt = DateTime.UtcNow,
            };
            db.AgentTasks.Add(parent);
        }
        await db.SaveChangesAsync(ct);
        var sourceSha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();
        var markerName = $"plan-{DelegationReportFormatter.Short(source.Id)}.md";
        var sourceMarker = Path.Combine(repo.Path, markerName);
        File.Exists(sourceMarker).ShouldBeFalse();
        var request = new CreateAgentTaskRequest("Continue A", Title: "CARD-0442 Code 2",
            Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
            Card: card.Id.ToString("D"),
            MergeTargetRef: scenario == "explicit_master" ? "master" : null);
        var caller = new AgentTaskService.Caller(parent, null, repo.Path);
        AgentTaskCreatedDto created;
        await using (var createProvider = AgentTaskDispatchBaseGuardTests.CreateProvider(
            schema.ConnectionString, repo.WorktreeRoot))
        await using (var scope = createProvider.CreateAsyncScope())
            created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .CreateAsync(request, caller, ct);
        created.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
        created.WorktreeBase.SourceTaskId.ShouldBe(source.Id);
        created.WorktreeBase.SourceSha.ShouldBe(sourceSha);
        await using (var dispatchProvider = AgentTaskDispatchBaseGuardTests.CreateProvider(
            schema.ConnectionString, repo.WorktreeRoot))
        await using (var scope = dispatchProvider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
        db.ChangeTracker.Clear();
        var child = await db.AgentTasks.SingleAsync(t => t.Id == created.Id, ct);
        child.Status.ShouldBe(AgentTaskStatus.Dispatched);
        child.WorktreeBaseSha.ShouldBe(sourceSha);
        child.WorktreeBaseTaskId.ShouldBe(source.Id);
        child.MergeTargetRef.ShouldBe(scenario == "explicit_master" ? "master" : null);
        if (scenario == "inherited_parent") child.ParentTaskId.ShouldBe(parent!.Id);
        var dispatchEvent = await db.AgentTaskEvents.AsNoTracking().SingleAsync(e =>
            e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Dispatched
                && e.Detail.StartsWith("Worktree created"), ct);
        dispatchEvent.Detail.ShouldContain(DelegationReportFormatter.Short(source.Id));
        dispatchEvent.Detail.ShouldContain(source.WorktreeBranch!);
        dispatchEvent.Detail.ShouldContain(sourceSha);
        var brief = DelegationReportFormatter.BuildBrief(child, new DelegationSettings());
        brief.ShouldContain($"Worktree source: task {DelegationReportFormatter.Short(source.Id)}");
        brief.ShouldContain(sourceSha);
        brief.ShouldContain("Landing target: master");
        await using (var detailProvider = AgentTaskDispatchBaseGuardTests.CreateProvider(
            schema.ConnectionString, repo.WorktreeRoot))
        await using (var scope = detailProvider.CreateAsyncScope())
        {
            var detail = await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
                .GetAsync(child.Id, ct);
            detail.WorktreeBaseSha.ShouldBe(sourceSha);
            detail.WorktreeBaseTaskId.ShouldBe(source.Id);
            detail.MergeTargetRef.ShouldBe(child.MergeTargetRef);
        }
        (await ScratchGitRepo.GitInAsync(child.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim().ShouldBe(sourceSha);
        File.Exists(Path.Combine(child.WorktreePath!, markerName)).ShouldBeTrue();
        File.Exists(sourceMarker).ShouldBeFalse();
        (await repo.GitReadAsync("rev-parse", "master")).Trim().ShouldBe(targetBefore);
        await File.WriteAllTextAsync(Path.Combine(child.WorktreePath!, "B.txt"), "B\n", ct);
        (await ScratchGitRepo.GitInAsync(child.WorktreePath!, "add", "B.txt")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(child.WorktreePath!, "commit", "-m", "B")).Ok.ShouldBeTrue();
        if (parent is not null)
        {
            var settledParent = await db.AgentTasks.SingleAsync(t => t.Id == parent.Id, ct);
            settledParent.Status = AgentTaskStatus.Succeeded;
            settledParent.CompletedAt = DateTime.UtcNow;
        }
        child.Status = AgentTaskStatus.Succeeded;
        child.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        var (land, _) = AgentTaskLandStageOutcomeTests.CreateLand(db, repo);
        await AgentTaskLandStageOutcomeTests.RequestHeadAsync(land, child);
        var outcome = await land.RunAsync(child.Id, null, ct);
        if (outcome == LandRunResult.Held)
        {
            var hold = await db.AgentTaskLandRequests.AsNoTracking()
                .SingleAsync(r => r.TaskId == child.Id, ct);
            throw new Exception($"unexpected land hold: {hold.HoldReasonCode}, owner={hold.HoldingTaskId}");
        }
        outcome.ShouldBe(LandRunResult.Complete);
        db.ChangeTracker.Clear();
        var landed = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == child.Id, ct);
        landed.WorktreeBaseSha.ShouldBe(sourceSha);
        landed.WorktreeBaseTaskId.ShouldBe(source.Id);
        var originHead = (await ScratchGitRepo.GitInAsync(remote.Path, "rev-parse", "refs/heads/master"))
            .StdOut.Trim();
        originHead.ShouldNotBe(targetBefore);
        (await ScratchGitRepo.GitInAsync(remote.Path, "show", $"{originHead}:{markerName}"))
            .StdOut.ShouldBe("A\n");
        (await ScratchGitRepo.GitInAsync(remote.Path, "show", $"{originHead}:B.txt"))
            .StdOut.ShouldBe("B\n");
    }

    [Test]
    [Arguments("no_target")]
    [Arguments("explicit_target")]
    public async Task T0442_V27_git_facts_keep_the_creation_base_after_adoption(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-git-facts");
        await repo.CommitFileAsync("README.md", "M\n");
        await repo.GitAsync("checkout", "-b", "source-A");
        await repo.CommitFileAsync("A.txt", "A\n");
        var sourceSha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "master");
        var graph = DelegationTestServices.CreateGitGraph(new GitSettings
        {
            DefaultBranch = "master", WorktreeBasePath = repo.WorktreeRoot,
        });
        var sourceId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "Code B", Goal = "B",
            Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path, RepoPath = repo.Path,
            MergeTargetRef = scenario == "explicit_target" ? "master" : null,
            CreatedAt = DateTime.UtcNow,
        };
        await using (var lease = await graph.Leases.TryAcquireAsync(repo.Path, CancellationToken.None))
        {
            lease.ShouldNotBeNull();
            await graph.Worktrees.CreateForTaskAsync(task, lease!, CancellationToken.None,
                cardBase: new ResolvedBase(sourceSha, WorktreeBaseSource.CardCurrent, null),
                cardBaseTaskId: sourceId, cardBaseBranch: "source-A");
        }
        task.WorktreeBaseSha.ShouldBe(sourceSha);
        task.WorktreeBaseTaskId.ShouldBe(sourceId);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "B.txt"), "B\n");
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "B.txt")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "B")).Ok.ShouldBeTrue();
        var taskHead = (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim();
        var expectedBase = scenario == "explicit_target" ? "master" : sourceSha;
        DelegationGitFacts.ResolveBase(task).ShouldBe(expectedBase);
        var initialCount = (await ScratchGitRepo.GitInAsync(task.WorktreePath!,
            "rev-list", "--count", $"{expectedBase}..HEAD")).StdOut.Trim();
        initialCount.ShouldBe(scenario == "explicit_target" ? "2" : "1");
        var initialFiles = (await ScratchGitRepo.GitInAsync(task.WorktreePath!,
            "diff", "--name-only", $"{expectedBase}..HEAD")).StdOut;
        initialFiles.ShouldContain("B.txt");
        initialFiles.Contains("A.txt", StringComparison.Ordinal)
            .ShouldBe(scenario == "explicit_target");

        var retry = new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = task.Title, Goal = task.Goal,
            Workspace = WorkspaceMode.Worktree, WorkingDirectory = repo.Path, RepoPath = repo.Path,
            MergeTargetRef = task.MergeTargetRef, WorktreeBaseRef = task.WorktreeBaseRef,
            WorktreeBaseSha = task.WorktreeBaseSha, WorktreeBaseSource = task.WorktreeBaseSource,
            WorktreeBaseTaskId = task.WorktreeBaseTaskId,
            WorktreeBaseBranch = task.WorktreeBaseBranch, CreatedAt = task.CreatedAt,
        };
        await graph.Worktrees.CreateForTaskAsync(retry, CancellationToken.None);
        retry.WorktreeBaseSha.ShouldBe(sourceSha);
        retry.WorktreeBaseTaskId.ShouldBe(sourceId);
        DelegationGitFacts.ResolveBase(retry).ShouldBe(expectedBase);
        (await ScratchGitRepo.GitInAsync(retry.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim().ShouldBe(taskHead);
        (await ScratchGitRepo.GitInAsync(retry.WorktreePath!, "rev-list", "--count",
            $"{DelegationGitFacts.ResolveBase(retry)}..HEAD")).StdOut.Trim().ShouldBe(initialCount);
    }

    [Test]
    [Arguments("uncontained_sibling")]
    [Arguments("uncertain_sibling")]
    [Arguments("inspection_failure")]
    [Timeout(180_000)]
    public async Task T0442_V31_land_retains_distinct_sibling_diagnostics(string scenario, CancellationToken ct)
    {
        foreach (var residue in new[] { false, true })
            await RunLandDiagnosticAsync(scenario, residue, ct);
    }

    private static async Task RunLandDiagnosticAsync(string scenario, bool residue, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c442-land-diagnostic");
        using var remote = new TemporaryDirectory("c442-land-diagnostic-origin");
        (await ScratchGitRepo.GitInAsync(remote.Path, "init", "--bare")).Ok.ShouldBeTrue();
        await AgentTaskLandStageOutcomeTests.SeedBuildableAsync(repo);
        await repo.GitAsync("remote", "add", "origin", remote.Path);
        await repo.GitAsync("push", "-u", "origin", "master");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var (land, worktrees) = AgentTaskLandStageOutcomeTests.CreateLand(db, repo);
        var task = await AgentTaskLandStageOutcomeTests.SeedSucceededWorktreeAsync(
            db, worktrees, repo, card.Id);
        var sibling = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(
            db, repo, card.Id, scenario == "uncertain_sibling" ? "L" : "X", startRef: "master");
        if (scenario == "uncertain_sibling")
        {
            var left = (await repo.GitReadAsync("rev-parse", sibling.WorktreeBranch!)).Trim();
            await repo.GitAsync("checkout", "-b", "right-R", "master");
            await repo.CommitFileAsync("right.txt", "R\n");
            var right = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
            await repo.GitAsync("checkout", sibling.WorktreeBranch!);
            await repo.GitAsync("merge", "--no-ff", "right-R", "-m", "join");
            await repo.GitAsync("checkout", "master");
            await repo.GitAsync("cherry-pick", left);
            await repo.GitAsync("cherry-pick", right);
            await repo.GitAsync("push", "origin", "master");
        }
        else if (scenario == "inspection_failure")
        {
            sibling.RepoPath = Path.Combine(repo.WorktreeRoot, "removed-original");
            sibling.WorktreePath = Path.Combine(repo.WorktreeRoot, "removed-checkout");
        }
        await db.SaveChangesAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath!, "B.txt"), "B\n", ct);
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "add", "B.txt")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "commit", "-m", "B")).Ok.ShouldBeTrue();
        if (residue)
            (await ScratchGitRepo.GitInAsync(repo.Path, "worktree", "lock", task.WorktreePath!))
                .Ok.ShouldBeTrue();
        try
        {
            await AgentTaskLandStageOutcomeTests.RequestHeadAsync(land, task);
            (await land.RunAsync(task.Id, null, ct)).ShouldBe(LandRunResult.Complete);
            db.ChangeTracker.Clear();
            var events = await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == task.Id).ToListAsync(ct);
            var terminal = events.Single(e => e.Type == (residue
                ? AgentTaskEventType.LandedWithResidue : AgentTaskEventType.Landed));
            var warnings = events.Where(e => e.Type == AgentTaskEventType.Warning)
                .Select(e => e.Detail).ToArray();
            var token = $"unlanded-sibling={DelegationReportFormatter.Short(sibling.Id)}:{sibling.WorktreeBranch}";
            if (scenario == "uncontained_sibling")
            {
                terminal.Detail.ShouldContain(token);
                warnings.ShouldContain(w => w.Contains(sibling.WorktreeBranch!, StringComparison.Ordinal));
            }
            else
            {
                terminal.Detail.ShouldNotContain("unlanded-sibling=");
                warnings.ShouldContain(w => w.Contains("unknown", StringComparison.OrdinalIgnoreCase)
                    && w.Contains(sibling.WorktreeBranch!, StringComparison.Ordinal));
            }
            (await ScratchGitRepo.GitInAsync(remote.Path, "show", "master:B.txt"))
                .StdOut.ShouldBe("B\n");
        }
        finally
        {
            if (residue)
            {
                (await ScratchGitRepo.GitInAsync(repo.Path, "worktree", "unlock", task.WorktreePath!))
                    .Ok.ShouldBeTrue();
                (await ScratchGitRepo.GitInAsync(repo.Path, "worktree", "remove", "--force", task.WorktreePath!))
                    .Ok.ShouldBeTrue();
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("c442-origin").FullName;

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
