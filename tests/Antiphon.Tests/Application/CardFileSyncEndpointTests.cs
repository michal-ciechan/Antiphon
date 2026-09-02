using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0004 S3: <c>POST /api/boards/{id}/card-files/sync</c> — unknown board 404, dryRun on a
/// path-less project 200 with <c>no_repository_path</c>, <c>CardFileSync:Enabled=false</c> 409
/// <c>card_file_sync_disabled</c>.
/// </summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<CardFileSyncEndpointWebAppFactory>(Shared = SharedType.PerClass)]
public class CardFileSyncEndpointTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly CardFileSyncEndpointWebAppFactory _factory;
    private Guid _projectId;

    public CardFileSyncEndpointTests(CardFileSyncEndpointWebAppFactory factory) => _factory = factory;

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
        await db.Cards.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }

    [Test]
    public async Task Unknown_board_returns_404()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync($"/api/boards/{Guid.NewGuid()}/card-files/sync", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Dry_run_on_a_pathless_project_returns_200_no_repository_path()
    {
        var board = await SeedPathlessBoardAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/boards/{board.Id}/card-files/sync?dryRun=true", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = (await response.Content.ReadFromJsonAsync<CardFileSyncBoardResult>(Json))!;
        result.WriteSkipReason.ShouldBe("no_repository_path");
        result.DryRun.ShouldBeTrue();
        result.BoardId.ShouldBe(board.Id);
        result.Written.ShouldBe(0);
        result.Directory.ShouldBeNull();
        result.CommitSha.ShouldBeNull();
    }

    private async Task<Board> SeedPathlessBoardAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"c0004-ep-{Guid.NewGuid():N}"[..20],
            GitRepositoryUrl = "https://example.invalid/repo.git",
            LocalRepositoryPath = null,
            BaseBranch = "master",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Pathless Endpoint",
            CreatedAt = now,
            UpdatedAt = now,
            Project = project,
        };
        project.Boards.Add(board);
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board,
        };
        board.Columns.Add(column);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;
        return board;
    }
}

/// <summary>
/// CARD-0004 S3: <c>CardFileSync:Enabled=false</c> makes the feature not exist — 409 even for an
/// unknown board, before any lookup.
/// </summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<DisabledCardFileSyncEndpointWebAppFactory>(Shared = SharedType.PerClass)]
public class CardFileSyncDisabledEndpointTests
{
    private readonly DisabledCardFileSyncEndpointWebAppFactory _factory;

    public CardFileSyncDisabledEndpointTests(DisabledCardFileSyncEndpointWebAppFactory factory) =>
        _factory = factory;

    [Test]
    public async Task Disabled_sync_returns_409_card_file_sync_disabled()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsync($"/api/boards/{Guid.NewGuid()}/card-files/sync", content: null);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("card_file_sync_disabled");
    }
}

public sealed class CardFileSyncEndpointWebAppFactory : AntiphonWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // HTTP tests are the manual surface; do not let the 60 s tick race them.
                ["CardFileSync:IntervalSeconds"] = "0",
            }));
    }
}

public sealed class DisabledCardFileSyncEndpointWebAppFactory : AntiphonWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["CardFileSync:Enabled"] = "false",
                ["CardFileSync:IntervalSeconds"] = "0",
            }));
    }
}
