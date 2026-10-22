using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1480: a same-card dispatch cuts from a sibling's verified settled source when its
/// local branch still owes the settlement sync, and blocks by name when that source cannot be
/// verified in the local object store.
/// </summary>
public partial class AgentTaskDispatchBaseGuardTests
{
    [Test]
    [Timeout(120_000)]
    public async Task C1480_dispatch_cuts_from_settled_source(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c1480-dispatch");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (card, owner, _, settled) = await SeedSettledOwnerAsync(db, repo, absent: false);
        await db.SaveChangesAsync(ct);

        var created = await CreateCardTaskAsync(schema, repo, card, AgentTaskRole.Review, ct);
        await using (var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickInspectedAsync(ct);

        db.ChangeTracker.Clear();
        var dispatched = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched, dispatched.FailureReason);
        dispatched.WorktreeBaseSha.ShouldBe(settled);
        dispatched.WorktreeBaseTaskId.ShouldBe(owner.Id);
        dispatched.WorktreePath.ShouldNotBeNull();
        (await ScratchGitRepo.GitInAsync(dispatched.WorktreePath!, "rev-parse", "HEAD"))
            .StdOut.Trim().ShouldBe(settled);
    }

    [Test]
    [Arguments(AgentTaskRole.Code)]
    [Arguments(AgentTaskRole.Review)]
    [Timeout(120_000)]
    public async Task C1480_dispatch_blocks_unverified_settled_source(AgentTaskRole role, CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c1480-dispatch-block");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var (card, _, _, settled) = await SeedSettledOwnerAsync(db, repo, absent: true);
        await db.SaveChangesAsync(ct);

        var created = await CreateCardTaskAsync(schema, repo, card, role, ct);
        await using (var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot))
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickInspectedAsync(ct);

        db.ChangeTracker.Clear();
        var blocked = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id, ct);
        blocked.Status.ShouldBe(AgentTaskStatus.Blocked);
        blocked.FailureReason.ShouldNotBeNull();
        blocked.FailureReason.ShouldContain("source_sync_unverified");
        blocked.FailureReason.ShouldContain(settled);
        blocked.WorktreePath.ShouldBeNull();
        blocked.AgentSessionId.ShouldBeNull();
    }

    /// <summary>
    /// Owner on branch b at B, master fast-forwarded to B, and a Pending sync debt to S. S is a
    /// descendant of B held only under a progress ref, or a well-formed SHA absent from the store.
    /// </summary>
    private static async Task<(Card Card, AgentTask Owner, string B, string S)> SeedSettledOwnerAsync(
        AppDbContext db, ScratchGitRepo repo, bool absent)
    {
        await repo.CommitFileAsync("seed.txt", "M\n");
        var card = await SeedCardAsync(db, "CARD-1480");
        var owner = await SeedKeptSiblingAsync(db, repo, card.Id, "B", startRef: "master");
        owner.Attempt = 1;
        await repo.GitAsync("merge", "--ff-only", owner.WorktreeBranch!);
        var b = (await repo.GitReadAsync("rev-parse", owner.WorktreeBranch!)).Trim();
        var s = absent ? "0123456789abcdef0123456789abcdef01234567"
            : await AgentTaskWorktreeBaseSyncDebtTests.DetachedCommitAsync(repo, b, "settled",
                $"refs/antiphon/progress/{owner.Id:N}/1/observed");
        AgentTaskWorktreeBaseSyncDebtTests.AddDebt(db, owner, b, s, AgentTaskSyncDebtState.Pending);
        return (card, owner, b, s);
    }

    private static async Task<AgentTaskCreatedDto> CreateCardTaskAsync(IsolatedTestSchema schema,
        ScratchGitRepo repo, Card card, AgentTaskRole role, CancellationToken ct)
    {
        await using var provider = CreateProvider(schema.ConnectionString, repo.WorktreeRoot);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentTaskService>()
            .CreateAsync(new CreateAgentTaskRequest("Continue the card", Title: "CARD-1480 " + role,
                Role: role, Workspace: WorkspaceMode.Worktree, Card: card.Id.ToString("D")),
                new AgentTaskService.Caller(null, null, repo.Path), ct);
    }
}
