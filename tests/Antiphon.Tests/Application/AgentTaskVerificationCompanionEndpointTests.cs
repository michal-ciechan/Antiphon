using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0552 V-552-40..49 (D-7). The backfill and recovery door: the same writer the land runs,
/// idempotent, with a named refusal for every state that has nothing to verify.
/// </summary>
[NotInParallel]
[ClassDataSource<LandContractWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class AgentTaskVerificationCompanionEndpointTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly LandContractWebAppFactory _factory;

    public AgentTaskVerificationCompanionEndpointTests(LandContractWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task C552_E01_PostCreatesCompanionAndReturnsCoordinates()
    {
        var world = await SeedAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(Route(world.Task.Id), null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body.TaskId.ShouldBe(world.Task.Id);
        body.OperationId.ShouldBe(world.Operation.Id);
        body.Identifier.ShouldBe("CARD-0002");
        body.Created.ShouldBeTrue();
        body.Linked.ShouldBeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards.SingleAsync(c => c.Id == body.CardId);
        card.BoardId.ShouldBe(world.BoardId);
        card.BoardColumnId.ShouldBe(world.BacklogColumnId);
        card.Status.ShouldBe(CardStatus.Backlog);
        BoardService.ParseLabels(card.LabelsJson).ShouldContain("post-land-verification");
        (await db.AgentTaskLandings.SingleAsync(o => o.Id == world.Operation.Id))
            .VerificationCardId.ShouldBe(body.CardId);
    }

    [Test]
    public async Task C552_E02_SecondPostIsIdempotent()
    {
        var world = await SeedAsync();
        using var client = _factory.CreateClient();
        var first = await ReadAsync(await client.PostAsync(Route(world.Task.Id), null));

        var response = await client.PostAsync(Route(world.Task.Id), null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body.CardId.ShouldBe(first.CardId);
        body.Created.ShouldBeFalse();
        body.Linked.ShouldBeFalse();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Cards.CountAsync(c => c.BoardId == world.BoardId)).ShouldBe(2);
        (await db.CardRevisions.CountAsync(r => r.CardId == first.CardId)).ShouldBe(1);
        (await db.CardRevisions.CountAsync(r => r.CardId == world.OriginalCardId)).ShouldBe(1);
    }

    [Test]
    public async Task C552_E03_ShortIdResolves()
    {
        var world = await SeedAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(Route(DelegationReportFormatter.Short(world.Task.Id)), null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).TaskId.ShouldBe(world.Task.Id);
    }

    [Test]
    [Arguments("none")]
    [Arguments("unconfirmed")]
    public async Task C552_E04_UnconfirmedIs409(string variant)
    {
        var world = await SeedAsync(landing: variant == "unconfirmed" ? "unconfirmed" : "none");
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(Route(world.Task.Id), null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(response)).ShouldBe("verification_publication_unconfirmed");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Cards.CountAsync(c => c.BoardId == world.BoardId)).ShouldBe(1);
    }

    [Test]
    [Arguments("mutation-role")]
    [Arguments("sourced")]
    public async Task C552_E05_MutationOrSourcedTaskIs409(string variant)
    {
        var world = await SeedAsync(role: variant == "mutation-role" ? AgentTaskRole.Mutation : AgentTaskRole.Code,
            sourced: variant == "sourced");
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(Route(world.Task.Id), null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(response)).ShouldBe("verification_publication_forbidden");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Cards.CountAsync(c => c.BoardId == world.BoardId)).ShouldBe(1);
    }

    [Test]
    public async Task C552_E06_CardlessTaskIs409()
    {
        var world = await SeedAsync(bindCard: false);
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(Route(world.Task.Id), null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadCodeAsync(response)).ShouldBe("verification_companion_requires_card");
    }

    [Test]
    public async Task C552_E07_UnknownTaskIs404()
    {
        using var client = _factory.CreateClient();

        (await client.PostAsync(Route(Guid.NewGuid()), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task C552_E08_TaskGetExposesVerificationCardId()
    {
        var world = await SeedAsync();
        using var client = _factory.CreateClient();

        var before = await client.GetFromJsonAsync<JsonElement>($"/api/agent-tasks/{world.Task.Id}", Json);
        VerificationCardId(before).ShouldBeNull();

        var body = await ReadAsync(await client.PostAsync(Route(world.Task.Id), null));

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/agent-tasks/{world.Task.Id}", Json);
        VerificationCardId(after).ShouldBe(body.CardId);
    }

    [Test]
    public async Task C552_E09_NewestConfirmedLandingIsChosenWhenNoActiveLanding()
    {
        var world = await SeedAsync(landing: "none");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == world.Task.Id);
        await LandContractSeeds.SeedConfirmedLandingAsync(db, task, DateTime.UtcNow.AddHours(-2), active: false);
        var newer = await LandContractSeeds.SeedConfirmedLandingAsync(db, task, DateTime.UtcNow.AddHours(-1), active: false);
        task.ActiveLandingId = null;
        await db.SaveChangesAsync();

        using var client = _factory.CreateClient();
        var body = await ReadAsync(await client.PostAsync(Route(world.Task.Id), null));

        body.OperationId.ShouldBe(newer.Id);
    }

    [Test]
    public async Task C552_E10_PostWaitsForTheOwnerRowLock()
    {
        var world = await SeedAsync();
        string connection;
        using (var probe = _factory.Services.CreateScope())
            connection = probe.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
        await using var holder = new NpgsqlConnection(connection);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var select = new NpgsqlCommand(
            "SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = @id FOR UPDATE", holder, transaction))
        {
            select.Parameters.AddWithValue("id", world.Task.Id);
            await select.ExecuteNonQueryAsync();
        }

        using var client = _factory.CreateClient();
        var post = client.PostAsync(Route(world.Task.Id), null);
        await Task.Delay(500);

        post.IsCompleted.ShouldBeFalse();

        await transaction.CommitAsync();
        var stopwatch = Stopwatch.StartNew();
        var response = await post.WaitAsync(TimeSpan.FromSeconds(5));
        stopwatch.Stop();
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Cards.CountAsync(c => c.BoardId == world.BoardId)).ShouldBe(2);
    }

    private static string Route(object id) => $"/api/agent-tasks/{id}/verification-companion";

    private static Guid? VerificationCardId(JsonElement task) =>
        task.TryGetProperty("landing", out var landing)
        && landing.ValueKind == JsonValueKind.Object
        && landing.TryGetProperty("verificationCardId", out var id)
        && id.ValueKind != JsonValueKind.Null
            ? id.GetGuid()
            : null;

    private static async Task<CompanionBody> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<CompanionBody>(Json))!;

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<World> SeedAsync(
        string landing = "confirmed",
        AgentTaskRole role = AgentTaskRole.Code,
        bool sourced = false,
        bool bindCard = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (boardId, originalCardId, backlogColumnId) = await LandContractSeeds.SeedBoardWithOriginalAsync(db);
        var task = await LandContractSeeds.SeedSucceededWorktreeAsync(db, bindCard ? originalCardId : null);
        if (role != AgentTaskRole.Code)
        {
            task.Role = role;
            await db.SaveChangesAsync();
        }

        AgentTaskLanding? op = landing switch
        {
            "confirmed" => await LandContractSeeds.SeedConfirmedLandingAsync(db, task, DateTime.UtcNow.AddHours(-1)),
            "unconfirmed" => await LandContractSeeds.SeedConfirmedLandingAsync(db, task, null),
            _ => null,
        };

        if (sourced)
        {
            // SourceLandingOperationId is read-only after save, so the sourced owner is a second
            // row inserted with it rather than the seeded task mutated.
            var sourcedId = Guid.NewGuid();
            var sourcedTask = new AgentTask
            {
                Id = sourcedId, RootTaskId = sourcedId, Title = "sourced", Goal = "sourced",
                Kind = AgentTaskKind.Worker, Role = role, Status = AgentTaskStatus.Succeeded,
                Workspace = WorkspaceMode.Worktree, WorkingDirectory = task.WorkingDirectory,
                RepoPath = task.RepoPath, CardId = bindCard ? originalCardId : null,
                SourceLandingOperationId = op!.Id, SourceLandingSha = op.VerifiedSourceSha,
                CreatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
            };
            db.AgentTasks.Add(sourcedTask);
            await db.SaveChangesAsync();
            return new World(sourcedTask, op, boardId, originalCardId, backlogColumnId);
        }

        return new World(task, op!, boardId, originalCardId, backlogColumnId);
    }

    private sealed record World(
        AgentTask Task, AgentTaskLanding Operation, Guid BoardId, Guid OriginalCardId, Guid BacklogColumnId);

    private sealed record CompanionBody(
        Guid TaskId, Guid OperationId, Guid CardId, string Identifier, bool Created, bool Linked);
}
