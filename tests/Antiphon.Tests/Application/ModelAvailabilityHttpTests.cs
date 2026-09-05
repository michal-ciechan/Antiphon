using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
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
}

public sealed class ModelAvailabilityApiWebAppFactory : AntiphonWebAppFactory;
