using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
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
            var shaA = code.WorktreeBaseSha!;
            await world.Repo.GitAsync("checkout", code.WorktreeBranch!);
            await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "code-b.txt"), "B\n");
            await world.Repo.GitAsync("add", "code-b.txt");
            await world.Repo.GitAsync("commit", "-m", "B");
            var shaB = (await world.Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
            await using (var db = world.CreateDb())
            {
                var live = await db.AgentTasks.FindAsync(code.Id);
                live!.WorktreeBaseSha = shaB;
                await db.SaveChangesAsync();
            }

            await world.Repo.GitAsync("checkout", "master");
            await world.Repo.GitAsync("branch", "feat/review-at-a", shaA);
            await using (var db = world.CreateDb())
            {
                var reviewId = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask
                {
                    Id = reviewId, RootTaskId = reviewId, Title = "review at A", Goal = "review at A",
                    Role = AgentTaskRole.Review, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
                    Workspace = WorkspaceMode.Worktree, WorkingDirectory = world.Repo.Path, RepoPath = world.Repo.Path,
                    CardId = world.Card.Id, WorktreeBranch = "feat/review-at-a", WorktreeBaseSha = shaA,
                    Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
                    CreatedAt = DateTime.UtcNow.AddHours(-1), CompletedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            var first = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
            first.Decision.ShouldBe(WorktreeBaseDecisionKind.Continue);
            first.SourceTaskId.ShouldBe(code.Id);
            first.StartSha.ShouldBe(shaB);
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
            var resolution = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
            resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Continue);
            resolution.StartSha.ShouldBe(a.WorktreeBaseSha);
            var expectedId = name == "equal_tip_id_tie"
                ? (string.Compare(a.Id.ToString("D"), copy.Id.ToString("D"), StringComparison.Ordinal) < 0 ? a.Id : copy.Id)
                : copy.Id;
            if (name == "equal_tip_id_tie")
                resolution.SourceTaskId.ShouldBe(expectedId);
            else
                resolution.SourceTaskId.ShouldBe(copy.Id);
        }
    }

    [Test]
    [Arguments("disjoint_linked_worktree")]
    [Arguments("nested_repository")]
    [Arguments("same_origin_clone")]
    [Arguments("other_card_guid")]
    [Arguments("same_identifier_other_board")]
    public async Task T0442_V03(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var local = await world.SeedSucceededAsync("local", "code-a.txt", "A\n");
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        if (name == "nested_repository")
        {
            var nested = Path.Combine(world.Repo.Path, "vendor", "lib");
            Directory.CreateDirectory(nested);
            (await ScratchGitRepo.GitInAsync(nested, "init", "-b", "master")).Ok.ShouldBeTrue();
            (await ScratchGitRepo.GitInAsync(nested, "config", "user.email", "t@t")).Ok.ShouldBeTrue();
            (await ScratchGitRepo.GitInAsync(nested, "config", "user.name", "t")).Ok.ShouldBeTrue();
            await File.WriteAllTextAsync(Path.Combine(nested, "n.txt"), "n\n");
            (await ScratchGitRepo.GitInAsync(nested, "add", "n.txt")).Ok.ShouldBeTrue();
            (await ScratchGitRepo.GitInAsync(nested, "commit", "-m", "n")).Ok.ShouldBeTrue();
            await world.SeedSucceededAsync("nested", "n2.txt", "n2\n", repoPath: nested);
            var nestedOnly = world.NewQueued();
            nestedOnly.RepoPath = world.Repo.Path;
            var resolution = await resolver.ResolveAsync(nestedOnly, CancellationToken.None);
            resolution.SourceTaskId.ShouldBe(local.Id);
            return;
        }

        if (name == "same_origin_clone")
        {
            var clone = Directory.CreateTempSubdirectory("c442-clone").FullName;
            try
            {
                (await ScratchGitRepo.GitInAsync(world.Repo.Path, "clone", world.Repo.Path, clone)).Ok.ShouldBeTrue();
                await world.SeedSucceededAsync("clone", "c.txt", "c\n", repoPath: clone);
                var resolution = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
                resolution.SourceTaskId.ShouldBe(local.Id);
            }
            finally
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(clone, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(clone, true);
                }
                catch (Exception) { /* git objects can stay read-only on Windows */ }
            }

            return;
        }

        if (name is "other_card_guid" or "same_identifier_other_board")
        {
            var other = await world.SeedOtherCardAsync();
            await using var db = world.CreateDb();
            local = await db.AgentTasks.FindAsync(local.Id);
            local!.CardId = other.Id;
            await db.SaveChangesAsync();
            var resolution = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
            resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
            resolution.SourceTaskId.ShouldBeNull();
            return;
        }

        var linked = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        linked.SourceTaskId.ShouldBe(local.Id);
    }

    [Test]
    [Arguments("ancestor")]
    [Arguments("linear_patch_equivalent")]
    [Arguments("merge_range")]
    [Arguments("merge_plus_one_tip")]
    [Arguments("merge_plus_two_tips")]
    [Arguments("landed_event")]
    [Arguments("landed_with_residue_event")]
    public async Task T0442_V04(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        if (name == "linear_patch_equivalent")
        {
            var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
            await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "other.txt"), "o\n");
            await world.Repo.GitAsync("add", "other.txt");
            await world.Repo.GitAsync("commit", "-m", "other");
            await world.Repo.GitAsync("cherry-pick", a.WorktreeBaseSha!);
            var resolution = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
            resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
            var chosen = await resolver.ResolveAsync(world.NewQueued(AgentTaskWorktreeBaseMode.Task, a.Id), CancellationToken.None);
            chosen.Decision.ShouldBe(WorktreeBaseDecisionKind.Continue);
            chosen.StartSha.ShouldBe(a.WorktreeBaseSha);
            return;
        }

        if (name.StartsWith("merge", StringComparison.Ordinal))
        {
            var merge = await world.SeedMergeRangeAsync();
            AgentTask? extraA = null;
            AgentTask? extraX = null;
            if (name is "merge_plus_one_tip" or "merge_plus_two_tips")
                extraA = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
            if (name == "merge_plus_two_tips")
                extraX = await world.SeedSucceededAsync("X", "x.txt", "X\n");
            var resolution = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
            if (name == "merge_range")
            {
                resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
                resolution.Preview.Warnings.ShouldContain(w => w.Contains("merge_range", StringComparison.OrdinalIgnoreCase)
                    || w.Contains("uncertain", StringComparison.OrdinalIgnoreCase));
            }
            else if (name == "merge_plus_one_tip")
            {
                resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Continue);
                resolution.SourceTaskId.ShouldBe(extraA!.Id);
            }
            else
            {
                resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Ambiguous);
                resolution.Reason.ShouldBe(AgentTaskWorktreeBaseResolver.AmbiguousCode);
                var ids = resolution.Preview.Candidates!.Select(c => c.TaskId).ToList();
                ids.ShouldContain(extraA!.Id);
                ids.ShouldContain(extraX!.Id);
            }

            merge.Id.ShouldNotBe(Guid.Empty);
            return;
        }

        var source = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        await world.Repo.GitAsync("merge", "--ff-only", source.WorktreeBranch!);
        if (name.StartsWith("landed", StringComparison.Ordinal))
        {
            await world.SeedLandedAsync(source.Id, name.Contains("residue", StringComparison.Ordinal)
                ? AgentTaskEventType.LandedWithResidue
                : AgentTaskEventType.Landed);
        }

        var auto = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        auto.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
        var explicitTask = await resolver.ResolveAsync(world.NewQueued(AgentTaskWorktreeBaseMode.Task, source.Id), CancellationToken.None);
        explicitTask.Decision.ShouldBe(WorktreeBaseDecisionKind.Continue);
        explicitTask.StartSha.ShouldBe(source.WorktreeBaseSha);
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
        auto.Preview.Warnings!.ShouldContain(w => w.Contains(DelegationReportFormatter.Short(a.Id), StringComparison.Ordinal));
        var explicitTask = await resolver.ResolveAsync(world.NewQueued(AgentTaskWorktreeBaseMode.Task, a.Id), CancellationToken.None);
        explicitTask.Decision.ShouldBe(WorktreeBaseDecisionKind.Continue);
        explicitTask.StartSha.ShouldBe(a.WorktreeBaseSha);
    }

    [Test]
    [Arguments("queued_original")]
    [Arguments("dispatched_original")]
    [Arguments("working_original")]
    [Arguments("queued_shared_followup")]
    [Arguments("dispatched_shared_followup")]
    [Arguments("working_shared_followup")]
    public async Task T0442_V06(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var originalStatus = name.Contains("shared", StringComparison.Ordinal)
            ? AgentTaskStatus.Succeeded
            : name switch
            {
                "queued_original" => AgentTaskStatus.Queued,
                "dispatched_original" => AgentTaskStatus.Dispatched,
                _ => AgentTaskStatus.Working,
            };
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n", status: originalStatus);
        if (name.Contains("shared", StringComparison.Ordinal))
        {
            var followStatus = name switch
            {
                "queued_shared_followup" => AgentTaskStatus.Queued,
                "dispatched_shared_followup" => AgentTaskStatus.Dispatched,
                _ => AgentTaskStatus.Working,
            };
            await using var db = world.CreateDb();
            var follow = Guid.NewGuid();
            db.AgentTasks.Add(new Antiphon.Server.Domain.Entities.AgentTask
            {
                Id = follow, RootTaskId = follow, Title = "follow", Goal = "follow",
                Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared, WorkingDirectory = world.Repo.Path, RepoPath = world.Repo.Path,
                CardId = world.Card.Id, FollowUpOfTaskId = a.Id, Status = followStatus,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var auto = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        auto.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
        var explicitTask = await resolver.ResolveAsync(world.NewQueued(AgentTaskWorktreeBaseMode.Task, a.Id), CancellationToken.None);
        explicitTask.Decision.ShouldBe(WorktreeBaseDecisionKind.Invalid);
    }

    [Test]
    [Arguments("tracked_unstaged")]
    [Arguments("staged")]
    [Arguments("untracked")]
    [Arguments("merge_in_progress")]
    [Arguments("rebase_in_progress")]
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

        switch (name)
        {
            case "tracked_unstaged":
                await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "README.md"), "dirty\n");
                break;
            case "staged":
                await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "staged.txt"), "s\n");
                await world.Repo.GitAsync("add", "staged.txt");
                break;
            case "untracked":
                await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "scratch.txt"), "u\n");
                break;
            case "merge_in_progress":
                await world.Repo.GitAsync("checkout", "-b", "other");
                await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "other.txt"), "o\n");
                await world.Repo.GitAsync("add", "other.txt");
                await world.Repo.GitAsync("commit", "-m", "other");
                await world.Repo.GitAsync("checkout", "master");
                await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "other.txt"), "conflict\n");
                await world.Repo.GitAsync("add", "other.txt");
                await world.Repo.GitAsync("commit", "-m", "master-other");
                var merge = await ScratchGitRepo.GitInAsync(world.Repo.Path, "merge", "--no-commit", "other");
                merge.Ok.ShouldBeFalse();
                break;
            default:
                await world.Repo.GitAsync("checkout", "-b", "topic");
                await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "topic.txt"), "t\n");
                await world.Repo.GitAsync("add", "topic.txt");
                await world.Repo.GitAsync("commit", "-m", "topic");
                await world.Repo.GitAsync("checkout", "master");
                await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "topic.txt"), "m\n");
                await world.Repo.GitAsync("add", "topic.txt");
                await world.Repo.GitAsync("commit", "-m", "master-topic");
                var rebase = await ScratchGitRepo.GitInAsync(world.Repo.Path, "rebase", "topic");
                rebase.Ok.ShouldBeFalse();
                break;
        }

        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var auto = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        auto.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
        var explicitTask = await resolver.ResolveAsync(world.NewQueued(AgentTaskWorktreeBaseMode.Task, a.Id), CancellationToken.None);
        explicitTask.Decision.ShouldBe(WorktreeBaseDecisionKind.Invalid);
    }

    [Test]
    [Arguments("unregistered_local_branch")]
    [Arguments("missing_original_directory")]
    [Arguments("missing_local_branch")]
    [Arguments("remote_only")]
    [Arguments("git_error")]
    public async Task T0442_V08(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        if (name == "missing_local_branch")
            await world.Repo.GitAsync("branch", "-D", a.WorktreeBranch!);
        if (name == "remote_only")
        {
            var sha = a.WorktreeBaseSha!;
            await world.Repo.GitAsync("update-ref", $"refs/remotes/origin/{a.WorktreeBranch}", sha);
            await world.Repo.GitAsync("branch", "-D", a.WorktreeBranch!);
        }

        if (name == "git_error")
        {
            await using var db = world.CreateDb();
            var live = await db.AgentTasks.FindAsync(a.Id);
            live!.RepoPath = Path.Combine(world.Repo.Path, "missing-git");
            live.WorktreePath = live.RepoPath;
            live.WorkingDirectory = live.RepoPath;
            await db.SaveChangesAsync();
        }

        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var resolution = await resolver.ResolveAsync(world.NewQueued(), CancellationToken.None);
        if (name is "unregistered_local_branch" or "missing_original_directory")
            resolution.SourceTaskId.ShouldBe(a.Id);
        else
            resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);

        if (name is "missing_local_branch" or "remote_only" or "git_error")
        {
            var explicitTask = await resolver.ResolveAsync(world.NewQueued(AgentTaskWorktreeBaseMode.Task, a.Id), CancellationToken.None);
            explicitTask.Decision.ShouldBe(WorktreeBaseDecisionKind.Invalid);
        }
    }

    [Test]
    [Arguments("excluded_contained")]
    [Arguments("excluded_divergent")]
    public async Task T0442_V09(string name)
    {
        await using var world = await WorktreeContinuityHarness.CreateAsync();
        var a = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
        var b = await world.SeedSucceededAsync("B", "code-b.txt", "B\n", fromBranch: name == "excluded_contained" ? a.WorktreeBranch : null);
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
        await world.Repo.GitAsync("checkout", "-b", "topic");
        await File.WriteAllTextAsync(Path.Combine(world.Repo.Path, "topic.txt"), "t\n");
        await world.Repo.GitAsync("add", "topic.txt");
        await world.Repo.GitAsync("commit", "-m", "topic");
        var topicHead = (await world.Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var queued = world.NewQueued(name == "fresh_target" ? AgentTaskWorktreeBaseMode.Target : AgentTaskWorktreeBaseMode.Auto);
        if (name == "no_card_auto") queued.CardId = null;
        var resolution = await resolver.ResolveAsync(queued, CancellationToken.None);
        resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
        resolution.SourceTaskId.ShouldBeNull();
        if (name == "fresh_target")
            resolution.Preview.Warnings.ShouldContain(w => w.Contains("omitted", StringComparison.OrdinalIgnoreCase));
        if (name != "bound_no_candidates")
        {
            queued.MergeTargetRef = "release";
            await world.Repo.GitAsync("branch", "release", "master");
            var release = await resolver.ResolveAsync(queued, CancellationToken.None);
            release.Decision.ShouldBe(WorktreeBaseDecisionKind.Target);
        }

        topicHead.ShouldNotBeNullOrEmpty();
    }

    [Test]
    [Arguments("six_kept")]
    [Arguments("candidate_cap")]
    [Arguments("git_call_cap")]
    [Arguments("deadline")]
    [Arguments("gate_deadline")]
    [Arguments("explicit_deadline")]
    [Arguments("caller_canceled")]
    public async Task T0442_V29(string name)
    {
        if (name == "caller_canceled")
        {
            await using var world = await WorktreeContinuityHarness.CreateAsync();
            await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
            var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => resolver.ResolveAsync(world.NewQueued(), cts.Token));
            return;
        }

        if (name is "deadline" or "explicit_deadline" or "gate_deadline")
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var gate = new GitProcessGate(maxConcurrentProcesses: 1);
            var settings = new GitSettings
            {
                WorktreeBasePath = Path.GetTempPath(),
                WorktreeBaseMaxCandidates = 16,
                WorktreeBaseMaxGitCommands = 128,
                WorktreeBaseInspectionTimeoutSeconds = 2,
            };
            await using var world = await WorktreeContinuityHarness.CreateAsync(settings, clock: clock, gate: gate);
            var source = await world.SeedSucceededAsync("A", "code-a.txt", "A\n");
            var held = await gate.EnterAsync(CancellationToken.None);
            try
            {
                var resolver = world.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
                var queued = name == "explicit_deadline"
                    ? world.NewQueued(AgentTaskWorktreeBaseMode.Task, source.Id)
                    : world.NewQueued();
                var resolve = Task.Run(() => resolver.ResolveAsync(queued, CancellationToken.None));
                var waited = DateTime.UtcNow;
                while (gate.Waiting == 0 && DateTime.UtcNow - waited < TimeSpan.FromSeconds(5))
                    await Task.Delay(20);
                gate.Waiting.ShouldBeGreaterThan(0);
                clock.Advance(TimeSpan.FromSeconds(2));
                var resolution = await resolve;
                if (name == "explicit_deadline")
                    resolution.Decision.ShouldBe(WorktreeBaseDecisionKind.Invalid);
                else
                {
                    resolution.Decision.ShouldBeOneOf(WorktreeBaseDecisionKind.Incomplete, WorktreeBaseDecisionKind.Target);
                    resolution.Reason.ShouldBe("inspection_timeout");
                    resolution.SourceTaskId.ShouldBeNull();
                }
            }
            finally
            {
                held?.Dispose();
            }

            return;
        }

        var capSettings = new GitSettings
        {
            WorktreeBasePath = Path.GetTempPath(),
            WorktreeBaseMaxCandidates = name == "candidate_cap" ? 2 : 16,
            WorktreeBaseMaxGitCommands = name == "git_call_cap" ? 3 : 128,
            WorktreeBaseInspectionTimeoutSeconds = name == "six_kept" ? 30 : 2,
        };
        await using var capped = await WorktreeContinuityHarness.CreateAsync(capSettings);
        AgentTask? previous = null;
        var count = name == "six_kept" ? 6 : 3;
        for (var i = 0; i < count; i++)
        {
            previous = await capped.SeedSucceededAsync($"c{i}", $"f{i}.txt", $"{i}\n",
                fromBranch: previous?.WorktreeBranch);
        }

        var resolver2 = capped.Services.GetRequiredService<AgentTaskWorktreeBaseResolver>();
        var resolution2 = await resolver2.ResolveAsync(capped.NewQueued(), CancellationToken.None);
        if (name == "candidate_cap")
        {
            resolution2.Reason.ShouldBe("candidate_limit");
            resolution2.Decision.ShouldBeOneOf(WorktreeBaseDecisionKind.Incomplete, WorktreeBaseDecisionKind.Target);
            resolution2.SourceTaskId.ShouldBeNull();
        }
        else if (name == "git_call_cap")
        {
            resolution2.Reason.ShouldBe("git_command_limit");
            resolution2.Preview.CommandCount.ShouldBeLessThanOrEqualTo(3);
        }
        else
        {
            resolution2.Preview.CommandCount.ShouldBeLessThanOrEqualTo(128);
            resolution2.Decision.ShouldBeOneOf(WorktreeBaseDecisionKind.Continue, WorktreeBaseDecisionKind.Ambiguous);
            if (resolution2.Decision == WorktreeBaseDecisionKind.Continue)
                resolution2.SourceTaskId.ShouldBe(previous!.Id);
        }
    }
}
