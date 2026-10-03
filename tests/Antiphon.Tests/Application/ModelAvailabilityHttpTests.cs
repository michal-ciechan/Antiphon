using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0309 S1 HTTP: GET/PUT/DELETE camelCase, 422 unknown alias / past until, 204 double-clear.
/// Isolated schema via <see cref="AntiphonWebAppFactory"/>.
/// </summary>
[NotInParallel]
[ClassDataSource<ModelAvailabilityApiWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class ModelAvailabilityHttpTests
{
    private readonly ModelAvailabilityApiWebAppFactory _factory;

    public ModelAvailabilityHttpTests(ModelAvailabilityApiWebAppFactory factory) => _factory = factory;

    [Test]
    [Arguments("Codex", "gpt-6-sol")]
    [Arguments("Codex", "gpt-5.6-terra")]
    [Arguments("Codex", "gpt-5.6-sol")]
    [Arguments("Grok", "grok-4.6")]
    public async Task Retired_manual_hold_is_listed_and_DELETE_clears_persisted_row(string kind, string alias)
    {
        using var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(), Kind = Enum.Parse<AgentKind>(kind), ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual, HitAt = DateTime.UtcNow,
            Reason = "retired manual hold", DisabledUntil = null,
        };
        db.ModelAvailabilityHolds.Add(row);
        await db.SaveChangesAsync();
        try
        {
            var snapshot = await client.GetFromJsonAsync<JsonElement>("/api/model-availability");
            snapshot.GetProperty("holds").EnumerateArray()
                .ShouldContain(h => h.GetProperty("id").GetGuid() == row.Id
                    && h.GetProperty("modelAlias").GetString() == alias);
            snapshot.GetProperty("available").EnumerateArray()
                .Select(v => v.GetString()).ShouldNotContain(alias);

            var cleared = await client.DeleteAsync($"/api/model-availability/{kind}/{alias}");
            cleared.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            db.ChangeTracker.Clear();
            var persisted = await db.ModelAvailabilityHolds.SingleAsync(h => h.Id == row.Id);
            persisted.ClearedAt.ShouldNotBeNull();
            persisted.ClearCause.ShouldBe(ModelAvailabilityClearCause.OperatorCleared);
            persisted.ReleasePendingAt.ShouldBe(persisted.ClearedAt);
            var pendingAt = persisted.ReleasePendingAt;

            (await client.DeleteAsync($"/api/model-availability/{kind}/{alias}"))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            db.ChangeTracker.Clear();
            (await db.ModelAvailabilityHolds.SingleAsync(h => h.Id == row.Id))
                .ReleasePendingAt.ShouldBe(pendingAt);
            var after = await client.GetFromJsonAsync<JsonElement>("/api/model-availability");
            after.GetProperty("holds").EnumerateArray()
                .ShouldNotContain(h => h.GetProperty("id").GetGuid() == row.Id);
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == row.Id).ExecuteDeleteAsync();
        }
    }

    [Test]
    [Arguments("gpt-6-sol")]
    [Arguments("gpt-5.6-terra")]
    [Arguments("gpt-5.6-sol")]
    public async Task PUT_accepts_retired_selectable_alias_and_converts_auto_hold(string alias)
    {
        using var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            var put = await client.PutAsJsonAsync($"/api/model-availability/Codex/{alias}",
                new { reason = "retired explicit profile" });
            put.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await put.Content.ReadFromJsonAsync<JsonElement>();
            var id = body.GetProperty("id").GetGuid();
            body.GetProperty("modelAlias").GetString().ShouldBe(alias);
            body.GetProperty("source").GetString().ShouldBe("Manual");
            body.GetProperty("disabledUntil").ValueKind.ShouldBe(JsonValueKind.Null);

            var row = await db.ModelAvailabilityHolds.SingleAsync(h => h.Id == id);
            row.Source = ModelAvailabilitySource.AutoDetected;
            row.DisabledUntil = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
            var converted = await client.PutAsJsonAsync($"/api/model-availability/Codex/{alias}",
                new { reason = "operator override" });
            converted.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await converted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id")
                .GetGuid().ShouldBe(id);
            db.ChangeTracker.Clear();
            var persisted = await db.ModelAvailabilityHolds.SingleAsync(h => h.Id == id);
            persisted.Source.ShouldBe(ModelAvailabilitySource.Manual);
            persisted.DisabledUntil.ShouldBeNull();
            persisted.Reason.ShouldBe("operator override");
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Kind == AgentKind.Codex
                && h.ModelAlias == alias).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Unknown_alias_PUT_and_DELETE_remain_422_without_persisting_a_hold()
    {
        using var client = _factory.CreateClient();
        var alias = $"unknown-{Guid.NewGuid():N}";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            var put = await client.PutAsJsonAsync($"/api/model-availability/Codex/{alias}",
                new { reason = "garbage" });
            put.StatusCode.ShouldBe((HttpStatusCode)422);
            (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")
                .TryGetProperty("alias", out _).ShouldBeTrue();
            var delete = await client.DeleteAsync($"/api/model-availability/Codex/{alias}");
            delete.StatusCode.ShouldBe((HttpStatusCode)422);
            (await delete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")
                .TryGetProperty("alias", out _).ShouldBeTrue();
            (await db.ModelAvailabilityHolds.AnyAsync(h => h.ModelAlias == alias)).ShouldBeFalse();
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.ModelAlias == alias).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Put_get_delete_round_trip_is_camelCase_and_double_clear_is_204()
    {
        using var client = _factory.CreateClient();
        var until = DateTime.UtcNow.AddDays(2);
        until = new DateTime(until.Year, until.Month, until.Day, 12, 0, 0, DateTimeKind.Utc);

        try
        {
            var put = await client.PutAsJsonAsync(
                "/api/model-availability/ClaudeCode/fable",
                new { disabledUntil = until.ToString("yyyy-MM-ddTHH:mm:ssZ"), reason = "http pin" });
            put.StatusCode.ShouldBe(HttpStatusCode.OK);
            var putJson = await put.Content.ReadFromJsonAsync<JsonElement>();
            putJson.GetProperty("kind").GetString().ShouldBe("ClaudeCode");
            putJson.GetProperty("modelAlias").GetString().ShouldBe("fable");
            putJson.GetProperty("source").GetString().ShouldBe("Manual");
            putJson.GetProperty("reason").GetString().ShouldBe("http pin");
            putJson.TryGetProperty("disabledUntil", out _).ShouldBeTrue();

            var get = await client.GetAsync("/api/model-availability");
            get.StatusCode.ShouldBe(HttpStatusCode.OK);
            var snapshot = await get.Content.ReadFromJsonAsync<JsonElement>();
            snapshot.GetProperty("holds").EnumerateArray()
                .Select(h => h.GetProperty("modelAlias").GetString())
                .ShouldContain("fable");
            snapshot.GetProperty("available").EnumerateArray()
                .Select(v => v.GetString())
                .ShouldNotContain("fable");

            var del = await client.DeleteAsync("/api/model-availability/ClaudeCode/fable");
            del.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var delAgain = await client.DeleteAsync("/api/model-availability/ClaudeCode/fable");
            delAgain.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        finally
        {
            await client.DeleteAsync("/api/model-availability/ClaudeCode/fable");
        }
    }

    [Test]
    public async Task Put_unknown_alias_and_past_until_are_422()
    {
        using var client = _factory.CreateClient();

        var unknown = await client.PutAsJsonAsync(
            "/api/model-availability/ClaudeCode/not-a-model",
            new { reason = "nope" });
        unknown.StatusCode.ShouldBe((HttpStatusCode)422);
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        unknownBody.GetProperty("errors").TryGetProperty("alias", out _).ShouldBeTrue();

        var past = await client.PutAsJsonAsync(
            "/api/model-availability/ClaudeCode/fable",
            new { disabledUntil = DateTime.UtcNow.AddMinutes(-5).ToString("yyyy-MM-ddTHH:mm:ssZ") });
        past.StatusCode.ShouldBe((HttpStatusCode)422);
        var pastBody = await past.Content.ReadFromJsonAsync<JsonElement>();
        pastBody.GetProperty("errors").TryGetProperty("disabledUntil", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Put_star_is_kind_wide()
    {
        using var client = _factory.CreateClient();
        try
        {
            var put = await client.PutAsJsonAsync(
                "/api/model-availability/ClaudeCode/%2A",
                new { reason = "kind-wide" });
            put.StatusCode.ShouldBe(HttpStatusCode.OK);
            var json = await put.Content.ReadFromJsonAsync<JsonElement>();
            json.GetProperty("modelAlias").GetString().ShouldBe("*");
            json.GetProperty("source").GetString().ShouldBe("Manual");

            var get = await client.GetAsync("/api/model-availability");
            var snapshot = await get.Content.ReadFromJsonAsync<JsonElement>();
            snapshot.GetProperty("available").EnumerateArray()
                .Select(v => v.GetString())
                .ShouldNotContain("haiku");
        }
        finally
        {
            await client.DeleteAsync("/api/model-availability/ClaudeCode/%2A");
        }
    }

    [Test]
    public async Task Card0412_V06_http_delete_is_204_and_repeat_is_204()
    {
        using var client = _factory.CreateClient();
        var until = DateTime.UtcNow.AddDays(1);
        try
        {
            var put = await client.PutAsJsonAsync(
                "/api/model-availability/ClaudeCode/sonnet",
                new { disabledUntil = until.ToString("yyyy-MM-ddTHH:mm:ssZ"), reason = "v06" });
            put.StatusCode.ShouldBe(HttpStatusCode.OK);
            var del = await client.DeleteAsync("/api/model-availability/ClaudeCode/sonnet");
            del.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var delAgain = await client.DeleteAsync("/api/model-availability/ClaudeCode/sonnet");
            delAgain.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var get = await client.GetAsync("/api/model-availability");
            var snapshot = await get.Content.ReadFromJsonAsync<JsonElement>();
            snapshot.GetProperty("holds").EnumerateArray()
                .Select(h => h.GetProperty("modelAlias").GetString())
                .ShouldNotContain("sonnet");
        }
        finally
        {
            await client.DeleteAsync("/api/model-availability/ClaudeCode/sonnet");
        }
    }
}

public sealed class ModelAvailabilityApiWebAppFactory : AntiphonWebAppFactory;
