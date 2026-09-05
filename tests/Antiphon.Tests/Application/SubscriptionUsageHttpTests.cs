using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0333 S1 HTTP: GET /api/subscription-usage is a display-safe projection of stored
/// snapshots. Empty array when none exist; nulls preserved; raw sample/profile/session
/// details must not appear. Isolated schema via <see cref="AntiphonWebAppFactory"/>.
/// </summary>
[NotInParallel]
[ClassDataSource<SubscriptionUsageApiWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public sealed class SubscriptionUsageHttpTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    private static readonly HashSet<string> AllowedObservationNames =
        new(StringComparer.Ordinal)
        {
            "provider",
            "planLabel",
            "remainingPercent",
            "resetsAt",
            "observedAt",
            "age",
        };

    private readonly SubscriptionUsageApiWebAppFactory _factory;

    public SubscriptionUsageHttpTests(SubscriptionUsageApiWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public async Task ResetAsync()
    {
        await _factory.ResetAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.SubscriptionUsageSamples.ExecuteDeleteAsync();
    }

    [Test]
    public async Task Empty_data_returns_empty_array()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/subscription-usage");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.ShouldBe(JsonValueKind.Array);
        json.GetArrayLength().ShouldBe(0);
    }

    [Test]
    public async Task Full_codex_observation_is_camelCase()
    {
        var observedAt = DateTime.UtcNow.AddMinutes(-15);
        var resetsAt = DateTime.UtcNow.AddHours(36);
        await SeedAsync(new SubscriptionUsageSample
        {
            Id = Guid.NewGuid(),
            Provider = AgentKind.Codex,
            SubscriptionKey = Guid.NewGuid().ToString("D"),
            PlanLabel = "ChatGPT Plus",
            RemainingPercent = 73.5,
            ResetsAt = resetsAt,
            ResetsAtRaw = "12:00 on 6 September",
            ObservedAt = observedAt,
            AgentId = Guid.NewGuid(),
            AgentSessionId = Guid.NewGuid(),
            SourceCommand = "/status",
            ParseStatus = SubscriptionUsageParseStatus.Parsed,
            RawExcerpt = "Weekly limit: 73.5% left",
        });

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/subscription-usage");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.ValueKind.ShouldBe(JsonValueKind.Array);
        json.GetArrayLength().ShouldBe(1);
        var obs = json[0];
        obs.GetProperty("provider").GetString().ShouldBe("Codex");
        obs.GetProperty("planLabel").GetString().ShouldBe("ChatGPT Plus");
        obs.GetProperty("remainingPercent").GetDouble().ShouldBe(73.5);
        obs.GetProperty("resetsAt").ValueKind.ShouldBe(JsonValueKind.String);
        obs.GetProperty("observedAt").ValueKind.ShouldBe(JsonValueKind.String);
        var age = ParseAge(obs.GetProperty("age"));
        age.ShouldBeGreaterThan(TimeSpan.FromMinutes(10));
        age.ShouldBeLessThan(TimeSpan.FromMinutes(30));
        PropertyNames(obs).ShouldBe(AllowedObservationNames, ignoreOrder: true);
    }

    [Test]
    public async Task Optional_reset_and_plan_label_are_json_null()
    {
        await SeedAsync(new SubscriptionUsageSample
        {
            Id = Guid.NewGuid(),
            Provider = AgentKind.Codex,
            SubscriptionKey = Guid.NewGuid().ToString("D"),
            PlanLabel = null,
            RemainingPercent = 17,
            ResetsAt = null,
            ResetsAtRaw = null,
            ObservedAt = DateTime.UtcNow.AddMinutes(-3),
            AgentSessionId = Guid.NewGuid(),
            SourceCommand = "/status",
            ParseStatus = SubscriptionUsageParseStatus.Parsed,
            RawExcerpt = "Weekly limit: 17% left",
        });

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/subscription-usage");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var obs = json.EnumerateArray().ShouldHaveSingleItem();
        obs.GetProperty("provider").GetString().ShouldBe("Codex");
        obs.GetProperty("planLabel").ValueKind.ShouldBe(JsonValueKind.Null);
        obs.GetProperty("resetsAt").ValueKind.ShouldBe(JsonValueKind.Null);
        obs.GetProperty("remainingPercent").GetDouble().ShouldBe(17);
        obs.TryGetProperty("remainingPercent", out _).ShouldBeTrue();
    }

    [Test]
    public void Optional_percent_and_reset_serialize_as_json_null()
    {
        var dto = new SubscriptionUsageObservationDto(
            AgentKind.Codex,
            PlanLabel: null,
            RemainingPercent: null,
            ResetsAt: null,
            ObservedAt: new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc),
            Age: TimeSpan.FromMinutes(4));

        var json = JsonSerializer.Serialize(dto, Json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("planLabel").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("remainingPercent").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("resetsAt").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("provider").GetString().ShouldBe("Codex");
        json.ShouldNotContain("subscriptionKey");
        json.ShouldNotContain("rawExcerpt");
        json.ShouldNotContain("sourceCommand");
        json.ShouldNotContain("agentSessionId");
        PropertyNames(root).ShouldBe(AllowedObservationNames, ignoreOrder: true);
    }

    [Test]
    public async Task Raw_sample_profile_and_session_details_are_absent()
    {
        var leakKey = "leak-sub-key-" + Guid.NewGuid().ToString("N");
        var leakExcerpt = "LEAK-RAW-EXCERPT-" + Guid.NewGuid().ToString("N");
        var leakCommand = "/status leak-command-" + Guid.NewGuid().ToString("N");
        var leakResetRaw = "leak-reset-raw-" + Guid.NewGuid().ToString("N");
        var leakAgentId = Guid.NewGuid();
        var leakSessionId = Guid.NewGuid();
        var hiddenExcerpt = "HIDDEN-UNPARSED-EXCERPT-" + Guid.NewGuid().ToString("N");

        await SeedAsync(new SubscriptionUsageSample
        {
            Id = Guid.NewGuid(),
            Provider = AgentKind.Codex,
            SubscriptionKey = leakKey,
            PlanLabel = "Plus",
            RemainingPercent = 40,
            ResetsAt = DateTime.UtcNow.AddHours(12),
            ResetsAtRaw = leakResetRaw,
            ObservedAt = DateTime.UtcNow.AddMinutes(-8),
            AgentId = leakAgentId,
            AgentSessionId = leakSessionId,
            SourceCommand = leakCommand,
            ParseStatus = SubscriptionUsageParseStatus.Parsed,
            RawExcerpt = leakExcerpt,
        });
        await SeedAsync(new SubscriptionUsageSample
        {
            Id = Guid.NewGuid(),
            Provider = AgentKind.Grok,
            SubscriptionKey = "hidden-" + Guid.NewGuid().ToString("N"),
            RemainingPercent = null,
            ObservedAt = DateTime.UtcNow.AddMinutes(-1),
            AgentSessionId = Guid.NewGuid(),
            SourceCommand = "/usage",
            ParseStatus = SubscriptionUsageParseStatus.Unparsed,
            RawExcerpt = hiddenExcerpt,
        });

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/subscription-usage");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        json.ValueKind.ShouldBe(JsonValueKind.Array);
        json.GetArrayLength().ShouldBe(1);
        var obs = json[0];
        obs.GetProperty("provider").GetString().ShouldBe("Codex");
        obs.GetProperty("planLabel").GetString().ShouldBe("Plus");
        obs.GetProperty("remainingPercent").GetDouble().ShouldBe(40);

        PropertyNames(json).ShouldBe(AllowedObservationNames, ignoreOrder: true);

        body.ShouldNotContain(leakKey);
        body.ShouldNotContain(leakExcerpt);
        body.ShouldNotContain(leakCommand);
        body.ShouldNotContain(leakResetRaw);
        body.ShouldNotContain(leakAgentId.ToString("D"));
        body.ShouldNotContain(leakSessionId.ToString("D"));
        body.ShouldNotContain(hiddenExcerpt);
        body.ShouldNotContain("subscriptionKey");
        body.ShouldNotContain("rawExcerpt");
        body.ShouldNotContain("sourceCommand");
        body.ShouldNotContain("resetsAtRaw");
        body.ShouldNotContain("agentSessionId");
        body.ShouldNotContain("\"agentId\"");
    }

    [Test]
    public async Task Get_does_not_poll_or_enable_monitoring()
    {
        using var scope = _factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<SubscriptionUsageMonitoringSettings>>().Value;
        settings.Enabled.ShouldBeFalse();
        var launchesBefore = _factory.SessionRunner.LaunchAttempts.Count;
        var sampleCountBefore = await scope.ServiceProvider
            .GetRequiredService<AppDbContext>()
            .SubscriptionUsageSamples.CountAsync();

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/subscription-usage");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var after = _factory.Services.CreateScope();
        var settingsAfter = after.ServiceProvider.GetRequiredService<IOptions<SubscriptionUsageMonitoringSettings>>().Value;
        settingsAfter.Enabled.ShouldBeFalse();
        settingsAfter.IncludeDegradedProviders.ShouldBeFalse();
        _factory.SessionRunner.LaunchAttempts.Count.ShouldBe(launchesBefore);
        var sampleCountAfter = await after.ServiceProvider
            .GetRequiredService<AppDbContext>()
            .SubscriptionUsageSamples.CountAsync();
        sampleCountAfter.ShouldBe(sampleCountBefore);
    }

    private async Task SeedAsync(SubscriptionUsageSample sample)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SubscriptionUsageSamples.Add(sample);
        await db.SaveChangesAsync();
    }

    private static TimeSpan ParseAge(JsonElement age)
    {
        age.ValueKind.ShouldBe(JsonValueKind.String);
        var raw = age.GetString();
        raw.ShouldNotBeNullOrWhiteSpace();
        return TimeSpan.Parse(raw);
    }

    private static HashSet<string> PropertyNames(JsonElement el)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Collect(el, names);
        return names;
    }

    private static void Collect(JsonElement el, HashSet<string> names)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    names.Add(p.Name);
                    Collect(p.Value, names);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    Collect(item, names);
                break;
        }
    }
}

public sealed class SubscriptionUsageApiWebAppFactory : AntiphonWebAppFactory;
