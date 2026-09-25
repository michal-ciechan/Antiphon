using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0710 V-1. Literal JSON through the production create contract and the real
/// <see cref="Antiphon.Server.Application.Services.AgentTaskService"/>. Unknown request fields are
/// whatever the current contract does with them; the oracle is the status, the stored row and the
/// serialized create response, never a type that does not exist yet.
/// </summary>
[Category("Integration")]
public sealed class TaskPlatformWireTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    [Test]
    public async Task Explicit_windows_on_linux_is_409_without_task()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        await using var db = kit.Context();
        var before = await kit.TaskCountAsync();
        var request = Deserialize(
            """
            {"goal":"c710 windows on linux","title":"c710 windows","role":"Code","agentKind":"Grok","workspace":"Worktree","runnerId":"server2","requiredPlatform":"Windows"}
            """);

        var refused = await Should.ThrowAsync<ConflictException>(() =>
            kit.Service(db).CreateAsync(request, kit.Caller, CancellationToken.None));

        refused.Code.ShouldBe("runner_platform_mismatch");
        refused.Message.ShouldContain("Windows");
        refused.Message.ShouldContain("server2");
        (await kit.TaskCountAsync()).ShouldBe(before, "a platform mismatch inserts no task");
    }

    [Test]
    public async Task Card_default_is_frozen_on_create()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var cardId = await SeedCardAsync(kit, "CARD-0710");
        await TryStampCardPlatformAsync(kit, cardId, windows: true);
        await using var db = kit.Context();
        var request = Deserialize(
            $$"""
            {"goal":"c710 inherit card","title":"c710 inherit","role":"Code","agentKind":"Grok","workspace":"Worktree","card":"CARD-0710"}
            """);

        var created = await kit.Service(db).CreateAsync(request, kit.Caller, CancellationToken.None);

        created.CardId.ShouldBe(cardId);
        var body = JsonSerializer.Serialize(created, Json);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("requiredPlatform", out var platform).ShouldBeTrue(body);
        platform.GetString().ShouldBe("Windows");
        (await ReadStoredPlatformAsync(kit, created.Id)).ShouldBe("Windows");
    }

    [Test]
    public async Task Explicit_any_overrides_card_default()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var cardId = await SeedCardAsync(kit, "CARD-0711");
        await TryStampCardPlatformAsync(kit, cardId, windows: true);
        await using var db = kit.Context();
        var request = Deserialize(
            """
            {"goal":"c710 explicit any","title":"c710 any","role":"Code","agentKind":"Grok","workspace":"Worktree","card":"CARD-0711","requiredPlatform":"Any"}
            """);

        var created = await kit.Service(db).CreateAsync(request, kit.Caller, CancellationToken.None);

        var body = JsonSerializer.Serialize(created, Json);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("requiredPlatform", out var platform).ShouldBeTrue(body);
        platform.GetString().ShouldBe("Any");
        (await ReadStoredPlatformAsync(kit, created.Id)).ShouldBe("Any");
    }

    [Test]
    public async Task Desktop_alias_is_local()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: "server2", allowedRunnerId: "server2");
        await using var db = kit.Context();
        var request = Deserialize(
            """
            {"goal":"c710 desktop alias","title":"c710 desktop","role":"Code","agentKind":"ClaudeCode","workspace":"Shared","runnerId":"desktop"}
            """);

        var created = await Should.NotThrowAsync(() =>
            kit.Service(db).CreateAsync(request, kit.Caller, CancellationToken.None));

        var saved = await kit.ReadAsync(created.Id);
        saved.Task.RunnerId.ShouldBeNull("desktop is the local runner and is stored as null");
        saved.Task.Workspace.ShouldBe(WorkspaceMode.Shared, "a desktop alias must not trip remote-only workspace guards");
        var body = JsonSerializer.Serialize(created, Json);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("runnerId", out var runner).ShouldBeTrue(body);
        runner.GetString().ShouldBe("desktop");
    }

    private static CreateAgentTaskRequest Deserialize(string json) =>
        JsonSerializer.Deserialize<CreateAgentTaskRequest>(json, Json)
        ?? throw new InvalidOperationException("create JSON did not bind");

    private static async Task<Guid> SeedCardAsync(DefaultRunnerKit kit, string identifier)
    {
        await using var db = kit.Context();
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "c710 " + Guid.NewGuid().ToString("N")[..8],
            LocalRepositoryPath = kit.RepoRoot,
            CreatedAt = DateTime.UtcNow,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "c710",
            CreatedAt = DateTime.UtcNow,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Name = "Ready",
            StateKey = "ready",
            CreatedAt = DateTime.UtcNow,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = identifier,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Projects.Add(project);
        db.Boards.Add(board);
        db.BoardColumns.Add(column);
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        return card.Id;
    }

    /// <summary>
    /// Baseline schemas have no platform column. A failed stamp must not abort the later create
    /// assertion; once the column exists this is how the card default is seeded.
    /// </summary>
    private static async Task TryStampCardPlatformAsync(DefaultRunnerKit kit, Guid cardId, bool windows)
    {
        try
        {
            await using var db = kit.Context();
            // Stable domain values from the plan: Any = 0, Windows = 1, Linux = 2.
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "Cards" SET "RequiredPlatform" = {0} WHERE "Id" = {1}""",
                windows ? 1 : 0, cardId);
        }
        catch (Exception)
        {
            // The column is absent until the platform migration. The create response is the oracle.
        }
    }

    private static async Task<string?> ReadStoredPlatformAsync(DefaultRunnerKit kit, Guid taskId)
    {
        await using var db = kit.Context();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "RequiredPlatform" FROM "AgentTasks" WHERE "Id" = @id""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = taskId;
        command.Parameters.Add(parameter);
        var value = await command.ExecuteScalarAsync();
        return value switch
        {
            null or DBNull => null,
            0 or "0" or "Any" => "Any",
            1 or "1" or "Windows" => "Windows",
            2 or "2" or "Linux" => "Linux",
            _ => value.ToString(),
        };
    }
}
