using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AgentTaskWorktreeBaseResolverTests
{
    [Test]
    [Arguments("disjoint_linked_worktree")]
    [Arguments("nested_repository")]
    [Arguments("same_origin_clone")]
    [Arguments("other_card_guid")]
    [Arguments("same_identifier_other_board")]
    public async Task T0442_V03_card_guid_and_git_common_directory_define_identity(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v03");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var sourceCard = scenario is "other_card_guid" or "same_identifier_other_board"
            ? await CardAsync(db) : card;
        if (scenario == "other_card_guid")
        {
            sourceCard.Identifier = "CARD-9999";
            await db.SaveChangesAsync();
        }
        var source = await SourceAsync(db, repo, sourceCard.Id, "A", "master");
        string? foreign = null;
        if (scenario == "disjoint_linked_worktree")
            source.RepoPath = await CheckoutAsync(repo, source);
        else if (scenario is "nested_repository" or "same_origin_clone")
        {
            foreign = scenario == "nested_repository"
                ? Path.Combine(repo.Path, "nested-" + Guid.NewGuid().ToString("N")[..8])
                : Path.Combine(repo.WorktreeRoot, "clone-" + Guid.NewGuid().ToString("N")[..8]);
            (await ScratchGitRepo.GitInAsync(repo.Path, "clone", repo.Path, foreign)).Ok.ShouldBeTrue();
            (await ScratchGitRepo.GitInAsync(foreign, "branch", source.WorktreeBranch!,
                "refs/remotes/origin/" + source.WorktreeBranch)).Ok.ShouldBeTrue();
            source.RepoPath = foreign;
            source.WorktreePath = foreign;
        }
        await db.SaveChangesAsync();
        var selection = await ResolveAsync(db, NewRequest(repo, card.Id));
        if (scenario == "disjoint_linked_worktree")
        {
            selection.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            selection.SourceTaskId.ShouldBe(source.Id);
        }
        else
        {
            selection.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
            selection.SourceTaskId.ShouldBeNull();
            if (scenario is "nested_repository" or "same_origin_clone")
                selection.CandidateWarnings.ShouldContain(w => w.Contains("another Git repository", StringComparison.Ordinal));
            else
                selection.CandidateWarnings.ShouldBeEmpty();
        }
        if (foreign is not null)
            (await repo.GitReadAsync("rev-parse", "master")).Trim()
                .ShouldNotBe((await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim());
    }

    [Test]
    [Arguments("ancestor")]
    [Arguments("linear_patch_equivalent")]
    [Arguments("merge_range")]
    [Arguments("merge_plus_one_tip")]
    [Arguments("merge_plus_two_tips")]
    [Arguments("landed_event")]
    [Arguments("landed_with_residue_event")]
    public async Task T0442_V04_containment_and_completion_evidence_are_distinct(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v04");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var source = await SourceAsync(db, repo, card.Id, "L", "master");
        var firstSha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();
        AgentTask? a = null;
        AgentTask? x = null;
        if (scenario == "ancestor")
            await repo.GitAsync("merge", "--ff-only", source.WorktreeBranch!);
        else if (scenario == "linear_patch_equivalent")
        {
            await repo.CommitFileAsync("target.txt", "T\n");
            await repo.GitAsync("cherry-pick", firstSha);
            (await ScratchGitRepo.GitInAsync(repo.Path, "merge-base", "--is-ancestor",
                firstSha, "master")).Ok.ShouldBeFalse();
            (await repo.GitReadAsync("cherry", "master", source.WorktreeBranch!)).ShouldNotContain("+");
        }
        else
        {
            var right = await SourceAsync(db, repo, card.Id, "R", "master");
            var rightSha = (await repo.GitReadAsync("rev-parse", right.WorktreeBranch!)).Trim();
            // R is fixture construction, not an additional candidate in the decision.
            db.AgentTasks.Remove(right);
            await repo.GitAsync("checkout", source.WorktreeBranch!);
            await repo.GitAsync("merge", "--no-ff", right.WorktreeBranch!, "-m", "merge L and R");
            await repo.GitAsync("checkout", "master");
            await repo.CommitFileAsync("target.txt", "T\n");
            await repo.GitAsync("cherry-pick", firstSha);
            await repo.GitAsync("cherry-pick", rightSha);
            var merges = await repo.GitReadAsync("rev-list", "--max-count=1", "--min-parents=2",
                "master.." + source.WorktreeBranch);
            merges.Trim().ShouldNotBeEmpty();
            (await repo.GitReadAsync("cherry", "master", source.WorktreeBranch!)).ShouldNotContain("+");

            if (scenario is "merge_plus_one_tip" or "merge_plus_two_tips")
                a = await SourceAsync(db, repo, card.Id, "A", "master");
            if (scenario == "merge_plus_two_tips")
                x = await SourceAsync(db, repo, card.Id, "X", "master");
            if (scenario is "landed_event" or "landed_with_residue_event")
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(), AgentTaskId = source.Id,
                    Type = scenario == "landed_event"
                        ? AgentTaskEventType.Landed : AgentTaskEventType.LandedWithResidue,
                    Detail = "fixture completion", At = DateTime.UtcNow,
                });
        }
        await db.SaveChangesAsync();
        var fault = scenario is "landed_event" or "landed_with_residue_event"
            ? new RefLookupFailureGit(source.WorktreeBranch!) : null;
        var auto = await ResolveAsync(db, NewRequest(repo, card.Id), fault);
        switch (scenario)
        {
            case "merge_plus_one_tip":
                auto.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
                auto.SourceTaskId.ShouldBe(a!.Id);
                auto.CandidateWarnings.ShouldContain(w => w.Contains(source.WorktreeBranch!, StringComparison.Ordinal)
                    && w.Contains("unknown", StringComparison.Ordinal));
                break;
            case "merge_plus_two_tips":
                auto.Decision.ShouldBe(CardWorktreeBaseDecision.Ambiguous);
                auto.Reason.ShouldBe("worktree_base_ambiguous");
                auto.CandidateWarnings.ShouldContain(w => w.Contains(a!.WorktreeBranch!, StringComparison.Ordinal)
                    && w.Contains("competing tip", StringComparison.Ordinal));
                auto.CandidateWarnings.ShouldContain(w => w.Contains(x!.WorktreeBranch!, StringComparison.Ordinal)
                    && w.Contains("competing tip", StringComparison.Ordinal));
                auto.CandidateWarnings.ShouldNotContain(w => w.Contains(source.WorktreeBranch!, StringComparison.Ordinal)
                    && w.Contains("competing tip", StringComparison.Ordinal));
                break;
            default:
                auto.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
                auto.SourceTaskId.ShouldBeNull();
                if (scenario == "merge_range")
                    auto.CandidateWarnings.ShouldContain(w => w.Contains(source.WorktreeBranch!, StringComparison.Ordinal)
                        && w.Contains("unknown", StringComparison.Ordinal));
                if (scenario is "landed_event" or "landed_with_residue_event")
                    auto.CandidateWarnings.ShouldBeEmpty();
                break;
        }
        if (scenario is "ancestor" or "linear_patch_equivalent" or "landed_event" or "landed_with_residue_event")
        {
            var explicitRequest = NewRequest(repo, card.Id);
            explicitRequest.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task;
            explicitRequest.RequestedWorktreeBaseTaskId = source.Id;
            var explicitSelection = await ResolveAsync(db, explicitRequest);
            explicitSelection.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            explicitSelection.SourceTaskId.ShouldBe(source.Id);
            explicitSelection.SourceSha.ShouldBe((await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim());
        }
    }

    [Test]
    [Arguments("newer_review_at_ancestor")]
    [Arguments("equal_tip_completion")]
    [Arguments("equal_tip_id_tie")]
    public async Task T0442_V02_tip_order_is_ancestry_then_equal_tip_label(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v02");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var a = await SourceAsync(db, repo, card.Id, "A", "master");
        AgentTask expected;
        if (scenario == "newer_review_at_ancestor")
        {
            var b = await SourceAsync(db, repo, card.Id, "B", a.WorktreeBranch!);
            var review = await SourceAsync(db, repo, card.Id, "review", a.WorktreeBranch!, commit: false);
            review.Role = AgentTaskRole.Review;
            review.CompletedAt = DateTime.UtcNow;
            b.CompletedAt = DateTime.UtcNow.AddMinutes(-10);
            expected = b;
        }
        else
        {
            var alias = await SourceAsync(db, repo, card.Id, "alias", a.WorktreeBranch!, commit: false);
            if (scenario == "equal_tip_completion")
            {
                alias.CompletedAt = DateTime.UtcNow;
                a.CompletedAt = DateTime.UtcNow.AddMinutes(-10);
                expected = alias;
            }
            else
            {
                alias.CompletedAt = a.CompletedAt;
                expected = string.CompareOrdinal(a.Id.ToString("D"), alias.Id.ToString("D")) < 0 ? a : alias;
            }
        }
        await db.SaveChangesAsync();
        var sha = (await repo.GitReadAsync("rev-parse", expected.WorktreeBranch!)).Trim();
        var request = NewRequest(repo, card.Id);
        var first = await ResolveAsync(db, request);
        var second = await ResolveAsync(db, request);
        foreach (var selection in new[] { first, second })
        {
            selection.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            selection.SourceTaskId.ShouldBe(expected.Id);
            selection.SourceSha.ShouldBe(sha);
            selection.CandidateWarnings.ShouldNotContain(w => w.Contains("competing tip", StringComparison.Ordinal));
        }
    }

    [Test]
    [Arguments(AgentTaskStatus.Blocked)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    public async Task T0442_V05_quiescent_non_success_requires_explicit_selection(AgentTaskStatus status)
    {
        using var repo = new ScratchGitRepo("c442-v05");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var source = await SourceAsync(db, repo, card.Id, "A", "master");
        source.Status = status;
        await db.SaveChangesAsync();
        var auto = await ResolveAsync(db, NewRequest(repo, card.Id));
        auto.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
        auto.CandidateWarnings.ShouldContain(w => w.Contains(source.WorktreeBranch!, StringComparison.Ordinal)
            && w.Contains(status.ToString(), StringComparison.Ordinal));
        var request = NewRequest(repo, card.Id);
        request.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task;
        request.RequestedWorktreeBaseTaskId = source.Id;
        var explicitSelection = await ResolveAsync(db, request);
        explicitSelection.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
        explicitSelection.SourceTaskId.ShouldBe(source.Id);
        explicitSelection.SourceSha.ShouldBe((await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim());
    }

    [Test]
    [Arguments("queued_original")]
    [Arguments("dispatched_original")]
    [Arguments("working_original")]
    [Arguments("queued_shared_followup")]
    [Arguments("dispatched_shared_followup")]
    [Arguments("working_shared_followup")]
    public async Task T0442_V06_open_writer_cannot_be_inherited(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v06");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var source = await SourceAsync(db, repo, card.Id, "A", "master");
        var checkout = await CheckoutAsync(repo, source);
        var status = scenario.StartsWith("queued", StringComparison.Ordinal) ? AgentTaskStatus.Queued
            : scenario.StartsWith("dispatched", StringComparison.Ordinal) ? AgentTaskStatus.Dispatched
            : AgentTaskStatus.Working;
        AgentTask? writer = null;
        if (scenario.EndsWith("original", StringComparison.Ordinal))
            source.Status = status;
        else
        {
            writer = new AgentTask
            {
                Id = Guid.NewGuid(), RootTaskId = source.Id, ParentTaskId = source.Id,
                Title = "shared follow-up", Goal = "shared follow-up", Role = AgentTaskRole.Code,
                AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Low,
                Workspace = WorkspaceMode.Shared, WorkingDirectory = checkout,
                WorktreePath = checkout, RepoPath = repo.Path, CardId = card.Id,
                FollowUpOfTaskId = source.Id,
                Status = status, ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
            };
            db.AgentTasks.Add(writer);
        }
        await db.SaveChangesAsync();

        var auto = await ResolveAsync(db, NewRequest(repo, card.Id));
        auto.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
        auto.CandidateWarnings.ShouldContain(w => w.Contains(source.WorktreeBranch!, StringComparison.Ordinal));
        var explicitRequest = NewRequest(repo, card.Id);
        explicitRequest.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task;
        explicitRequest.RequestedWorktreeBaseTaskId = source.Id;
        var refused = await ResolveAsync(db, explicitRequest);
        refused.Decision.ShouldBe(CardWorktreeBaseDecision.Unknown);
        refused.Reason.ShouldBe("requested_source_invalid");

        source.Status = AgentTaskStatus.Succeeded;
        if (writer is not null) writer.Status = AgentTaskStatus.Succeeded;
        await db.SaveChangesAsync();
        (await ResolveAsync(db, NewRequest(repo, card.Id))).SourceTaskId.ShouldBe(source.Id);
    }

    [Test]
    [Arguments("tracked_unstaged")]
    [Arguments("staged")]
    [Arguments("untracked")]
    [Arguments("merge_in_progress")]
    [Arguments("rebase_in_progress")]
    public async Task T0442_V07_dirty_or_in_progress_checkout_is_excluded(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v07");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var source = await SourceAsync(db, repo, card.Id, "A", "master");
        var checkout = await CheckoutAsync(repo, source);
        var head = (await ScratchGitRepo.GitInAsync(checkout, "rev-parse", "HEAD")).StdOut.Trim();
        var change = Path.Combine(checkout, scenario == "untracked" ? "new.txt" : "seed.txt");
        string? operationPath = null;
        switch (scenario)
        {
            case "tracked_unstaged":
                await File.WriteAllTextAsync(change, "modified\n");
                break;
            case "staged":
                await File.WriteAllTextAsync(change, "staged\n");
                (await ScratchGitRepo.GitInAsync(checkout, "add", "seed.txt")).Ok.ShouldBeTrue();
                break;
            case "untracked":
                await File.WriteAllTextAsync(change, "untracked\n");
                break;
            case "merge_in_progress":
                operationPath = (await ScratchGitRepo.GitInAsync(checkout, "rev-parse", "--git-path", "MERGE_HEAD")).StdOut.Trim();
                await File.WriteAllTextAsync(operationPath, head + "\n");
                break;
            case "rebase_in_progress":
                operationPath = (await ScratchGitRepo.GitInAsync(checkout, "rev-parse", "--git-path", "rebase-merge")).StdOut.Trim();
                Directory.CreateDirectory(operationPath);
                break;
        }
        await db.SaveChangesAsync();
        var porcelain = (await ScratchGitRepo.GitInAsync(checkout, "status", "--porcelain", "--untracked-files=all")).StdOut;

        var auto = await ResolveAsync(db, NewRequest(repo, card.Id));
        auto.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
        auto.CandidateWarnings.ShouldContain(w => w.Contains("dirty or in-progress", StringComparison.Ordinal));
        var explicitRequest = NewRequest(repo, card.Id);
        explicitRequest.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task;
        explicitRequest.RequestedWorktreeBaseTaskId = source.Id;
        var refused = await ResolveAsync(db, explicitRequest);
        refused.Decision.ShouldBe(CardWorktreeBaseDecision.Unknown);
        refused.Reason.ShouldBe("requested_source_invalid");
        (await ScratchGitRepo.GitInAsync(checkout, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(head);
        (await ScratchGitRepo.GitInAsync(checkout, "status", "--porcelain", "--untracked-files=all")).StdOut
            .ShouldBe(porcelain);
        if (operationPath is not null)
            (File.Exists(operationPath) || Directory.Exists(operationPath)).ShouldBeTrue();
        else
            (await File.ReadAllTextAsync(change)).ShouldBe(scenario switch
            {
                "tracked_unstaged" => "modified\n",
                "staged" => "staged\n",
                _ => "untracked\n",
            });

        if (operationPath is not null)
        {
            if (File.Exists(operationPath)) File.Delete(operationPath);
            else Directory.Delete(operationPath);
        }
        else if (scenario == "untracked")
            File.Delete(change);
        else
            (await ScratchGitRepo.GitInAsync(checkout, "reset", "--hard", "HEAD")).Ok.ShouldBeTrue();
        (await ResolveAsync(db, NewRequest(repo, card.Id))).SourceTaskId.ShouldBe(source.Id);
    }

    [Test]
    [Arguments("unregistered_local_branch")]
    [Arguments("missing_original_directory")]
    [Arguments("missing_local_branch")]
    [Arguments("remote_only")]
    [Arguments("git_error")]
    public async Task T0442_V08_local_commit_availability_is_explicit(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v08");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var source = await SourceAsync(db, repo, card.Id, "A", "master");
        var sha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();
        if (scenario == "missing_original_directory")
        {
            source.WorktreePath = await CheckoutAsync(repo, source);
            source.RepoPath = Path.Combine(repo.Path, "removed-original");
        }
        else if (scenario is "missing_local_branch" or "remote_only")
        {
            if (scenario == "remote_only")
                await repo.GitAsync("update-ref", "refs/remotes/origin/" + source.WorktreeBranch!, sha);
            await repo.GitAsync("branch", "-D", source.WorktreeBranch!);
        }
        await db.SaveChangesAsync();
        ILandingGit? fault = scenario == "git_error" ? new RefLookupFailureGit(source.WorktreeBranch!) : null;
        var auto = await ResolveAsync(db, NewRequest(repo, card.Id), fault);
        var explicitRequest = NewRequest(repo, card.Id);
        explicitRequest.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task;
        explicitRequest.RequestedWorktreeBaseTaskId = source.Id;
        var explicitSelection = await ResolveAsync(db, explicitRequest, fault);
        if (scenario is "unregistered_local_branch" or "missing_original_directory")
        {
            auto.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            auto.SourceTaskId.ShouldBe(source.Id);
            auto.SourceSha.ShouldBe(sha);
            explicitSelection.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            explicitSelection.SourceSha.ShouldBe(sha);
        }
        else
        {
            auto.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
            auto.CandidateWarnings.ShouldContain(w => w.Contains(source.WorktreeBranch!, StringComparison.Ordinal)
                && w.Contains(scenario == "git_error" ? "unknown" : "no local commit", StringComparison.Ordinal));
            explicitSelection.Decision.ShouldBe(CardWorktreeBaseDecision.Unknown);
            explicitSelection.Reason.ShouldBe("requested_source_invalid");
        }
        if (scenario == "remote_only")
            (await repo.GitReadAsync("rev-parse", "refs/remotes/origin/" + source.WorktreeBranch!)).Trim().ShouldBe(sha);
    }

    [Test]
    [Arguments("excluded_contained")]
    [Arguments("excluded_divergent")]
    public async Task T0442_V09_eligible_tip_keeps_excluded_history_visible(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v09");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        var a = await SourceAsync(db, repo, card.Id, "A", "master");
        var b = await SourceAsync(db, repo, card.Id, "B", a.WorktreeBranch!);
        var excluded = scenario == "excluded_contained"
            ? await SourceAsync(db, repo, card.Id, "alias", a.WorktreeBranch!, commit: false)
            : await SourceAsync(db, repo, card.Id, "X", "master");
        excluded.Status = AgentTaskStatus.Failed;
        await db.SaveChangesAsync();
        var selection = await ResolveAsync(db, NewRequest(repo, card.Id));
        selection.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
        selection.SourceTaskId.ShouldBe(b.Id);
        if (scenario == "excluded_divergent")
            selection.CandidateWarnings.ShouldContain(w => w.Contains(excluded.WorktreeBranch!, StringComparison.Ordinal));
        else
            selection.CandidateWarnings.ShouldNotContain(w => w.Contains(excluded.WorktreeBranch!, StringComparison.Ordinal)
                && w.Contains("not inherited", StringComparison.Ordinal));
    }

    [Test]
    [Arguments("no_card_auto")]
    [Arguments("bound_no_candidates")]
    [Arguments("fresh_target")]
    public async Task T0442_V10_fallback_uses_configured_default_or_explicit_destination(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v10");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await repo.GitAsync("branch", "release");
        await repo.GitAsync("checkout", "-b", "topic");
        await repo.CommitFileAsync("topic.txt", "topic\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = scenario == "no_card_auto" ? null : await CardAsync(db);
        AgentTask? source = null;
        if (scenario == "fresh_target")
        {
            source = await SourceAsync(db, repo, card!.Id, "A", "master");
            await db.SaveChangesAsync();
        }
        foreach (var destination in new string?[] { null, "release" })
        {
            var request = NewRequest(repo, card?.Id);
            request.MergeTargetRef = destination;
            if (scenario == "fresh_target")
                request.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target;
            var selection = await ResolveAsync(db, request);
            selection.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
            selection.FallbackRef.ShouldBe(destination ?? "master"); // CARD-0508 configured default.
            selection.LandingTarget.ShouldBe(destination ?? "master");
            selection.SourceTaskId.ShouldBeNull();
            if (source is not null && destination is null)
                selection.CandidateWarnings.ShouldContain(w => w.Contains(source.WorktreeBranch!, StringComparison.Ordinal));
        }
    }

    [Test]
    [Arguments("six_kept")]
    [Arguments("candidate_cap")]
    [Arguments("git_call_cap")]
    [Arguments("deadline")]
    [Arguments("gate_deadline")]
    [Arguments("explicit_deadline")]
    [Arguments("caller_canceled")]
    public async Task T0442_V29_inspection_budget_never_selects_a_partial_inventory(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v29");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = Context(schema);
        var card = await CardAsync(db);
        AgentTask? newest = null;
        var count = scenario == "candidate_cap" ? 17 : scenario == "six_kept" ? 6 : 1;
        var parent = "master";
        for (var i = 0; i < count; i++)
        {
            newest = await SourceAsync(db, repo, card.Id, "A" + i, parent,
                commit: scenario != "candidate_cap");
            if (scenario == "six_kept") parent = newest.WorktreeBranch!;
        }
        await db.SaveChangesAsync();
        var settings = new GitSettings
        {
            DefaultBranch = "master",
            WorktreeBaseMaxCandidates = 16,
            WorktreeBaseMaxGitCommands = scenario == "git_call_cap" ? 2 : 128,
            WorktreeBaseInspectionTimeoutSeconds = 2,
        };
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var gate = new GitProcessGate(1);
        ILandingGit git = scenario is "deadline" or "explicit_deadline"
            ? new DeadlineGit(clock) : new LandingGit();
        var request = NewRequest(repo, card.Id);
        if (scenario == "explicit_deadline")
        {
            request.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Task;
            request.RequestedWorktreeBaseTaskId = newest!.Id;
        }
        var resolver = new AgentTaskWorktreeBaseResolver(db, git, Options.Create(settings), clock, gate);
        if (scenario == "caller_canceled")
        {
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(
                () => resolver.ResolveAsync(request, canceled.Token));
            gate.Started.ShouldBe(0);
            return;
        }
        CardWorktreeBaseSelection selection;
        if (scenario == "gate_deadline")
        {
            using var held = await gate.EnterAsync(CancellationToken.None);
            var pending = resolver.ResolveAsync(request, CancellationToken.None);
            using var barrierTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (gate.Waiting == 0)
            {
                pending.IsCompleted.ShouldBeFalse("resolution must reach the held Git process gate");
                barrierTimeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            clock.Advance(TimeSpan.FromSeconds(2));
            selection = await pending;
            gate.Started.ShouldBe(1); // only the fixture's lease entered the gate.
        }
        else
            selection = await resolver.ResolveAsync(request, CancellationToken.None);

        if (scenario == "six_kept")
        {
            selection.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            selection.SourceTaskId.ShouldBe(newest!.Id);
            selection.TotalCandidates.ShouldBe(6);
            selection.InspectedCandidates.ShouldBe(6);
            gate.Started.ShouldBeLessThanOrEqualTo(128);
            gate.Started.ShouldBeGreaterThan(0);
        }
        else
        {
            selection.Decision.ShouldBe(CardWorktreeBaseDecision.Unknown);
            selection.SourceTaskId.ShouldBeNull();
            var reason = scenario switch
            {
                "candidate_cap" => "candidate_limit",
                "git_call_cap" => "git_command_limit",
                _ => "inspection_timeout",
            };
            selection.Reason.ShouldBe(reason);
            selection.CandidateWarnings.ShouldContain(w => w.Contains(reason, StringComparison.Ordinal));
            if (scenario == "candidate_cap")
            {
                selection.TotalCandidates.ShouldBe(17);
                selection.InspectedCandidates.ShouldBe(0);
                gate.Started.ShouldBe(0);
            }
            else if (scenario == "git_call_cap")
                gate.Started.ShouldBe(2);
            else if (scenario != "gate_deadline")
                gate.Started.ShouldBe(1);
        }
        if (scenario != "candidate_cap")
            (await repo.GitReadAsync("rev-parse", "master")).Trim()
                .ShouldNotBe((await repo.GitReadAsync("rev-parse", newest!.WorktreeBranch!)).Trim());
    }

    private static AppDbContext Context(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static async Task<Card> CardAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = "c442-" + Guid.NewGuid().ToString("N"),
            GitRepositoryUrl = "https://example.test/c442.git", CreatedAt = now, UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Name = "c442",
            MaxConcurrentSessions = 1, CreatedAt = now, UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, StateKey = "backlog", Name = "Backlog",
            ColumnOrder = 0, CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-0442", Title = "Worktree continuation",
            Description = "Fixture", CreatedAt = now, UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return card;
    }

    private static async Task<AgentTask> SourceAsync(AppDbContext db, ScratchGitRepo repo,
        Guid cardId, string label, string startRef, bool commit = true)
    {
        var id = Guid.NewGuid();
        var branch = "feat/card-task-" + id.ToString("N")[..8];
        await repo.GitAsync("checkout", "-b", branch, startRef);
        if (commit) await repo.CommitFileAsync(label + ".txt", label + "\n");
        await repo.GitAsync("checkout", "master");
        var task = new AgentTask
        {
            Id = id, RootTaskId = id, Title = label, Goal = label,
            Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Low, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repo.Path, RepoPath = repo.Path, CardId = cardId,
            WorktreeBranch = branch, Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            CompletedAt = DateTime.UtcNow.AddMinutes(-30),
        };
        db.AgentTasks.Add(task);
        return task;
    }

    private static AgentTask NewRequest(ScratchGitRepo repo, Guid? cardId) => new()
    {
        Id = Guid.NewGuid(), CardId = cardId, RepoPath = repo.Path,
        Workspace = WorkspaceMode.Worktree,
    };

    private static Task<CardWorktreeBaseSelection> ResolveAsync(AppDbContext db, AgentTask request,
        ILandingGit? git = null) =>
        new AgentTaskWorktreeBaseResolver(db, git ?? new LandingGit(),
            Options.Create(new GitSettings { DefaultBranch = "master" }))
            .ResolveAsync(request, CancellationToken.None);

    private static async Task<string> CheckoutAsync(ScratchGitRepo repo, AgentTask source)
    {
        var checkout = Path.Combine(repo.WorktreeRoot, source.Id.ToString("N"));
        await repo.GitAsync("worktree", "add", checkout, source.WorktreeBranch!);
        source.WorktreePath = checkout;
        return checkout;
    }

    private sealed class RefLookupFailureGit(string branch) : LandingGit
    {
        public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args,
            CancellationToken ct) =>
            args is ["rev-parse", "--verify", "--quiet", var reference]
                && reference == $"refs/heads/{branch}^{{commit}}"
                    ? Task.FromResult(new LandingGitResult(128, "", "injected commit lookup error"))
                    : base.RunAsync(repository, args, ct);
    }

    private sealed class DeadlineGit(FakeTimeProvider clock) : LandingGit
    {
        public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> args,
            CancellationToken ct)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            return Task.FromResult(new LandingGitResult(0, repository, ""));
        }
    }
}
