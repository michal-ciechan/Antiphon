using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskWorktreeBaseResolverTests
{
    [Test]
    [Arguments("newer_review_at_ancestor")]
    [Arguments("equal_tip_completion")]
    [Arguments("equal_tip_id_tie")]
    public async Task T0442_V02(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        if (name == "newer_review_at_ancestor")
        {
            var code = await world.SeedSucceededAsync("code A then B", "code-a.txt", "A\n");
            await world.Repo.GitAsync("checkout", code.WorktreeBranch!);
            await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "code-b.txt"), "B\n");
            await world.Repo.GitAsync("add", "code-b.txt");
            await world.Repo.GitAsync("commit", "-m", "B");
            await world.Repo.GitAsync("checkout", "master");
            var review = await world.SeedSucceededAsync("review at A", "review.txt", "r\n", AgentTaskRole.Review,
                completedAt: DateTime.UtcNow);
            var queued = world.NewQueued();
            var first = await resolver.ResolveAsync(queued, CancellationToken.None);
            first.Decision.ShouldBeOneOf(WorktreeBaseDecisionKind.Continue, WorktreeBaseDecisionKind.Ambiguous);
            return;
        }

        var a = await world.SeedSucceededAsync("one", "code-a.txt", "A\n", completedAt: DateTime.UtcNow.AddMinutes(-2));
        await using (var db = world.CreateDb())
        {
            var copy = await world.SeedSucceededAsync("copy", "other.txt", "x\n", completedAt: DateTime.UtcNow.AddMinutes(-1));
            var live = await db.AgentTasks.FindAsync(copy.Id);
            live!.WorktreeBranch = a.WorktreeBranch;
            live.WorktreeBaseSha = a.WorktreeBaseSha;
            if (name == "equal_tip_id_tie")
                live.CompletedAt = a.CompletedAt;
            await db.SaveChangesAsync();
            var queued = world.NewQueued();
            var resolution = await resolver.ResolveAsync(queued, CancellationToken.None);
            resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Continue);
            resolution.StartSha.ShouldBe(a.WorktreeBaseSha);
        }
    }

    [Test]
    [Arguments("disjoint_linked_worktree")]
    [Arguments("other_card_guid")]
    [Arguments("same_identifier_other_board")]
    public async Task T0442_V03(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var local = await world.SeedSucceededAsync("local", "code-a.txt", "A\n");
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var queued = world.NewQueued();
        if (name is "other_card_guid" or "same_identifier_other_board")
        {
            await using var db = world.CreateDb();
            local = await db.AgentTasks.FindAsync(local.Id);
            local!.CardId = null;
            await db.SaveChangesAsync();
        }

        var resolution = await resolver.ResolveAsync(queued, CancellationToken.None);
        if (name == "disjoint_linked_worktree")
            resolution.SourceTaskId.ShouldBe(local.Id);
        else
            resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
    }

    [Test]
    [Arguments("ancestor")]
    [Arguments("landed_event")]
    [Arguments("landed_with_residue_event")]
    public async Task T0442_V04(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        await world.Repo.GitAsync("merge", "--ff-only", a.WorktreeBranch!);
        if (name.StartsWith("landed", StringComparison.Ordinal))
        {
            await using var db = world.CreateDb();
            db.AgentTaskEvents.Add(new Antiphon.Server.Domain.Entities.AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = a.Id,
                Type = name.Contains("residue", StringComparison.Ordinal)
                    ? AgentTaskEventType.LandedWithResidue
                    : AgentTaskEventType.Landed,
                Detail = "landed", At = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var resolution = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
        var explicitTask = world.NewQueued(AgentTaskWorktreeBaseMode.Task, a.Id);
        var chosen = await resolver.ResolveAsync(explicitTask, CancellationToken.None);
        chosen.Decision.ShouldBe(WorktreeBaseDecisionKind.Continue);
        chosen.StartSha.ShouldBe(a.WorktreeBaseSha);
    }

    [Test]
    [Arguments("blocked")]
    [Arguments("failed")]
    [Arguments("canceled")]
    public async Task T0442_V05(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var status = name switch
        {
            "blocked" => AgentTaskStatus.Blocked,
            "failed" => AgentTaskStatus.Failed,
            _ => AgentTaskStatus.Canceled,
        };
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n", status: status);
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var auto = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        auto.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
        auto.Preview.Warnings.ShouldNotBeNull();
        var explicitTask = await resolver.ResolveAsync(world.NewQueued(AgentTaskWorktreeBaseMode.Task, a.Id), CancellationToken.None);
        explicitTask.Decision.ShouldBe(WorktreeBaseDecisionKind.Continue);
    }

    [Test]
    [Arguments("queued_original")]
    [Arguments("dispatched_original")]
    [Arguments("working_original")]
    public async Task T0442_V06(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var status = name switch
        {
            "queued_original" => AgentTaskStatus.Queued,
            "dispatched_original" => AgentTaskStatus.Dispatched,
            _ => AgentTaskStatus.Working,
        };
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n", status: status);
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var auto = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        auto.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
        var explicitTask = await resolver.ResolveAsync(world.NewQueued(AgentTaskWorktreeBaseMode.Task, a.Id), CancellationToken.None);
        explicitTask.Decision.ShouldBe(WorktreeBaseDecisionKind.Invalid);
    }

    [Test]
    [Arguments("tracked_unstaged")]
    [Arguments("untracked")]
    public async Task T0442_V07(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        await using (var db = world.CreateDb())
        {
            var live = await db.AgentTasks.FindAsync(a.Id);
            live!.WorktreePath = world.Repo.Path;
            await db.SaveChangesAsync();
        }

        if (name == "tracked_unstaged")
            await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "README.md"), "dirty\n");
        else
            await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "scratch.txt"), "u\n");

        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var auto = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        auto.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
        var explicitTask = await resolver.ResolveAsync(world.NewQueued(AgentTaskWorktreeBaseMode.Task, a.Id), CancellationToken.None);
        explicitTask.Decision.ShouldBe(WorktreeBaseDecisionKind.Invalid);
    }

    [Test]
    [Arguments("unregistered_local_branch")]
    [Arguments("missing_local_branch")]
    public async Task T0442_V08(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        if (name == "missing_local_branch")
            await world.Repo.GitAsync("branch", "-D", a.WorktreeBranch!);
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var resolution = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        if (name == "unregistered_local_branch")
            resolution.SourceTaskId.ShouldBe(a.Id);
        else
            resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
    }

    [Test]
    [Arguments("excluded_contained")]
    [Arguments("excluded_divergent")]
    public async Task T0442_V09(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var b = await world.SeedSucceededAsync("B", "code-b.txt", "B\n");
        if (name == "excluded_contained")
            await world.Repo.GitAsync("merge", "--ff-only", a.WorktreeBranch!);
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var resolution = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        if (name == "excluded_contained")
            resolution.SourceTaskId.ShouldBe(b.Id);
        else
            resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Ambiguous);
    }

    [Test]
    [Arguments("no_card_auto")]
    [Arguments("bound_no_candidates")]
    [Arguments("fresh_target")]
    public async Task T0442_V10(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        if (name != "bound_no_candidates")
            await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var queued = world.NewQueued(name == "fresh_target" ? AgentTaskWorktreeBaseMode.Target : AgentTaskWorktreeBaseMode.Auto);
        if (name == "no_card_auto") queued.CardId = null;
        var resolution = await resolver.ResolveAsync(queued, CancellationToken.None);
        resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
        if (name == "fresh_target")
            resolution.SourceTaskId.ShouldBeNull();
    }

    [Test]
    [Arguments("six_kept")]
    [Arguments("candidate_cap")]
    [Arguments("caller_canceled")]
    public async Task T0442_V29(string name)
    {
        var settings = new Antiphon.Server.Application.Settings.GitSettings
        {
            WorktreeBasePath = Path.GetTempPath(),
            WorktreeBaseMaxCandidates = name == "candidate_cap" ? 2 : 16,
            WorktreeBaseMaxGitCommands = 128,
            WorktreeBaseInspectionTimeoutSeconds = 2,
        };
        await using var world = await WorktreeContinuityHarness.CreateAsync(settings);
        for (var i = 0; i < (name == "six_kept" ? 6 : 3); i++)
            await world.SeedSucceededAsync($"c{i}", $"f{i}.txt", $"{i}\n");
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        if (name == "caller_canceled")
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => resolver.ResolveAsync(world.NewQueued(), cts.Token));
            return;
        }

        var resolution = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        if (name == "candidate_cap")
            resolution.Reason.ShouldBe("candidate_limit");
        else
            resolution.Decision.ShouldBeOneOf(WorktreeBaseDecisionKind.Continue, WorktreeBaseDecisionKind.Ambiguous, WorktreeBaseDecisionKind.Incomplete);
        resolution.Preview.CommandCount.ShouldBeLessThanOrEqualTo(128);
    }
}
