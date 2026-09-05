using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0217 S9 HTTP wiring: list <c>includeArchived</c>, POST archive/unarchive, never DELETE.
/// Behaviour lives in <see cref="BoardProjectArchiveTests"/>; this pins the routes.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class BoardProjectArchiveApiTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public BoardProjectArchiveApiTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [After(Test)]
    public async Task CleanupAsync()
    {
        if (_projectId == Guid.Empty)
            return;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boardIds = await db.Boards.Where(b => b.ProjectId == _projectId).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }

    [Test]
    public async Task Board_archive_and_unarchive_over_http_hide_and_restore_the_row()
    {
        var (_, board) = await SeedAsync("Api archive board");
        using var client = _factory.CreateClient();

        var archive = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/archive",
            new ArchiveBoardRequest("Duplicate probe board.", "operator"));
        archive.StatusCode.ShouldBe(HttpStatusCode.OK);
        var archived = (await archive.Content.ReadFromJsonAsync<BoardSummaryDto>(Json))!;
        archived.ArchivedAt.ShouldNotBeNull();

        var hidden = await client.GetFromJsonAsync<List<BoardSummaryDto>>("/api/boards", Json);
        hidden!.Select(b => b.Id).ShouldNotContain(board.Id);

        var shown = await client.GetFromJsonAsync<List<BoardSummaryDto>>(
            "/api/boards?includeArchived=true", Json);
        shown!.Select(b => b.Id).ShouldContain(board.Id);

        var unarchive = await client.PostAsJsonAsync(
            $"/api/boards/{board.Id}/unarchive",
            new UnarchiveBoardRequest("Not a duplicate after all."));
        unarchive.StatusCode.ShouldBe(HttpStatusCode.OK);

        var restored = await client.GetFromJsonAsync<List<BoardSummaryDto>>("/api/boards", Json);
        restored!.Select(b => b.Id).ShouldContain(board.Id);

        var deleted = await client.DeleteAsync($"/api/boards/{board.Id}/archive");
        ((int)deleted.StatusCode).ShouldBeGreaterThanOrEqualTo(400);
    }

    [Test]
    public async Task Project_archive_and_unarchive_over_http_hide_and_restore_the_row()
    {
        var (project, _) = await SeedAsync("Api archive project board");
        using var client = _factory.CreateClient();

        var archive = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/archive",
            new ArchiveProjectRequest("Test residue.", "prune-test-data"));
        archive.StatusCode.ShouldBe(HttpStatusCode.OK);
        var archived = (await archive.Content.ReadFromJsonAsync<ProjectDto>(Json))!;
        archived.ArchivedAt.ShouldNotBeNull();
        archived.ArchivedReason.ShouldBe("Test residue.");

        var hidden = await client.GetFromJsonAsync<List<ProjectDto>>("/api/projects", Json);
        hidden!.Select(p => p.Id).ShouldNotContain(project.Id);

        var shown = await client.GetFromJsonAsync<List<ProjectDto>>(
            "/api/projects?includeArchived=true", Json);
        shown!.Select(p => p.Id).ShouldContain(project.Id);

        var unarchive = await client.PostAsJsonAsync(
            $"/api/projects/{project.Id}/unarchive",
            new UnarchiveProjectRequest("Keep it."));
        unarchive.StatusCode.ShouldBe(HttpStatusCode.OK);

        var restored = await client.GetFromJsonAsync<List<ProjectDto>>("/api/projects", Json);
        restored!.Select(p => p.Id).ShouldContain(project.Id);
    }

    private async Task<(Project Project, BoardDetailDto Board)> SeedAsync(string boardName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boards = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Application.Services.BoardService>();

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Api Archive Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-api-archive-{Guid.NewGuid():N}"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        var board = await boards.CreateAsync(
            new CreateBoardRequest(project.Id, boardName), CancellationToken.None);
        return (project, board);
    }
}
