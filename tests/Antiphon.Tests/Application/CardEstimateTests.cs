using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// Real HTTP, separate request scopes and an isolated migrated Postgres database.
// The factory refuses runner launches and disables the card-file publication tick.
[Category("Integration")]
[NotInParallel]
[ClassDataSource<CardFileSyncEndpointWebAppFactory>(Shared = SharedType.PerClass)]
public class CardEstimateTests(CardFileSyncEndpointWebAppFactory factory)
{
    private async Task<Guid> BoardAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"estimate-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.invalid/estimate.git",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return (await scope.ServiceProvider.GetRequiredService<BoardService>()
            .CreateAsync(new(project.Id, "Estimate board"), default)).Id;
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, Guid board, decimal? estimate)
    {
        using var response = await client.PostAsJsonAsync($"/api/boards/{board}/cards",
            new CreateCardRequest(null, "Estimate test", EstimateDays: estimate));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static decimal? Estimate(JsonElement card) =>
        card.TryGetProperty("estimateDays", out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetDecimal() : null;

    private static async Task<JsonElement> EditAsync(HttpClient client, JsonElement card,
        decimal? estimate = null, bool clear = false, string? title = null)
    {
        using var response = await client.PatchAsJsonAsync($"/api/cards/{card.GetProperty("id").GetGuid()}/content",
            new UpdateCardContentRequest(card.GetProperty("concurrencyToken").GetGuid(), "Revise estimate",
                Title: title, EstimateDays: estimate, ClearEstimate: clear));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Test]
    public async Task Create_with_estimate_round_trips_and_omits_the_field_when_unset()
    {
        var board = await BoardAsync();
        using var client = factory.CreateClient();
        foreach (var estimate in new[] { 1.5m, 0.01m, 365m })
        {
            var created = await CreateAsync(client, board, estimate);
            Estimate(created).ShouldBe(estimate);
            var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{created.GetProperty("id").GetGuid()}");
            Estimate(fetched).ShouldBe(estimate);
        }
        var unset = await CreateAsync(client, board, null);
        unset.TryGetProperty("estimateDays", out _).ShouldBeFalse();
        var edited = await EditAsync(client, unset, title: "Still unestimated");
        edited.TryGetProperty("estimateDays", out _).ShouldBeFalse();
        var revisions = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{unset.GetProperty("id").GetGuid()}/revisions");
        revisions.EnumerateArray().Single().TryGetProperty("estimateDays", out _).ShouldBeFalse();
    }

    [Test]
    public async Task An_estimate_edit_snapshots_the_superseded_value()
    {
        using var client = factory.CreateClient();
        var card = await CreateAsync(client, await BoardAsync(), 1.5m);
        var edited = await EditAsync(client, card, 3m);
        Estimate(edited).ShouldBe(3m);
        var titleEdit = await EditAsync(client, edited, title: "Title only");
        Estimate(titleEdit).ShouldBe(3m);
        var id = card.GetProperty("id").GetGuid();
        Estimate(await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}")).ShouldBe(3m);
        var revisions = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}/revisions");
        var ordered = revisions.EnumerateArray().OrderBy(r => r.GetProperty("revisionNumber").GetInt32()).ToArray();
        ordered.Length.ShouldBe(2);
        Estimate(ordered[0]).ShouldBe(1.5m);
        Estimate(ordered[1]).ShouldBe(3m);
    }

    [Test]
    public async Task Clear_estimate_sets_null_and_snapshots_the_old_value()
    {
        using var client = factory.CreateClient();
        var card = await CreateAsync(client, await BoardAsync(), 1.5m);
        var cleared = await EditAsync(client, card, clear: true);
        var id = card.GetProperty("id").GetGuid();
        var revisions = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}/revisions");
        Estimate(revisions.EnumerateArray().Single()).ShouldBe(1.5m);
        cleared.TryGetProperty("estimateDays", out _).ShouldBeFalse();
        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}");
        fetched.TryGetProperty("estimateDays", out _).ShouldBeFalse();
    }

    [Test]
    [Arguments("0", false)]
    [Arguments("-1", false)]
    [Arguments("365.01", false)]
    [Arguments("1.005", false)]
    [Arguments("2", true)]
    public async Task Estimate_out_of_range_or_with_clear_is_a_validation_error_and_writes_nothing(string value, bool clear)
    {
        using var client = factory.CreateClient();
        var board = await BoardAsync();
        var card = await CreateAsync(client, board, 1.5m);
        var id = card.GetProperty("id").GetGuid();
        var token = card.GetProperty("concurrencyToken").GetGuid();
        var estimate = decimal.Parse(value, CultureInfo.InvariantCulture);
        using var response = await client.PatchAsJsonAsync($"/api/cards/{id}/content",
            new UpdateCardContentRequest(token, "Invalid estimate", Title: "Must not persist",
                EstimateDays: estimate, ClearEstimate: clear));
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("errors").TryGetProperty(clear ? "clearEstimate" : "estimateDays", out _).ShouldBeTrue();
        var fetched = await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}");
        fetched.GetProperty("concurrencyToken").GetGuid().ShouldBe(token);
        fetched.GetProperty("revisionCount").GetInt32().ShouldBe(0);
        fetched.GetProperty("title").GetString().ShouldBe("Estimate test");
        Estimate(fetched).ShouldBe(1.5m);
        (await client.GetFromJsonAsync<JsonElement>($"/api/cards/{id}/revisions")).GetArrayLength().ShouldBe(0);
        if (!clear)
        {
            using var rejected = await client.PostAsJsonAsync($"/api/boards/{board}/cards",
                new CreateCardRequest(null, "Invalid create", EstimateDays: estimate));
            rejected.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            using var scope = factory.Services.CreateScope();
            (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Cards
                .CountAsync(c => c.BoardId == board)).ShouldBe(1);
        }
    }
}
