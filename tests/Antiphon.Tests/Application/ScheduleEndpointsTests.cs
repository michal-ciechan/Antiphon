using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0057 S3 — HTTP surface for schedules. Isolated WAF schema.</summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ScheduleEndpointsTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly AntiphonWebAppFactory _factory;

    public ScheduleEndpointsTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task create_validates_target_zone_and_length()
    {
        using var client = _factory.CreateClient();
        var agent = await CreateAgentAsync(client);

        var noAgent = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "x",
            kind = "Prompt",
            repeat = "Once",
            promptText = "hi",
            fireAt = DateTime.UtcNow.AddMinutes(5),
        }, Json);
        noAgent.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var badZone = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "x",
            kind = "Prompt",
            repeat = "Once",
            agent = agent.Id.ToString(),
            promptText = "hi",
            fireAt = DateTime.UtcNow.AddMinutes(5),
            timeZoneId = "Not/AZone",
        }, Json);
        badZone.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var zoneBody = await badZone.Content.ReadAsStringAsync();
        zoneBody.ShouldContain("time zone");

        var tooLong = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "x",
            kind = "Prompt",
            repeat = "Once",
            agent = agent.Id.ToString(),
            promptText = new string('x', 16_001),
            fireAt = DateTime.UtcNow.AddMinutes(5),
        }, Json);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task patch_requires_the_concurrency_token()
    {
        using var client = _factory.CreateClient();
        var agent = await CreateAgentAsync(client);
        var created = await CreateOnceAsync(client, agent.Id, "patch me");

        var missing = await client.PatchAsJsonAsync($"/api/schedules/{created.Id}", new { enabled = false }, Json);
        missing.StatusCode.ShouldBeOneOf(
            HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest, HttpStatusCode.Conflict);

        var stale = await client.PatchAsJsonAsync(
            $"/api/schedules/{created.Id}",
            new { concurrencyToken = Guid.NewGuid(), enabled = false },
            Json);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var ok = await client.PatchAsJsonAsync(
            $"/api/schedules/{created.Id}",
            new { concurrencyToken = created.ConcurrencyToken, enabled = false },
            Json);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var patched = await ok.Content.ReadFromJsonAsync<ScheduleDto>(Json);
        patched!.Enabled.ShouldBeFalse();
        patched.ConcurrencyToken.ShouldNotBe(created.ConcurrencyToken);
    }

    [Test]
    public async Task start_spawn_without_accept_spend_is_422_with_the_preview()
    {
        using var client = _factory.CreateClient();
        var cardId = await SeedCardAsync();

        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "spawn thursday",
            kind = "Card",
            repeat = "Once",
            cardId,
            targetStatus = "InProgress",
            start = "Spawn",
            fireAt = DateTime.UtcNow.AddHours(1),
        }, Json);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("spend_unacknowledged");
        body.ShouldContain("preview");
        body.ShouldContain("immediate-session");
    }

    [Test]
    public async Task preview_resolves_each_card_identifier_form_to_the_seeded_guid()
    {
        using var client = _factory.CreateClient();
        var (cardId, identifier) = await SeedCardWithUnusedIdentifierAsync();
        var number = int.Parse(identifier["CARD-".Length..]);
        string[] forms =
        [
            identifier,
            $"card-{number}",
            $"#{number}",
            number.ToString(),
            cardId.ToString(),
        ];

        foreach (var form in forms)
        {
            var response = await client.PostAsJsonAsync("/api/schedules/preview", new
            {
                name = "card ident",
                kind = "Card",
                repeat = "Once",
                cardId = form,
                targetStatus = "InProgress",
                fireAt = DateTime.UtcNow.AddHours(1),
            }, Json);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, $"'{form}' should preview");
            var body = await response.Content.ReadFromJsonAsync<SchedulePreviewDto>(Json);
            body.ShouldNotBeNull();
            body.Target.CardId.ShouldBe(cardId, $"'{form}' names {identifier}");
        }
    }

    [Test]
    public async Task preview_garbage_card_id_is_422_keyed_on_CardId()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/schedules/preview", new
        {
            name = "garbage card",
            kind = "Card",
            repeat = "Once",
            cardId = "not-a-card",
            targetStatus = "InProgress",
            fireAt = DateTime.UtcNow.AddHours(1),
        }, Json);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        problem.GetProperty("errors").TryGetProperty("CardId", out _).ShouldBeTrue();
    }

    [Test]
    public async Task preview_unknown_guid_is_422_not_404()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/schedules/preview", new
        {
            name = "missing card",
            kind = "Card",
            repeat = "Once",
            cardId = Guid.NewGuid().ToString(),
            targetStatus = "InProgress",
            fireAt = DateTime.UtcNow.AddHours(1),
        }, Json);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        ((int)response.StatusCode).ShouldNotBe(404);
    }

    [Test]
    public async Task list_filters_by_card_identifier()
    {
        using var client = _factory.CreateClient();
        var (cardId, identifier) = await SeedCardWithUnusedIdentifierAsync();
        var created = await client.PostAsJsonAsync("/api/schedules", new
        {
            name = "list by ident",
            kind = "Card",
            repeat = "Once",
            cardId = identifier,
            targetStatus = "InProgress",
            fireAt = DateTime.UtcNow.AddHours(1),
        }, Json);
        created.EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<ScheduleListDto>(
            $"/api/schedules?cardId={Uri.EscapeDataString(identifier)}", Json);
        list.ShouldNotBeNull();
        list.Schedules.ShouldNotBeEmpty();
        list.Schedules.ShouldAllBe(s => s.CardId == cardId);
    }

    [Test]
    public async Task preview_writes_nothing()
    {
        using var client = _factory.CreateClient();
        var agent = await CreateAgentAsync(client);
        var before = await ListCountAsync(client, agent.Id);

        var preview = await client.PostAsJsonAsync("/api/schedules/preview", new
        {
            name = "preview only",
            kind = "Prompt",
            repeat = "Once",
            agent = agent.Id.ToString(),
            promptText = "do not persist",
            fireAt = DateTime.UtcNow.AddHours(1),
        }, Json);
        preview.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await preview.Content.ReadFromJsonAsync<SchedulePreviewDto>(Json);
        body.ShouldNotBeNull();
        body.NextOccurrences.Count.ShouldBeGreaterThanOrEqualTo(1);
        body.Target.AgentId.ShouldBe(agent.Id);

        (await ListCountAsync(client, agent.Id)).ShouldBe(before);
    }

    [Test]
    public async Task fire_now_ignores_grace_and_does_not_advance_the_recurrence()
    {
        using var client = _factory.CreateClient();
        var agent = await CreateAgentAsync(client);
        var created = await CreateOnceAsync(client, agent.Id, "fire now", minutesAhead: 120);
        var nextBefore = created.NextFireAt;

        var fire = await client.PostAsync($"/api/schedules/{created.Id}/fire-now", null);
        fire.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var after = await client.GetFromJsonAsync<ScheduleDto>($"/api/schedules/{created.Id}", Json);
        after.ShouldNotBeNull();
        after.NextFireAt.ShouldNotBeNull();
        nextBefore.ShouldNotBeNull();
        after.NextFireAt!.Value.ShouldBe(nextBefore.Value, TimeSpan.FromSeconds(1));
        after.FireCount.ShouldBe(created.FireCount + 1);
        after.Fires.ShouldNotBeNull();
        after.Fires!.ShouldContain(f => f.Manual);
    }

    private static async Task<AgentSummaryDto> CreateAgentAsync(HttpClient client)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sched-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var response = await client.PostAsJsonAsync("/api/agents", new
        {
            name = $"Sched {Guid.NewGuid():N}"[..16],
            workingDirectory = dir,
            alwaysOn = true,
        }, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AgentSummaryDto>(Json))!;
    }

    private static async Task<ScheduleDto> CreateOnceAsync(
        HttpClient client, Guid agentId, string name, int minutesAhead = 30)
    {
        var response = await client.PostAsJsonAsync("/api/schedules", new
        {
            name,
            kind = "Prompt",
            repeat = "Once",
            agent = agentId.ToString(),
            promptText = "hello from the clock",
            fireAt = DateTime.UtcNow.AddMinutes(minutesAhead),
        }, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ScheduleDto>(Json))!;
    }

    private async Task<Guid> SeedCardAsync() => (await SeedCardWithIdentifierAsync($"SCD-{Guid.NewGuid():N}"[..12])).Id;

    private async Task<(Guid Id, string Identifier)> SeedCardWithUnusedIdentifierAsync()
        => await SeedCardWithIdentifierAsync(await NextUnusedIdentifierAsync());

    /// <summary>A <c>CARD-nnnn</c> no row in the shared database holds. See CARD-0326.</summary>
    private async Task<string> NextUnusedIdentifierAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidate = $"CARD-{Random.Shared.Next(4_000, 9_999):0000}";
            if (!await db.Cards.AnyAsync(c => c.Identifier == candidate))
                return candidate;
        }

        throw new InvalidOperationException("No unused CARD-nnnn identifier available.");
    }

    private async Task<(Guid Id, string Identifier)> SeedCardWithIdentifierAsync(string identifier)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = new Antiphon.Server.Domain.Entities.Project
        {
            Id = Guid.NewGuid(),
            Name = $"sched-card-{Guid.NewGuid():N}"[..24],
            GitRepositoryUrl = "https://example.test/sched.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Antiphon.Server.Domain.Entities.Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Sched cards",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var column = new Antiphon.Server.Domain.Entities.BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var card = new Antiphon.Server.Domain.Entities.Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = "Scheduled card",
            Status = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return (card.Id, identifier);
    }

    private static async Task<int> ListCountAsync(HttpClient client, Guid agentId)
    {
        var list = await client.GetFromJsonAsync<ScheduleListDto>($"/api/schedules?agentId={agentId}", Json);
        return list?.Schedules.Count ?? 0;
    }
}
