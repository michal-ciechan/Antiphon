using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkflowDefinitions;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("Board")]
public class WorkflowDefinitionLoaderTests
{
    [Test]
    public void Loader_parses_front_matter_and_body_separately()
    {
        var parsed = WorkflowDefinitionLoader.Parse("""
            ---
            name: E09 Workflow
            hooks:
              before_run:
                command: npm test
                timeout_seconds: 5
            ---
            # Prompt

            Work on {{ issue.identifier }}.
            """);

        parsed.Name.ShouldBe("E09 Workflow");
        parsed.FrontMatter.ShouldContain("hooks:");
        parsed.PromptMarkdown.ShouldContain("# Prompt");
        parsed.PromptMarkdown.ShouldContain("{{ issue.identifier }}");
        parsed.Hooks.BeforeRun.ShouldNotBeNull();
        parsed.Hooks.BeforeRun!.Command.ShouldBe("npm test");
        parsed.Hooks.BeforeRun.Timeout.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void Loader_accepts_indented_front_matter_delimiters()
    {
        var parsed = WorkflowDefinitionLoader.Parse("""
              ---
            name: E09 Workflow
              ---
            Work on {{ issue.identifier }}.
            """);

        parsed.Name.ShouldBe("E09 Workflow");
        parsed.PromptMarkdown.ShouldContain("{{ issue.identifier }}");
    }

    [Test]
    public void PromptRenderer_unknown_variable_throws()
    {
        var ex = Should.Throw<ValidationException>(() =>
            WorkflowDefinitionLoader.RenderPrompt(
                "Work on {{ issue.title }} in {{ workspace.unknown }}.",
                new Dictionary<string, string?>
                {
                    ["issue.title"] = "Pinned workflow"
                }));

        ex.Errors["prompt"].Single().ShouldBe("Unknown workflow prompt variable 'workspace.unknown'.");
    }

    [Test]
    public async Task Loader_invalid_reload_keeps_last_good()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var activeContent = """
                ---
                name: Last Good
                ---
                Work on {{ issue.title }}.
                """;
            var active = NewDefinition(graph.Board, version: 1, activeContent, isActive: true);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var fileStore = new FakeWorkflowFileStore { Content = "---\nname: Broken\nWork on {{ issue.title }}" };
            var eventBus = new MockEventBus();
            var loader = new WorkflowDefinitionLoader(
                db,
                fileStore,
                new FakeFileSystemWatcher(),
                eventBus,
                TimeProvider.System);

            var result = await loader.ReloadFromFileAsync(graph.Board.Id, CancellationToken.None);

            result.ShouldNotBeNull();
            result!.DefinitionId.ShouldBe(active.Id);
            result.Version.ShouldBe(1);
            await using var verify = CreateContext();
            var definitions = await verify.BoardWorkflowDefinitions
                .Where(d => d.BoardId == graph.Board.Id)
                .OrderBy(d => d.Version)
                .ToListAsync();
            definitions.Count.ShouldBe(1);
            definitions.Single().IsActive.ShouldBeTrue();
            definitions.Single().Content.ShouldBe(activeContent);
            eventBus.PublishedEvents.Single(e => e.EventName == "WorkflowReloaded")
                .Payload
                .GetType()
                .GetProperty("ok")!
                .GetValue(eventBus.PublishedEvents.Single(e => e.EventName == "WorkflowReloaded").Payload)
                .ShouldBe(false);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Loader_get_reconciles_file_changes_against_active_definition()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var activeContent = """
                ---
                name: Last Good
                ---
                Work on {{ issue.title }}.
                """;
            var fileContent = """
                ---
                name: From Disk
                ---
                Work on {{ workspace.branch }}.
                """;
            NewDefinition(graph.Board, version: 1, activeContent, isActive: true);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var loader = new WorkflowDefinitionLoader(
                db,
                new FakeWorkflowFileStore { Content = fileContent },
                new FakeFileSystemWatcher(),
                new MockEventBus(),
                TimeProvider.System);

            var result = await loader.GetAsync(graph.Board.Id, CancellationToken.None);

            result.Version.ShouldBe(2);
            result.Name.ShouldBe("From Disk");
            result.Content.ShouldBe(fileContent);

            await using var verify = CreateContext();
            var definitions = await verify.BoardWorkflowDefinitions
                .Where(d => d.BoardId == graph.Board.Id)
                .OrderBy(d => d.Version)
                .ToListAsync();
            definitions.Count.ShouldBe(2);
            definitions[0].IsActive.ShouldBeFalse();
            definitions[1].IsActive.ShouldBeTrue();
            definitions[1].Content.ShouldBe(fileContent);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Loader_update_requires_local_repository_path_to_write_file()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot, localRepositoryPath: null);
            db.Add(graph.Project);
            await db.SaveChangesAsync();

            var loader = new WorkflowDefinitionLoader(
                db,
                new WorkflowFileStore(),
                new FakeFileSystemWatcher(),
                new MockEventBus(),
                TimeProvider.System);

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                loader.UpdateAsync(
                    graph.Board.Id,
                    new UpdateBoardWorkflowRequest("""
                        ---
                        name: No Repo
                        ---
                        Work on {{ issue.title }}.
                        """),
                    CancellationToken.None));

            ex.Errors[nameof(Project.LocalRepositoryPath)].Single()
                .ShouldBe("Project local repository path is required to save WORKFLOW.md.");

            await using var verify = CreateContext();
            var definitions = await verify.BoardWorkflowDefinitions
                .Where(d => d.BoardId == graph.Board.Id)
                .ToListAsync();
            definitions.ShouldBeEmpty();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public void Workflow_file_store_uses_board_scoped_paths()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var secondBoard = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = graph.Project.Id,
                Name = "Second Board",
                TrackerKind = TrackerKind.Internal,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Project = graph.Project
            };
            graph.Project.Boards.Add(secondBoard);
            var store = new WorkflowFileStore();

            var firstPath = store.GetWorkflowFilePath(graph.Board);
            var secondPath = store.GetWorkflowFilePath(secondBoard);

            firstPath.ShouldNotBeNull();
            secondPath.ShouldNotBeNull();
            firstPath.ShouldNotBe(secondPath);
            firstPath.ShouldContain(graph.Board.Id.ToString("N"));
            secondPath.ShouldContain(secondBoard.Id.ToString("N"));
            Path.GetFileName(firstPath).ShouldBe(WorkflowDefinitionLoader.WorkflowFileName);
            Path.GetFileName(secondPath).ShouldBe(WorkflowDefinitionLoader.WorkflowFileName);
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Graph NewGraph(string tempRoot) =>
        NewGraph(tempRoot, Path.Combine(tempRoot, "repo"));

    private static Graph NewGraph(string tempRoot, string? localRepositoryPath)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Workflow Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = localRepositoryPath,
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        if (!string.IsNullOrWhiteSpace(project.LocalRepositoryPath))
            Directory.CreateDirectory(project.LocalRepositoryPath);

        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Workflow Board {Guid.NewGuid():N}",
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
        Path.Combine(Path.GetTempPath(), $"antiphon-workflow-loader-tests-{Guid.NewGuid():N}");

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
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp directories.
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

        public void RaiseChanged(Guid boardId, string path)
        {
            Changed?.Invoke(this, new WorkflowFileChangedEventArgs(boardId, path));
        }
    }
}
