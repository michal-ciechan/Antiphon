using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The thread projection (mobile-thread spec slice T2): one card's work gathered from the four
/// places it is recorded, correlated by the identifier everything already cites.
///
/// <para><b>The property under test is the correlation, and its failure mode is silent.</b> There
/// is no foreign key between <c>Card</c> and <c>AgentTask</c>, so nothing in the database can
/// contradict a wrong text match — a thread that quietly adopted CARD-00670's tasks would look
/// exactly like a correct one. The boundary guard therefore gets its own test on both sides, task
/// and commit, each with a decoy row that a plain <c>contains</c> would have matched.</para>
///
/// <para><b>Shared-database discipline.</b> Every test in this assembly runs against ONE Postgres,
/// and the task query here is a fleet-global text match — it can legitimately return another
/// suite's row if that row cites this identifier. So each test claims its own identifier
/// (<c>CARD-07nn</c>, one per test, a range nothing else in this repo writes into a task title),
/// asserts only over the ids it created, and names its decoys explicitly rather than counting.
/// Three separate "flaky test" incidents here were an unscoped assertion over this database.</para>
/// </summary>
[Category("Integration")]
public class CardThreadServiceTests
{
    // ---- 1. correlation by citation --------------------------------------------------------------

    [Test]
    public async Task A_task_that_names_the_card_in_its_title_is_on_the_thread()
    {
        await using var scenario = new Scenario("CARD-0701");
        var task = await scenario.AddTaskAsync(
            title: "CARD-0701 - the reply route out of an agent", goal: "Make the route durable.");

        var thread = await scenario.ThreadAsync();

        var row = scenario.Owned(thread).Single();
        row.Id.ShouldBe(task);
        row.MatchedOn.ShouldBe("title");
        thread.Identifier.ShouldBe("CARD-0701");
        thread.Card.Identifier.ShouldBe("CARD-0701");
    }

    [Test]
    public async Task A_citation_in_the_goal_alone_still_correlates_and_the_row_says_where()
    {
        // A goal often names OTHER cards as context, so which field carried the citation is a real
        // difference in confidence and belongs on the row rather than being flattened away.
        await using var scenario = new Scenario("CARD-0702");
        var goalOnly = await scenario.AddTaskAsync(
            title: "durable channel replies", goal: "Implement slices 1-3 of CARD-0702 and report.");
        var both = await scenario.AddTaskAsync(
            title: "CARD-0702 slice 4", goal: "Finish CARD-0702.");

        var rows = scenario.Owned(await scenario.ThreadAsync());

        rows.Single(r => r.Id == goalOnly).MatchedOn.ShouldBe("goal");
        rows.Single(r => r.Id == both).MatchedOn.ShouldBe("both");
    }

    [Test]
    public async Task The_display_form_of_the_identifier_correlates_too()
    {
        // CARD-0042 made "#704" a stable citation and it is what the orchestrator's own status
        // lines use, so a thread that only understood the canonical form would miss them.
        await using var scenario = new Scenario("CARD-0704");
        var hash = await scenario.AddTaskAsync(title: "#704 launch leak - slices 3+4", goal: "fix it");
        var unpadded = await scenario.AddTaskAsync(title: "CARD-704 follow-up", goal: "fix it");

        var ids = scenario.Owned(await scenario.ThreadAsync()).Select(r => r.Id).ToList();

        ids.ShouldContain(hash);
        ids.ShouldContain(unpadded);
    }

    [Test]
    public async Task A_longer_identifier_is_never_mistaken_for_this_one()
    {
        // THE test. With no foreign key anywhere, a substring match is the only thing standing
        // between this thread and another card's work, and nothing downstream would notice. Both
        // decoys are returned by the database's ILIKE narrowing, so this proves the in-memory
        // boundary guard and not merely the SQL.
        await using var scenario = new Scenario("CARD-0705");
        var real = await scenario.AddTaskAsync(title: "CARD-0705 - the real one", goal: "work");
        var longerCanonical = await scenario.AddTaskAsync(
            title: "CARD-07050 - a different card entirely", goal: "work");
        var longerDisplay = await scenario.AddTaskAsync(title: "#7051 - also not this card", goal: "work");

        var thread = await scenario.ThreadAsync();
        var ids = thread.Tasks.Select(t => t.Id).ToList();

        scenario.Owned(thread).Single().Id.ShouldBe(real);
        ids.ShouldNotContain(longerCanonical, "CARD-0705 must not be read out of CARD-07050");
        ids.ShouldNotContain(longerDisplay, "#705 must not be read out of #7051");
    }

    // ---- 2. what each task row carries -----------------------------------------------------------

    [Test]
    public async Task The_latest_check_reading_comes_through_as_the_interpreters_reading()
    {
        await using var scenario = new Scenario("CARD-0706");
        var task = await scenario.AddTaskAsync(title: "CARD-0706 - checked work", goal: "work");
        await scenario.AddCheckAsync(task, minutesAgo: 40, detail: Digest("an older check"));
        await scenario.AddCheckAsync(task, minutesAgo: 5, detail:
            $"{AgentTaskCheckService.ReadingHeading}\nWriting tests; two files changed since the last check.\n"
            + $"\n{AgentTaskCheckService.DigestHeading}\nfiles=2 turns=9");

        var row = scenario.Owned(await scenario.ThreadAsync()).Single();

        row.LatestCheck.ShouldNotBeNull();
        row.LatestCheck!.FromInterpreter.ShouldBeTrue();
        row.LatestCheck.Text.ShouldContain("two files changed");
        row.LatestCheck.Text.Contains("an older check")
            .ShouldBeFalse("the LATEST check is the one that matters");
    }

    [Test]
    public async Task A_check_with_no_interpreted_reading_degrades_to_the_digest_tail()
    {
        // Checks that ran before CARD-0035 slice 5, or whose interpreter was busy, have no reading.
        // That is not a degradation to report — it is the digest, and the row says which it is so a
        // client never presents a digest tail as somebody's verdict.
        await using var scenario = new Scenario("CARD-0707");
        var task = await scenario.AddTaskAsync(title: "CARD-0707 - checked before slice 5", goal: "work");
        await scenario.AddCheckAsync(task, minutesAgo: 5, detail: Digest("last line of the digest"));

        var row = scenario.Owned(await scenario.ThreadAsync()).Single();

        row.LatestCheck.ShouldNotBeNull();
        row.LatestCheck!.FromInterpreter.ShouldBeFalse();
        row.LatestCheck.Text.ShouldContain("last line of the digest");
    }

    [Test]
    public async Task A_settled_task_carries_its_report_and_the_spend_of_everything_under_it()
    {
        // Rolled up the same way the board and the attention view roll it up — two answers to
        // "what has this cost" would eventually disagree, and the board's is the one an operator
        // has already learned to read.
        await using var scenario = new Scenario("CARD-0708");
        var parent = await scenario.AddTaskAsync(
            title: "CARD-0708 - orchestrated work", goal: "work",
            status: AgentTaskStatus.Succeeded, costUsd: 1.50m,
            result: "Done: three slices landed, all green.");
        await scenario.AddTaskAsync(
            title: "a child nobody cited", goal: "sub-work", costUsd: 0.75m, parent: parent);

        var row = scenario.Owned(await scenario.ThreadAsync()).Single();

        row.Id.ShouldBe(parent);
        row.Result.ShouldBe("Done: three slices landed, all green.");
        row.CostUsd.ShouldBe(1.50m);
        row.SubtreeCostUsd.ShouldBe(2.25m, "the child's spend is part of what this card cost");
        row.Status.ShouldBe(AgentTaskStatus.Succeeded);
    }

    [Test]
    public async Task Each_row_says_which_agent_program_ran_it_so_the_tier_can_name_a_model()
    {
        // ModelLevel alone does not name a model: the same High rung is opus on Claude and
        // grok-4.6 on Grok (CARD-0084 S4). Without the kind on the row, the thread's tier badge
        // would name a model nobody was paying for on every Grok task.
        await using var scenario = new Scenario("CARD-0713");
        var grok = await scenario.AddTaskAsync(
            title: "CARD-0713 - the Grok half", goal: "work", agentKind: AgentKind.Grok);
        var claude = await scenario.AddTaskAsync(
            title: "CARD-0713 - the Claude half", goal: "work");

        var rows = scenario.Owned(await scenario.ThreadAsync());

        rows.Single(r => r.Id == grok).AgentKind.ShouldBe(AgentKind.Grok);
        rows.Single(r => r.Id == claude).AgentKind.ShouldBe(
            AgentKind.ClaudeCode, "an unset kind is Claude, which is what every pre-CARD-0084 row is");
    }

    // ---- 3. plans and commits, and degrading honestly when there is no repo ----------------------

    [Test]
    public async Task A_card_with_no_repository_says_nobody_asked_rather_than_reporting_nothing()
    {
        // The runnerConsulted distinction (CARD-0035 slice 1): "no commits cite this card" and "the
        // checkout is gone" are different answers, and a client that collapses them shows a
        // confident empty timeline over a broken lookup.
        await using var scenario = new Scenario("CARD-0709");
        await scenario.AddTaskAsync(title: "CARD-0709 - work with no checkout", goal: "work");

        var thread = await scenario.ThreadAsync();

        thread.ReposConsulted.ShouldBeFalse();
        thread.RepoRoot.ShouldBeNull();
        thread.Commits.ShouldBeEmpty();
        thread.Plans.ShouldBeEmpty();
        scenario.Owned(thread).Count.ShouldBe(1, "the task half comes from the database and still works");
    }

    [Test]
    public async Task Plans_and_commits_come_from_the_cards_own_worktree()
    {
        // Worktree-first: while a card is being worked, its plan and its commits are in the
        // throwaway checkout and the shared one has not seen them yet.
        await using var scenario = new Scenario("CARD-0710");
        using var repo = new ScratchGitRepo("antiphon-thread");
        await repo.WriteSpecAsync("2026-08-17-card-0710-the-thread.md", """
            # CARD-0710 — the thread

            - **Status**: Planned
            - **Card**: CARD-0710
            - **Date**: 2026-08-17
            """);
        await repo.CommitAsync("feat(thread): CARD-0710 - assemble the four records into one view");
        await repo.WriteFileAsync("unrelated.txt", "x");
        await repo.CommitAsync("chore: something else entirely");
        await scenario.AttachWorktreeAsync(repo.Path);

        var thread = await scenario.ThreadAsync();

        thread.ReposConsulted.ShouldBeTrue();
        thread.RepoRoot.ShouldBe(repo.Path);
        thread.Commits.Select(c => c.Subject).ShouldBe([
            "feat(thread): CARD-0710 - assemble the four records into one view",
        ]);
        var plan = thread.Plans.Single();
        plan.Subject.ShouldBeTrue();
        plan.Plan.RelativePath.ShouldBe("docs/superpowers/specs/2026-08-17-card-0710-the-thread.md");
        plan.Plan.Status.ShouldBe("Planned");
    }

    [Test]
    public async Task A_commit_citing_a_longer_identifier_is_not_this_cards_commit()
    {
        // The same boundary guard as the task match, on the git side — it lives in the --grep
        // pattern because git searches the whole message, so a post-filter over subjects would
        // drop commits that cite the card only in their body.
        await using var scenario = new Scenario("CARD-0711");
        using var repo = new ScratchGitRepo("antiphon-thread");
        await repo.WriteFileAsync("a.txt", "a");
        await repo.CommitAsync("fix(x): CARD-07110 - a different card");
        await repo.WriteFileAsync("b.txt", "b");
        await repo.CommitAsync("fix(y): mentioned in the body\n\nCloses CARD-0711.");
        await scenario.AttachWorktreeAsync(repo.Path);

        var subjects = (await scenario.ThreadAsync()).Commits.Select(c => c.Subject).ToList();

        subjects.ShouldBe(["fix(y): mentioned in the body"]);
    }

    [Test]
    public async Task A_plan_that_only_cites_the_card_is_on_the_thread_but_not_as_its_subject()
    {
        // Most specs cite four or five neighbours. Dropping those hides real context; promoting
        // them puts five plans on every card. They are on the thread, ranked below its own.
        await using var scenario = new Scenario("CARD-0712");
        using var repo = new ScratchGitRepo("antiphon-thread");
        await repo.WriteSpecAsync("2026-08-17-card-0712-the-subject.md",
            "# CARD-0712 — the subject\n\n- **Card**: CARD-0712\n- **Date**: 2026-08-17\n");
        await repo.WriteSpecAsync("2026-08-16-card-0799-a-neighbour.md",
            "# CARD-0799 — a neighbour\n\n- **Card**: CARD-0799\n- **Relates to**: CARD-0712\n");
        await repo.CommitAsync("docs: two plans");
        await scenario.AttachWorktreeAsync(repo.Path);

        var plans = (await scenario.ThreadAsync()).Plans;

        plans.Select(p => p.Subject).ShouldBe([true, false], "the card's own plan ranks first");
        plans[0].Plan.FileName.ShouldBe("2026-08-17-card-0712-the-subject.md");
        plans[1].Plan.FileName.ShouldBe("2026-08-16-card-0799-a-neighbour.md");
    }

    // ---- harness ---------------------------------------------------------------------------------

    private static string Digest(string lastLine) =>
        $"probe: read the workspace\nturns=9 files=2\n{lastLine}";

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>
    /// Seeds a project, board, column and card under one claimed identifier, remembers every row it
    /// wrote, and deletes exactly those on dispose. <see cref="Owned"/> is what keeps every
    /// assertion scoped to this test's own rows on a database the whole assembly shares.
    /// </summary>
    private sealed class Scenario : IAsyncDisposable
    {
        private readonly List<Guid> _tasks = [];
        private readonly Guid _projectId = Guid.NewGuid();
        private readonly Guid _boardId = Guid.NewGuid();
        private readonly Guid _columnId = Guid.NewGuid();
        private readonly List<Guid> _worktrees = [];
        private readonly string _identifier;

        private Guid _cardId;
        private Task? _seed;

        public Scenario(string identifier) => _identifier = identifier;

        public IReadOnlyList<CardThreadTaskDto> Owned(CardThreadDto thread) =>
            [.. thread.Tasks.Where(t => _tasks.Contains(t.Id))];

        public async Task<CardThreadDto> ThreadAsync()
        {
            await SeedAsync();
            var service = new CardThreadService(
                CreateContext(),
                new PlanCatalogService(
                    TimeProvider.System,
                    NullLogger<PlanCatalogService>.Instance,
                    new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance)),
                new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
                TimeProvider.System,
                NullLogger<CardThreadService>.Instance);
            return await service.GetAsync(_cardId, CancellationToken.None);
        }

        public async Task<Guid> AddTaskAsync(
            string title,
            string goal,
            AgentTaskStatus status = AgentTaskStatus.Dispatched,
            decimal costUsd = 0m,
            string? result = null,
            Guid? parent = null,
            AgentKind agentKind = AgentKind.ClaudeCode)
        {
            await SeedAsync();
            var id = Guid.NewGuid();
            await using var db = CreateContext();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = parent ?? id,
                ParentTaskId = parent,
                Depth = parent is null ? 0 : 1,
                Title = title,
                Goal = goal,
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                AgentKind = agentKind,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = status,
                ReplyTo = AgentTaskReplyTo.Session,
                CostUsd = costUsd,
                Result = result,
                CreatedAt = DateTime.UtcNow.AddMinutes(-60),
                DispatchedAt = DateTime.UtcNow.AddMinutes(-59),
                CompletedAt = status == AgentTaskStatus.Succeeded ? DateTime.UtcNow.AddMinutes(-1) : null,
            });
            await db.SaveChangesAsync();
            _tasks.Add(id);
            return id;
        }

        public async Task AddCheckAsync(Guid taskId, int minutesAgo, string detail)
        {
            await using var db = CreateContext();
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = taskId,
                Type = AgentTaskEventType.Check,
                Detail = detail,
                At = DateTime.UtcNow.AddMinutes(-minutesAgo),
            });
            await db.SaveChangesAsync();
        }

        /// <summary>Points the card at a real checkout, the way a card being worked on is.</summary>
        public async Task AttachWorktreeAsync(string path)
        {
            await SeedAsync();
            var id = Guid.NewGuid();
            await using var db = CreateContext();
            db.Worktrees.Add(new Worktree
            {
                Id = id,
                CardId = _cardId,
                RepoPath = path,
                Path = path,
                Branch = "master",
                BaseRef = "master",
                Status = WorktreeStatus.Active,
                CreatedAt = DateTime.UtcNow,
                LastTouchedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            _worktrees.Add(id);

            var card = await db.Cards.FirstAsync(c => c.Id == _cardId);
            card.CurrentWorktreeId = id;
            await db.SaveChangesAsync();
        }

        private Task SeedAsync() => _seed ??= SeedCoreAsync();

        private async Task SeedCoreAsync()
        {
            _cardId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await using var db = CreateContext();
            db.Projects.Add(new Project
            {
                Id = _projectId,
                Name = $"thread-{_identifier}",
                GitRepositoryUrl = "https://example.invalid/repo.git",
                // Deliberately null: a card with no checkout is the reposConsulted:false case, and
                // a test that pointed this at a real repo would never exercise it.
                LocalRepositoryPath = null,
                BaseBranch = "master",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Boards.Add(new Board
            {
                Id = _boardId,
                ProjectId = _projectId,
                Name = $"board-{_identifier}",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.BoardColumns.Add(new BoardColumn
            {
                Id = _columnId,
                BoardId = _boardId,
                StateKey = "in-progress",
                Name = "In Progress",
                ColumnOrder = 1,
                CardStatus = CardStatus.InProgress,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Cards.Add(new Card
            {
                Id = _cardId,
                BoardId = _boardId,
                BoardColumnId = _columnId,
                Identifier = _identifier,
                Title = $"the thread for {_identifier}",
                Description = "seeded by CardThreadServiceTests",
                Status = CardStatus.InProgress,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.AgentTaskEvents.Where(e => _tasks.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.Cards.Where(c => c.Id == _cardId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CurrentWorktreeId, (Guid?)null));
            await db.Worktrees.Where(w => _worktrees.Contains(w.Id)).ExecuteDeleteAsync();
            await db.CardRevisions.Where(r => r.CardId == _cardId).ExecuteDeleteAsync();
            await db.Cards.Where(c => c.Id == _cardId).ExecuteDeleteAsync();
            await db.BoardColumns.Where(c => c.Id == _columnId).ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == _boardId).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        }
    }
}

/// <summary>Plan files and custom commit messages, which the shared scratch repo does not do.</summary>
internal static class ThreadRepoExtensions
{
    public static async Task WriteSpecAsync(this ScratchGitRepo repo, string fileName, string content) =>
        await repo.WriteFileAsync($"docs/superpowers/specs/{fileName}", content);

    public static async Task WriteFileAsync(this ScratchGitRepo repo, string relativePath, string content)
    {
        var full = Path.Combine(repo.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);
    }

    public static async Task CommitAsync(this ScratchGitRepo repo, string message)
    {
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", message);
    }
}
