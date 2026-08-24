using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkflowDefinitions;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0166 S1: TrackerKind is a derived index of the workflow YAML tracker.kind.</summary>
[Category("Integration")]
[NotInParallel("Board")]
public class WorkflowTrackerActivationTests
{
    [Test]
    public async Task Saving_workflow_with_tracker_kind_github_flips_board_and_stamps_activated_at()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = NewGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var loader = NewLoader(db, clock);
            await loader.UpdateAsync(
                graph.Board.Id,
                new UpdateBoardWorkflowRequest("""
                    ---
                    name: Tracked
                    tracker:
                      kind: github
                      repository: acme/app
                      active_states: [open]
                    ---
                    Work on {{ issue.identifier }}.
                    """),
                CancellationToken.None);

            await using var verify = CreateContext();
            var board = await verify.Boards
                .Include(b => b.WorkflowDefinitions)
                .SingleAsync(b => b.Id == graph.Board.Id);
            board.TrackerKind.ShouldBe(TrackerKind.GitHubIssues);
            board.TrackerActivatedAt.ShouldBe(clock.GetUtcNow().UtcDateTime);
            IssueTrackerConfigParser.TryParse(board, out var config, out var error).ShouldBeTrue(error);
            config!.Kind.ShouldBe(TrackerKind.GitHubIssues);
            config.Repository.ShouldBe("acme/app");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Re_saving_does_not_move_TrackerActivatedAt()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = NewGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var loader = NewLoader(db, clock);
            var content = """
                ---
                name: Tracked
                tracker:
                  kind: github_issues
                  repository: acme/app
                ---
                Work on {{ issue.title }}.
                """;
            await loader.UpdateAsync(graph.Board.Id, new UpdateBoardWorkflowRequest(content), CancellationToken.None);
            var firstStamp = (await db.Boards.SingleAsync(b => b.Id == graph.Board.Id)).TrackerActivatedAt;

            clock.Advance(TimeSpan.FromHours(2));
            await loader.UpdateAsync(
                graph.Board.Id,
                new UpdateBoardWorkflowRequest(content.Replace("Work on", "Still work on", StringComparison.Ordinal)),
                CancellationToken.None);

            var board = await db.Boards.SingleAsync(b => b.Id == graph.Board.Id);
            board.TrackerKind.ShouldBe(TrackerKind.GitHubIssues);
            board.TrackerActivatedAt.ShouldBe(firstStamp);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Removing_tracker_block_flips_to_Internal_and_later_readd_does_not_move_stamp()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = NewGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var loader = NewLoader(db, clock);
            await loader.UpdateAsync(
                graph.Board.Id,
                new UpdateBoardWorkflowRequest("""
                    ---
                    name: Tracked
                    tracker:
                      kind: github
                      repository: acme/app
                    ---
                    Work on {{ issue.title }}.
                    """),
                CancellationToken.None);
            var firstStamp = (await db.Boards.SingleAsync(b => b.Id == graph.Board.Id)).TrackerActivatedAt;
            firstStamp.ShouldNotBeNull();

            await loader.UpdateAsync(
                graph.Board.Id,
                new UpdateBoardWorkflowRequest("""
                    ---
                    name: Untracked
                    ---
                    Work on {{ issue.title }}.
                    """),
                CancellationToken.None);

            var deactivated = await db.Boards.SingleAsync(b => b.Id == graph.Board.Id);
            deactivated.TrackerKind.ShouldBe(TrackerKind.Internal);
            deactivated.TrackerActivatedAt.ShouldBe(firstStamp);

            clock.Advance(TimeSpan.FromDays(1));
            await loader.UpdateAsync(
                graph.Board.Id,
                new UpdateBoardWorkflowRequest("""
                    ---
                    name: Tracked again
                    tracker:
                      kind: github
                      repository: acme/app
                    ---
                    Work on {{ issue.title }}.
                    """),
                CancellationToken.None);

            var reactivated = await db.Boards.SingleAsync(b => b.Id == graph.Board.Id);
            reactivated.TrackerKind.ShouldBe(TrackerKind.GitHubIssues);
            reactivated.TrackerActivatedAt.ShouldBe(firstStamp);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // CARD-0170 T11: import_column is validated in the same place, and for the same reason, as
    // kind — a typo must be a 400 on SAVE, not a silent fall back to the default on every sync
    // thereafter, which would look exactly like the bug this card fixed.
    [Test]
    public async Task Unknown_import_column_value_is_a_validation_error_on_save()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var fileStore = new FakeWorkflowFileStore();
            var loader = new WorkflowDefinitionLoader(
                db,
                fileStore,
                new FakeFileSystemWatcher(),
                new MockEventBus(),
                new WorkflowDefinitionVersionGate(),
                TimeProvider.System);

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                loader.UpdateAsync(
                    graph.Board.Id,
                    new UpdateBoardWorkflowRequest("""
                        ---
                        name: Tracked
                        tracker:
                          kind: github
                          repository: acme/app
                          import_column: in-progress
                        ---
                        Work on {{ issue.title }}.
                        """),
                    CancellationToken.None));

            ex.Errors.Keys.ShouldContain("tracker.import_column");
            fileStore.Content.ShouldBeNull();
            await using var verify = CreateContext();
            (await verify.Boards.SingleAsync(b => b.Id == graph.Board.Id)).TrackerKind
                .ShouldBe(TrackerKind.Internal);
            (await verify.BoardWorkflowDefinitions.CountAsync(d => d.BoardId == graph.Board.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Both_import_column_values_save_and_round_trip_through_the_config()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var loader = NewLoader(db, new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero)));
            foreach (var (value, expected) in new[]
            {
                ("backlog", TrackerImportColumn.Backlog),
                ("active", TrackerImportColumn.Active)
            })
            {
                await loader.UpdateAsync(
                    graph.Board.Id,
                    new UpdateBoardWorkflowRequest("""
                        ---
                        name: Tracked
                        tracker:
                          kind: github
                          repository: acme/app
                          import_column: PLACEHOLDER
                        ---
                        Work on {{ issue.title }}.
                        """.Replace("PLACEHOLDER", value)),
                    CancellationToken.None);

                await using var verify = CreateContext();
                var board = await verify.Boards
                    .Include(b => b.WorkflowDefinitions)
                    .SingleAsync(b => b.Id == graph.Board.Id);
                IssueTrackerConfigParser.TryParse(board, out var config, out var error).ShouldBeTrue(error);
                TrackerLandingColumn.ModeFor(config!).ShouldBe(expected);
            }
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Unparseable_kind_throws_ValidationException_and_leaves_board_unchanged()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var eventBus = new MockEventBus();
            var fileStore = new FakeWorkflowFileStore();
            var loader = new WorkflowDefinitionLoader(
                db,
                fileStore,
                new FakeFileSystemWatcher(),
                eventBus,
                new WorkflowDefinitionVersionGate(),
                TimeProvider.System);

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                loader.UpdateAsync(
                    graph.Board.Id,
                    new UpdateBoardWorkflowRequest("""
                        ---
                        name: Bad
                        tracker:
                          kind: not-a-real-tracker
                          repository: acme/app
                        ---
                        Work on {{ issue.title }}.
                        """),
                    CancellationToken.None));

            ex.Errors.Keys.ShouldContain("tracker.kind");
            fileStore.Content.ShouldBeNull();
            await using var verify = CreateContext();
            var board = await verify.Boards.SingleAsync(b => b.Id == graph.Board.Id);
            board.TrackerKind.ShouldBe(TrackerKind.Internal);
            board.TrackerActivatedAt.ShouldBeNull();
            (await verify.BoardWorkflowDefinitions.CountAsync(d => d.BoardId == graph.Board.Id)).ShouldBe(0);
            eventBus.PublishedEvents.ShouldContain(e => e.EventName == "WorkflowReloaded");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task File_reload_path_flips_TrackerKind_identically()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 15, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = NewGraph(tempRoot);
            NewDefinition(graph.Board, 1, """
                ---
                name: Internal
                ---
                Work on {{ issue.title }}.
                """, isActive: true);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var fileContent = """
                ---
                name: From Disk
                tracker:
                  kind: linear
                  project: Antiphon
                ---
                Work on {{ issue.title }}.
                """;
            var loader = new WorkflowDefinitionLoader(
                db,
                new FakeWorkflowFileStore { Content = fileContent },
                new FakeFileSystemWatcher(),
                new MockEventBus(),
                new WorkflowDefinitionVersionGate(),
                clock);

            await loader.GetAsync(graph.Board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            var board = await verify.Boards.SingleAsync(b => b.Id == graph.Board.Id);
            board.TrackerKind.ShouldBe(TrackerKind.Linear);
            board.TrackerActivatedAt.ShouldBe(clock.GetUtcNow().UtcDateTime);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public void TryParseKind_recognises_github_aliases_and_rejects_unknown()
    {
        IssueTrackerConfigParser.TryParseKind("github", out var github).ShouldBeTrue();
        github.ShouldBe(TrackerKind.GitHubIssues);
        IssueTrackerConfigParser.TryParseKind("github_issues", out var underscored).ShouldBeTrue();
        underscored.ShouldBe(TrackerKind.GitHubIssues);
        IssueTrackerConfigParser.TryParseKind("linear", out var linear).ShouldBeTrue();
        linear.ShouldBe(TrackerKind.Linear);
        IssueTrackerConfigParser.TryParseKind("bogus", out _).ShouldBeFalse();
        IssueTrackerConfigParser.TryParseKind(null, out _).ShouldBeFalse();
    }

    private static WorkflowDefinitionLoader NewLoader(AppDbContext db, TimeProvider clock) =>
        new(
            db,
            new FakeWorkflowFileStore(),
            new FakeFileSystemWatcher(),
            new MockEventBus(),
            new WorkflowDefinitionVersionGate(),
            clock);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Graph NewGraph(string tempRoot)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Activation Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath);

        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Activation Board {Guid.NewGuid():N}",
            TrackerKind = TrackerKind.Internal,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);
        return new Graph(project, board);
    }

    private static BoardWorkflowDefinition NewDefinition(
        Board board,
        int version,
        string content,
        bool isActive)
    {
        var now = DateTime.UtcNow;
        var definition = new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = version,
            Name = $"Definition {version}",
            Content = content,
            IsActive = isActive,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.WorkflowDefinitions.Add(definition);
        return definition;
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-tracker-activation-{Guid.NewGuid():N}");

    private static async Task CleanupProjectsByTempRootAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
            return;

        var boardIds = await db.Boards
            .Where(b => projectIds.Contains(b.ProjectId))
            .Select(b => b.Id)
            .ToListAsync();

        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private sealed record Graph(Project Project, Board Board);

    private sealed class FakeWorkflowFileStore : IWorkflowFileStore
    {
        public string? Content { get; set; }

        public string? GetWorkflowFilePath(Board board) =>
            string.IsNullOrWhiteSpace(board.Project.LocalRepositoryPath)
                ? null
                : Path.Combine(
                    board.Project.LocalRepositoryPath,
                    ".antiphon",
                    "boards",
                    board.Id.ToString("N"),
                    WorkflowDefinitionLoader.WorkflowFileName);

        public Task<string?> ReadAsync(Board board, CancellationToken ct) => Task.FromResult(Content);

        public Task WriteAsync(Board board, string content, CancellationToken ct)
        {
            Content = content;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFileSystemWatcher : IFileSystemWatcher
    {
        public event EventHandler<WorkflowFileChangedEventArgs>? Changed;

        public void Watch(Guid boardId, string directoryPath, string fileName)
        {
        }

        public void Unwatch(Guid boardId)
        {
        }

        public void Dispose()
        {
        }
    }
}
