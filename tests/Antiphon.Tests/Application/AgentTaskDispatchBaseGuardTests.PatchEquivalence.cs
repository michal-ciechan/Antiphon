using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskDispatchBaseGuardTests
{
    /// <summary>
    /// CARD-0643. <c>git cherry &lt;base&gt; &lt;tip&gt;</c> with no '+' lines is present, even when the
    /// rebased SHA is not an ancestor and the task has no landing row.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C643_PatchEquivalentKeptBranchStaysSilent(CancellationToken ct)
    {
        await using var w = await GuardWorld.OpenAsync("c643-cherry");
        var sibling = await SeedKeptSiblingAsync(w.Db, w.Repo, w.Card.Id, "rebased onto master");
        var tip = (await w.Repo.GitReadAsync("rev-parse", sibling.WorktreeBranch!)).Trim();
        await w.Repo.CommitFileAsync("diverge.md", "master moved\n");
        await w.Repo.GitAsync("cherry-pick", tip);
        var landed = (await w.Repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        landed.ShouldNotBe(tip);
        CherryContains((await ScratchGitRepo.GitInAsync(w.Repo.Path, "cherry", "master", sibling.WorktreeBranch!)).StdOut)
            .ShouldBeTrue();

        var intents = await w.DispatchAsync(ct);
        (await w.FreshStatusAsync(ct)).ShouldBe(AgentTaskStatus.Dispatched);
        intents.ShouldBeEmpty();
    }

    /// <summary>
    /// CARD-0643. A landing publication or a published land event silences the warning when the
    /// patch is not on the base. Each sibling exercises one signal alone.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C643_PublishedLandingSuppressesUnmatchedPatches(CancellationToken ct)
    {
        await using var w = await GuardWorld.OpenAsync("c643-published");
        var landed = await SeedKeptSiblingAsync(w.Db, w.Repo, w.Card.Id, "landed row only");
        var already = await SeedKeptSiblingAsync(w.Db, w.Repo, w.Card.Id, "already-present event only");
        var residue = await SeedKeptSiblingAsync(w.Db, w.Repo, w.Card.Id, "residue event only");
        AddLanding(w.Db, landed.Id, LandPublicationOutcome.Landed);
        AddLandEvent(w.Db, already.Id, AgentTaskEventType.AlreadyPresent, LandPublicationOutcome.AlreadyPresent);
        AddLandEvent(w.Db, residue.Id, AgentTaskEventType.LandedWithResidue, LandPublicationOutcome.Landed);
        foreach (var sibling in new[] { landed, already, residue })
            CherryContains((await ScratchGitRepo.GitInAsync(w.Repo.Path, "cherry", "master", sibling.WorktreeBranch!)).StdOut)
                .ShouldBeFalse();

        var intents = await w.DispatchAsync(ct);
        (await w.FreshStatusAsync(ct)).ShouldBe(AgentTaskStatus.Dispatched);
        intents.ShouldBeEmpty();
    }

    /// <summary>
    /// CARD-0643. A not-earlier sibling with a published landing supersedes this branch via a
    /// repair link, a worktree-base link, or patch containment of the tip. None of these owners
    /// is patch-contained in master.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C643_LaterSupersedingSiblingLandingSuppresses(CancellationToken ct)
    {
        await using var w = await GuardWorld.OpenAsync("c643-supersede");
        await SupersededPairAsync(w, "repair", "repair");
        await SupersededPairAsync(w, "base", "base");
        await SupersededPairAsync(w, "patch", "patch");

        var intents = await w.DispatchAsync(ct);
        (await w.FreshStatusAsync(ct)).ShouldBe(AgentTaskStatus.Dispatched);
        intents.ShouldBeEmpty();
    }

    /// <summary>
    /// CARD-0643 true positive. A refused landing is not publication, and a later sibling that
    /// landed different commits does not cover this branch.
    /// </summary>
    [Test]
    [Timeout(60_000)]
    public async Task C643_UnlandedCommitsStillWarn(CancellationToken ct)
    {
        await using var w = await GuardWorld.OpenAsync("c643-unlanded");
        var stranded = await SeedKeptSiblingAsync(w.Db, w.Repo, w.Card.Id, "still unlanded");
        var other = await SeedKeptSiblingAsync(w.Db, w.Repo, w.Card.Id, "landed something else");
        other.CreatedAt = stranded.CreatedAt.AddMinutes(1);
        AddLanding(w.Db, stranded.Id, LandPublicationOutcome.Refused);
        AddLandEvent(w.Db, stranded.Id, AgentTaskEventType.LandRefused, LandPublicationOutcome.Refused);
        AddLanding(w.Db, other.Id, LandPublicationOutcome.Landed);
        CherryContains((await ScratchGitRepo.GitInAsync(w.Repo.Path, "cherry", "master", stranded.WorktreeBranch!)).StdOut)
            .ShouldBeFalse();
        CherryContains((await ScratchGitRepo.GitInAsync(w.Repo.Path, "cherry", other.WorktreeBranch!, stranded.WorktreeBranch!)).StdOut)
            .ShouldBeFalse();

        var intents = await w.DispatchAsync(ct);
        (await w.FreshStatusAsync(ct)).ShouldBe(AgentTaskStatus.Dispatched);
        var warning = intents.ShouldHaveSingleItem();
        warning.WarningKey.ShouldBe(DispatchBaseNotificationPayload.SiblingKey(stranded.Id));
        warning.Detail.ShouldContain("without");
        warning.Detail.ShouldContain(stranded.WorktreeBranch!);
        warning.Detail.ShouldContain($"Land {DelegationReportFormatter.Short(stranded.Id)} first");
        warning.Detail.ShouldNotContain(other.WorktreeBranch!);
    }

    private static async Task SupersededPairAsync(GuardWorld w, string label, string link)
    {
        var owner = await SeedKeptSiblingAsync(w.Db, w.Repo, w.Card.Id, label + " owner");
        var successor = await SeedKeptSiblingAsync(w.Db, w.Repo, w.Card.Id, label + " successor");
        successor.CreatedAt = owner.CreatedAt.AddMinutes(1);
        if (link == "repair")
            successor.RepairSourceTaskId = owner.Id;
        else if (link == "base")
            successor.WorktreeBaseTaskId = owner.Id;
        else if (link == "patch")
        {
            var tip = (await w.Repo.GitReadAsync("rev-parse", owner.WorktreeBranch!)).Trim();
            await w.Repo.GitAsync("checkout", successor.WorktreeBranch!);
            await w.Repo.GitAsync("cherry-pick", tip);
            await w.Repo.GitAsync("checkout", "master");
            (await ScratchGitRepo.GitInAsync(w.Repo.Path, "merge-base", "--is-ancestor", tip, successor.WorktreeBranch!)).Ok
                .ShouldBeFalse();
            CherryContains((await ScratchGitRepo.GitInAsync(
                w.Repo.Path, "cherry", successor.WorktreeBranch!, owner.WorktreeBranch!)).StdOut).ShouldBeTrue();
        }
        else
            throw new InvalidOperationException(link);

        if (link != "patch")
            CherryContains((await ScratchGitRepo.GitInAsync(
                w.Repo.Path, "cherry", successor.WorktreeBranch!, owner.WorktreeBranch!)).StdOut).ShouldBeFalse();
        CherryContains((await ScratchGitRepo.GitInAsync(w.Repo.Path, "cherry", "master", owner.WorktreeBranch!)).StdOut)
            .ShouldBeFalse();
        AddLanding(w.Db, successor.Id, LandPublicationOutcome.Landed);
    }

    private static void AddLanding(AppDbContext db, Guid taskId, LandPublicationOutcome publication) =>
        db.AgentTaskLandings.Add(new AgentTaskLanding
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            Active = true,
            SchemaVersion = 1,
            Phase = publication == LandPublicationOutcome.Refused ? LandPhase.Refused : LandPhase.PublicationConfirmed,
            Publication = publication,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            RepositoryPath = "repo",
            CommonDirectory = "common",
            WorktreePath = "tree",
            GitDirectory = "git",
            SourceFullRef = "refs/heads/source",
            TargetFullRef = "refs/heads/master",
            DestinationFullRef = "refs/heads/master",
        });

    private static void AddLandEvent(
        AppDbContext db, Guid taskId, AgentTaskEventType type, LandPublicationOutcome publication) =>
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = type,
            Detail = "CARD-0643 " + type,
            At = DateTime.UtcNow,
            IsLandTerminal = true,
            LandingPublication = publication,
        });

    private sealed class GuardWorld : IAsyncDisposable
    {
        private GuardWorld(
            ScratchGitRepo repo, IsolatedTestSchema schema, AppDbContext db, Card card, AgentTask task)
        {
            Repo = repo;
            Schema = schema;
            Db = db;
            Card = card;
            Task = task;
        }

        public ScratchGitRepo Repo { get; }
        public IsolatedTestSchema Schema { get; }
        public AppDbContext Db { get; }
        public Card Card { get; }
        public AgentTask Task { get; }

        public static async Task<GuardWorld> OpenAsync(string name)
        {
            var repo = new ScratchGitRepo(name);
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var db = CreateContext(schema);
            try
            {
                await repo.CommitFileAsync("README.md", "base\n");
                var card = await SeedCardAsync(db, "CARD-0643");
                var parent = Guid.NewGuid();
                await SeedParentSessionAsync(db, parent);
                var task = await SeedQueuedWorktreeTaskAsync(db, repo.Path, card.Id, parent);
                return new GuardWorld(repo, schema, db, card, task);
            }
            catch
            {
                await db.DisposeAsync();
                await schema.DisposeAsync();
                repo.Dispose();
                throw;
            }
        }

        public async Task<IReadOnlyList<AgentTaskDispatchWarningIntent>> DispatchAsync(CancellationToken ct)
        {
            await Db.SaveChangesAsync(ct);
            await using var provider = CreateProvider(Schema.ConnectionString, Repo.WorktreeRoot);
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(ct);
            await MaterializeAndDeliverAsync(scope.ServiceProvider, Task.Id, ct);
            Db.ChangeTracker.Clear();
            return await Db.AgentTaskDispatchWarningIntents.AsNoTracking()
                .Where(i => i.TaskId == Task.Id)
                .ToListAsync(ct);
        }

        public async Task<AgentTaskStatus> FreshStatusAsync(CancellationToken ct)
        {
            await using var check = new AppDbContext(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));
            return await check.AgentTasks.AsNoTracking()
                .Where(t => t.Id == Task.Id)
                .Select(t => t.Status)
                .SingleAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Schema.DisposeAsync();
            Repo.Dispose();
        }
    }
}
