using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0881. The scoped pipeline route is the effective-settings read.</summary>
[Category("Integration")]
[NotInParallel("card-0505-advisory-lock")]
public class EffectiveSettingsPipelineTests
{
    private static readonly HashSet<string> VolatileRunnerFields = new(StringComparer.Ordinal)
    {
        "capacityObservedAt",
        "platformObservedAt",
        "codexCliVersionCheckedAtUtc",
    };

    [Test]
    [Timeout(180_000)]
    public async Task Scoped_read_carries_runners_and_defaults_matching_their_routes()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await StartAsync(schema.ConnectionString);
        var project = await SeedProjectAsync(host, "P");
        await SeedTaskAsync(host, project, AgentTaskRole.Code, AgentTaskStatus.Working, retained: false);
        await SeedTaskAsync(host, project, AgentTaskRole.Code, AgentTaskStatus.Working, retained: true);
        await InitConcurrencyAsync(host);
        await InitDefaultsAsync(host);

        using var scoped = await GetOkAsync(host, $"/api/agent-tasks/pipeline?projectId={project:D}");
        using var fleet = await GetOkAsync(host, "/api/agent-tasks/pipeline");
        using var catalogue = await GetOkAsync(host, "/api/session-runners");
        var root = scoped.RootElement;
        root.TryGetProperty("runners", out var runners).ShouldBeTrue("runners");
        root.TryGetProperty("runnerDefaults", out var defaults).ShouldBeTrue("runnerDefaults");
        JsonEqual(runners, catalogue.RootElement, VolatileRunnerFields);

        var desktop = Runner(runners, "desktop");
        desktop.GetProperty("capacityKind").GetString().ShouldBe("delegatedTasks");
        desktop.GetProperty("capacity").GetInt32().ShouldBe(2);
        desktop.GetProperty("occupied").GetInt32().ShouldBe(1);
        var remote = Runner(runners, "grok-linux");
        remote.GetProperty("dispatchEligible").GetBoolean().ShouldBeFalse();
        remote.GetProperty("capacity").ValueKind.ShouldBe(JsonValueKind.Null);

        defaults.GetProperty("revision").GetInt64().ShouldBe(1);
        defaults.GetProperty("globalRunnerId").GetString().ShouldBe("grok-linux");
        defaults.GetProperty("kindDefaults").GetArrayLength().ShouldBe(0);
        defaults.GetProperty("supportedKinds").GetArrayLength().ShouldBe(3);

        fleet.RootElement.TryGetProperty("runners", out var fleetRunners).ShouldBeTrue("runners");
        fleet.RootElement.TryGetProperty("runnerDefaults", out var fleetDefaults).ShouldBeTrue("runnerDefaults");
        JsonEqual(fleetRunners, runners, VolatileRunnerFields);
        JsonEqual(fleetDefaults, defaults, VolatileRunnerFields);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Uninitialised_defaults_read_null_and_seed_nothing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await StartAsync(schema.ConnectionString);
        var project = await SeedProjectAsync(host, "P");
        await InitConcurrencyAsync(host);

        using (var before = await GetOkAsync(host, $"/api/agent-tasks/pipeline?projectId={project:D}"))
        {
            before.RootElement.TryGetProperty("runnerDefaults", out var defaults).ShouldBeTrue("runnerDefaults");
            defaults.ValueKind.ShouldBe(JsonValueKind.Null);
        }

        (await CountAsync(host, db => db.RunnerRoutingSettings.CountAsync())).ShouldBe(0);

        await InitDefaultsAsync(host);
        var revisions = await CountAsync(host, db => db.RunnerRoutingRevisions.CountAsync());
        using (var seeded = await GetOkAsync(host, $"/api/agent-tasks/pipeline?projectId={project:D}"))
            seeded.RootElement.GetProperty("runnerDefaults").GetProperty("revision").GetInt64().ShouldBe(1);

        await ReadOkAsync(host, $"/api/agent-tasks/pipeline?projectId={project:D}");
        await ReadOkAsync(host, "/api/agent-tasks/pipeline");

        (await CountAsync(host, db => db.RunnerRoutingSettings.CountAsync())).ShouldBe(1);
        (await SingleRevisionAsync(host)).ShouldBe(1);
        (await CountAsync(host, db => db.RunnerRoutingRevisions.CountAsync())).ShouldBe(revisions);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Host_remaining_clamps_and_open_count_excludes_blocked_and_specialists()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await StartAsync(schema.ConnectionString);
        var project = await SeedProjectAsync(host, "P");
        await SeedTaskAsync(host, project, AgentTaskRole.Code, AgentTaskStatus.Working, retained: false);
        await SeedTaskAsync(host, project, AgentTaskRole.Plan, AgentTaskStatus.Queued, retained: false);
        await SeedTaskAsync(host, project, AgentTaskRole.Review, AgentTaskStatus.Blocked, retained: false);
        await SeedTaskAsync(host, project, AgentTaskRole.Check, AgentTaskStatus.Working, retained: false);
        await InitConcurrencyAsync(host);

        using (var first = await GetOkAsync(host, $"/api/agent-tasks/pipeline?projectId={project:D}"))
        {
            var root = first.RootElement;
            root.GetProperty("taskScope").GetString().ShouldBe("project");
            var local = Host(root, "local");
            local.TryGetProperty("remaining", out var remaining).ShouldBeTrue("remaining");
            local.GetProperty("inFlight").GetInt32().ShouldBe(1);
            local.GetProperty("effectiveLimit").GetInt32().ShouldBe(2);
            remaining.GetInt32().ShouldBe(1);
            local.GetProperty("source").GetString().ShouldBe("config");
            local.GetProperty("scope").GetString().ShouldBe("fleet");
            var remote = Host(root, "grok-linux");
            remote.GetProperty("effectiveLimit").ValueKind.ShouldBe(JsonValueKind.Null);
            remote.TryGetProperty("remaining", out var remoteRemaining).ShouldBeTrue("remaining");
            remoteRemaining.ValueKind.ShouldBe(JsonValueKind.Null);
            remote.GetProperty("scope").GetString().ShouldBe("fleet");

            var scope = root.GetProperty("concurrencyScopes").EnumerateArray().Single();
            scope.GetProperty("projectId").GetGuid().ShouldBe(project);
            scope.GetProperty("effective").GetProperty("maxParallel").GetInt32().ShouldBe(6);
            scope.GetProperty("effective").GetProperty("maxParallelSource").GetString().ShouldBe("default");
            var occupancy = scope.GetProperty("occupancy");
            occupancy.GetProperty("open").GetInt32().ShouldBe(2);
            occupancy.GetProperty("parallel").GetInt32().ShouldBe(1);
            occupancy.GetProperty("queued").GetInt32().ShouldBe(1);
            occupancy.GetProperty("parallelRemaining").GetInt32().ShouldBe(4);
            Role(occupancy, "Code").GetProperty("open").GetInt32().ShouldBe(1);
            Role(occupancy, "Code").GetProperty("parallelRemaining").GetInt32().ShouldBe(1);
            Role(occupancy, "Review").GetProperty("open").GetInt32().ShouldBe(0);
        }

        await using (var scope = host.App.Services.CreateAsyncScope())
        {
            var budgets = scope.ServiceProvider.GetRequiredService<HostBudgetService>();
            await budgets.UpsertAsync("local", 0, "drain", CancellationToken.None);
        }

        using var drained = await GetOkAsync(host, $"/api/agent-tasks/pipeline?projectId={project:D}");
        var drainedLocal = Host(drained.RootElement, "local");
        drainedLocal.GetProperty("effectiveLimit").GetInt32().ShouldBe(0);
        drainedLocal.GetProperty("inFlight").GetInt32().ShouldBe(1);
        drainedLocal.GetProperty("remaining").GetInt32().ShouldBe(0);
        drainedLocal.GetProperty("source").GetString().ShouldBe("budget");
    }

    private static Task<PhoneHomeTestHost> StartAsync(string connectionString) =>
        PhoneHomeTestHost.StartAsync(connectionString: connectionString,
            configureServices: services =>
            {
                services.AddSingleton<ISessionRunnerDirectory>(sp => sp.GetRequiredService<PhoneHomeRunnerDirectory>());
                services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings
                {
                    MaxConcurrentTasks = 2,
                    MaxOpenTasks = 6,
                    DefaultRunnerId = "grok-linux",
                }));
                services.AddScoped<HostBudgetService>();
                services.AddScoped<DispatchConcurrencySettingsService>();
                services.AddScoped<RunnerDefaultSettingsService>();
                services.AddScoped<AreaMapLoader>();
                services.AddScoped<AgentTaskPipelineStatusService>();
                services.ConfigureHttpJsonOptions(options =>
                {
                    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
                });
            },
            mapEndpoints: app => app.MapGet("/api/agent-tasks/pipeline", AgentTaskEndpoints.ReadPipelineAsync));

    private static async Task InitConcurrencyAsync(PhoneHomeTestHost host)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<DispatchConcurrencySettingsService>();
        await settings.EnsureInitializedAsync(CancellationToken.None);
    }

    private static async Task InitDefaultsAsync(PhoneHomeTestHost host)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<RunnerDefaultSettingsService>();
        await settings.EnsureInitializedAsync(CancellationToken.None);
    }

    private static async Task<Guid> SeedProjectAsync(PhoneHomeTestHost host, string name)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        db.Projects.Add(new Project
        {
            Id = id,
            Name = name,
            GitRepositoryUrl = $"https://example.test/{name}.git",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SeedTaskAsync(
        PhoneHomeTestHost host,
        Guid projectId,
        AgentTaskRole role,
        AgentTaskStatus status,
        bool retained)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = $"c0881-{role}-{status}",
            Goal = $"c0881-{id:N}",
            Kind = AgentTaskKind.Worker,
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Status = status,
            ProjectId = projectId,
            Workspace = WorkspaceMode.ReadOnly,
            WorkingDirectory = Path.GetTempPath(),
            CapacityWaitRetained = retained,
            CreatedAt = now,
            DispatchedAt = status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working ? now : null,
            ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonDocument> GetOkAsync(PhoneHomeTestHost host, string path)
    {
        using var response = await host.Http.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body);
    }

    private static async Task ReadOkAsync(PhoneHomeTestHost host, string path)
    {
        using var document = await GetOkAsync(host, path);
    }

    private static async Task<int> CountAsync(PhoneHomeTestHost host, Func<AppDbContext, Task<int>> query)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await query(db);
    }

    private static async Task<long> SingleRevisionAsync(PhoneHomeTestHost host)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.RunnerRoutingSettings.AsNoTracking().SingleAsync()).Revision;
    }

    private static JsonElement Runner(JsonElement runners, string id) =>
        runners.EnumerateArray().Single(row => row.GetProperty("runnerId").GetString() == id);

    private static JsonElement Host(JsonElement root, string id) =>
        root.GetProperty("hosts").EnumerateArray().Single(row => row.GetProperty("hostId").GetString() == id);

    private static JsonElement Role(JsonElement occupancy, string role) =>
        occupancy.GetProperty("roles").EnumerateArray().Single(row => row.GetProperty("role").GetString() == role);

    private static void JsonEqual(JsonElement left, JsonElement right, IReadOnlySet<string> ignore)
    {
        left.ValueKind.ShouldBe(right.ValueKind);
        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                var leftProps = left.EnumerateObject().Where(property => !ignore.Contains(property.Name))
                    .OrderBy(property => property.Name, StringComparer.Ordinal).ToList();
                var rightProps = right.EnumerateObject().Where(property => !ignore.Contains(property.Name))
                    .OrderBy(property => property.Name, StringComparer.Ordinal).ToList();
                rightProps.Select(property => property.Name).ShouldBe(leftProps.Select(property => property.Name));
                for (var i = 0; i < leftProps.Count; i++)
                    JsonEqual(leftProps[i].Value, rightProps[i].Value, ignore);
                break;
            case JsonValueKind.Array:
                right.GetArrayLength().ShouldBe(left.GetArrayLength());
                for (var i = 0; i < left.GetArrayLength(); i++)
                    JsonEqual(left[i], right[i], ignore);
                break;
            default:
                left.GetRawText().ShouldBe(right.GetRawText());
                break;
        }
    }
}
