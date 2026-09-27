using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AgentTaskWorktreeBaseCreateTests
{
    [Test]
    [Arguments("both_flags")]
    [Arguments("shared")]
    [Arguments("readonly")]
    [Arguments("onagent_task")]
    [Arguments("onagent_fresh")]
    [Arguments("task_without_card")]
    public async Task T0442_V11_invalid_override_shape_refuses_before_insert(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v11");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var source = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "A");
        await db.SaveChangesAsync();
        var before = await db.AgentTasks.CountAsync();
        var shortId = DelegationReportFormatter.Short(source.Id);
        var request = Request(scenario == "task_without_card" ? null : card.Id);
        request = scenario switch
        {
            "both_flags" => request with { WorktreeBaseTask = shortId, FreshWorktree = true },
            "shared" => request with { Workspace = WorkspaceMode.Shared, FreshWorktree = true },
            "readonly" => request with { Workspace = WorkspaceMode.ReadOnly, FreshWorktree = true },
            "onagent_task" => request with { AgentId = Guid.NewGuid(), WorktreeBaseTask = shortId },
            "onagent_fresh" => request with { AgentId = Guid.NewGuid(), FreshWorktree = true },
            "task_without_card" => request with { WorktreeBaseTask = shortId },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var error = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(request,
            new AgentTaskService.Caller(null, null, repo.Path), CancellationToken.None));
        error.Code.ShouldBe(scenario == "task_without_card" ? "worktree_base_card_required" : "worktree_base_mode");
        (await db.AgentTasks.CountAsync()).ShouldBe(before);
        (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim().ShouldNotBeEmpty();
    }

    [Test]
    [Arguments("cross_card")]
    [Arguments("cross_board")]
    [Arguments("nested_repo")]
    [Arguments("separate_clone")]
    [Arguments("destination_mismatch")]
    public async Task T0442_V12_explicit_source_cannot_cross_custody_or_destination(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v12");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await repo.GitAsync("branch", "release");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var sourceCard = scenario is "cross_card" or "cross_board"
            ? await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db,
                scenario == "cross_card" ? "CARD-0999" : "CARD-0442")
            : card;
        var source = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo,
            sourceCard.Id, "A");
        var sourceSha = (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim();
        if (scenario is "nested_repo" or "separate_clone")
        {
            var foreign = scenario == "nested_repo"
                ? Path.Combine(repo.Path, "nested-" + Guid.NewGuid().ToString("N")[..8])
                : Path.Combine(repo.WorktreeRoot, "clone-" + Guid.NewGuid().ToString("N")[..8]);
            (await ScratchGitRepo.GitInAsync(repo.Path, "clone", repo.Path, foreign)).Ok.ShouldBeTrue();
            source.RepoPath = foreign;
        }
        if (scenario == "destination_mismatch") source.MergeTargetRef = "release";
        await db.SaveChangesAsync();
        var before = await db.AgentTasks.CountAsync();
        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var request = Request(card.Id) with
        {
            WorktreeBaseTask = DelegationReportFormatter.Short(source.Id),
            MergeTargetRef = scenario == "destination_mismatch" ? "master" : null,
        };
        var error = await Should.ThrowAsync<ValidationException>(() => service.CreateAsync(request,
            new AgentTaskService.Caller(null, null, repo.Path), CancellationToken.None));
        error.Code.ShouldBe("worktree_base_source_invalid");
        (await db.AgentTasks.CountAsync()).ShouldBe(before);
        (await repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim().ShouldBe(sourceSha);

        if (scenario == "destination_mismatch")
        {
            source.MergeTargetRef = null;
            await db.SaveChangesAsync();
            var accepted = await service.CreateAsync(request,
                new AgentTaskService.Caller(null, null, repo.Path), CancellationToken.None);
            accepted.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            accepted.WorktreeBase.SourceTaskId.ShouldBe(source.Id);
        }
    }

    [Test]
    [Arguments("full_guid")]
    [Arguments("unique_short")]
    [Arguments("missing")]
    [Arguments("ambiguous_short")]
    public async Task T0442_V13_source_identifier_has_existing_guid_and_short_id_rules(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v13");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var source = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "A");
        var id = scenario == "full_guid" ? source.Id.ToString("D")
            : scenario == "unique_short" ? DelegationReportFormatter.Short(source.Id)
            : scenario == "missing" ? "deadbeef" : "aaaaaaaa";
        if (scenario == "ambiguous_short")
        {
            foreach (var suffix in new[] { "0000-0000-0000-000000000001",
                         "0000-0000-0000-000000000002" })
            {
                var otherId = Guid.Parse("aaaaaaaa-" + suffix);
                db.AgentTasks.Add(new AgentTask
                {
                    Id = otherId, RootTaskId = otherId, Title = "same short id",
                    Goal = "same short id", Role = AgentTaskRole.Code,
                    AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Low,
                    Workspace = WorkspaceMode.Worktree, WorkingDirectory = repo.Path,
                    RepoPath = repo.Path, CardId = card.Id,
                    Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.None,
                    CreatedAt = DateTime.UtcNow.AddHours(-1), CompletedAt = DateTime.UtcNow,
                });
            }
        }
        await db.SaveChangesAsync();
        var before = await db.AgentTasks.CountAsync();
        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var request = Request(card.Id) with { WorktreeBaseTask = id };
        var caller = new AgentTaskService.Caller(null, null, repo.Path);
        if (scenario == "missing")
            await Should.ThrowAsync<NotFoundException>(() => service.CreateAsync(request, caller, CancellationToken.None));
        else if (scenario == "ambiguous_short")
        {
            var error = await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(request, caller,
                CancellationToken.None));
            error.Message.ShouldContain("more than one task");
        }
        else
        {
            var created = await service.CreateAsync(request, caller, CancellationToken.None);
            created.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            created.WorktreeBase.SourceTaskId.ShouldBe(source.Id);
            db.ChangeTracker.Clear();
            (await db.AgentTasks.SingleAsync(t => t.Id == created.Id))
                .RequestedWorktreeBaseTaskId.ShouldBe(source.Id);
        }
        (await db.AgentTasks.CountAsync()).ShouldBe(before + (scenario is "full_guid" or "unique_short" ? 1 : 0));
    }

    [Test]
    [Arguments("two_tips")]
    [Arguments("four_tips")]
    public async Task T0442_V14_divergence_refuses_before_create_and_both_overrides_work(string scenario)
    {
        using var repo = new ScratchGitRepo("c442-v14");
        await repo.CommitFileAsync("seed.txt", "M\n");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = AgentTaskDispatchBaseGuardTests.CreateContext(schema);
        var card = await AgentTaskDispatchBaseGuardTests.SeedCardAsync(db, "CARD-0442");
        var a = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "A", startRef: "master");
        var x = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "X", startRef: "master");
        AgentTask? b = null;
        AgentTask? y = null;
        if (scenario == "four_tips")
        {
            b = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "B",
                startRef: a.WorktreeBranch);
            y = await AgentTaskDispatchBaseGuardTests.SeedKeptSiblingAsync(db, repo, card.Id, "Y",
                startRef: x.WorktreeBranch);
            a.CompletedAt = DateTime.UtcNow;
            b.CompletedAt = DateTime.UtcNow.AddMinutes(-10);
        }
        await db.SaveChangesAsync();
        var maxima = new[] { b ?? a, y ?? x };
        var before = await db.AgentTasks.CountAsync();
        await using var provider = AgentTaskDispatchBaseGuardTests.CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTaskService>();
        var request = Request(card.Id);
        var caller = new AgentTaskService.Caller(null, null, repo.Path);
        var conflict = await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(request, caller,
            CancellationToken.None));
        conflict.Code.ShouldBe("worktree_base_ambiguous");
        foreach (var tip in maxima)
        {
            conflict.Message.ShouldContain(DelegationReportFormatter.Short(tip.Id));
            conflict.Message.ShouldContain(tip.WorktreeBranch!);
            conflict.Message.ShouldContain((await repo.GitReadAsync("rev-parse", tip.WorktreeBranch!)).Trim());
        }
        conflict.Message.ShouldContain("-BaseTask");
        conflict.Message.ShouldContain("-FreshWorktree");
        (await db.AgentTasks.CountAsync()).ShouldBe(before);
        foreach (var tip in maxima)
        {
            var selected = await service.CreateAsync(request with
            {
                WorktreeBaseTask = DelegationReportFormatter.Short(tip.Id),
            }, caller, CancellationToken.None);
            selected.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Continue);
            selected.WorktreeBase.SourceTaskId.ShouldBe(tip.Id);
        }
        var fresh = await service.CreateAsync(request with { FreshWorktree = true }, caller,
            CancellationToken.None);
        fresh.WorktreeBase!.Decision.ShouldBe(CardWorktreeBaseDecision.Target);
        fresh.WorktreeBase.CandidateWarnings.ShouldContain(w => w.Contains(maxima[0].WorktreeBranch!, StringComparison.Ordinal));
        fresh.WorktreeBase.CandidateWarnings.ShouldContain(w => w.Contains(maxima[1].WorktreeBranch!, StringComparison.Ordinal));
    }

    private static CreateAgentTaskRequest Request(Guid? cardId) => new(
        "Build the card", Title: "Worktree continuation", Role: AgentTaskRole.Code,
        Workspace: WorkspaceMode.Worktree, Card: cardId?.ToString("D"));
}
