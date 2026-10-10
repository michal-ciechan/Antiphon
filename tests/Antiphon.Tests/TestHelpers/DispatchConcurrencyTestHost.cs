using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0505 fixtures. F-S is the fixed clock and the bound DelegationSettings seed.
/// F-H is a loopback host using the production exception middleware.
/// </summary>
public static class DispatchConcurrencyTestHost
{
    public static readonly DateTimeOffset SeedInstant = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    public static DelegationSettings BoundSettings()
    {
        var settings = new DelegationSettings
        {
            MaxOpenTasks = 9,
            MaxConcurrentTasks = 2,
        };
        settings.RolePolicy["Code"].RecommendedInFlight = 5;
        settings.RolePolicy["Review"].RecommendedInFlight = 4;
        settings.RolePolicy["Plan"].RecommendedInFlight = 3;
        return settings;
    }

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static PutDispatchConcurrencyRequest Put(
        long expectedRevision, string overrides, string reason, string provenance = "Human", long? expectedGlobalRevision = null) =>
        new(expectedRevision, expectedGlobalRevision, Json(overrides), reason, provenance);
}

public sealed class DispatchConcurrencyShop : IAsyncDisposable
{
    private readonly IsolatedTestSchema _schema;

    public DispatchConcurrencyShop(IsolatedTestSchema schema, DelegationSettings settings, FakeTimeProvider clock, MockEventBus bus)
    {
        _schema = schema;
        Settings = settings;
        Clock = clock;
        Bus = bus;
    }

    public DelegationSettings Settings { get; }
    public FakeTimeProvider Clock { get; }
    public MockEventBus Bus { get; }
    public Guid ProjectP { get; private set; }
    public Guid ProjectQ { get; private set; }
    public string ConnectionString => _schema.ConnectionString;

    public static async Task<DispatchConcurrencyShop> Open(DelegationSettings? settings = null)
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var shop = new DispatchConcurrencyShop(
            schema, settings ?? DispatchConcurrencyTestHost.BoundSettings(),
            new FakeTimeProvider(DispatchConcurrencyTestHost.SeedInstant), new MockEventBus());
        await shop.SeedProjectsAsync();
        return shop;
    }

    public AppDbContext Db(params IInterceptor[] interceptors) =>
        new(TestDbFixture.CreateDbContextOptions(ConnectionString, interceptors));

    public DispatchConcurrencySettingsService Service(
        AppDbContext db, DelegationSettings? settings = null, IEventBus? bus = null) =>
        new(db, Options.Create(settings ?? Settings), Clock, bus ?? Bus);

    public async Task<DispatchConcurrencyGlobalDto> ReadGlobalAsync()
    {
        await using var db = Db();
        return await Service(db).GetGlobalAsync(CancellationToken.None);
    }

    public async Task<DispatchConcurrencyProjectDto> ReadProjectAsync(Guid projectId)
    {
        await using var db = Db();
        return await Service(db).GetProjectAsync(projectId, CancellationToken.None);
    }

    public async Task<DispatchConcurrencyGlobalDto> PutGlobalAsync(long revision, string overrides, string reason, string provenance = "Human")
    {
        await using var db = Db();
        return await Service(db).PutGlobalAsync(
            DispatchConcurrencyTestHost.Put(revision, overrides, reason, provenance), null, CancellationToken.None);
    }

    public async Task<DispatchConcurrencyProjectDto> PutProjectAsync(
        Guid projectId, long revision, long globalRevision, string overrides, string reason, string provenance = "Human", Guid? caller = null)
    {
        await using var db = Db();
        return await Service(db).PutProjectAsync(
            projectId,
            DispatchConcurrencyTestHost.Put(revision, overrides, reason, provenance, globalRevision),
            caller,
            CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _schema.DisposeAsync();

    private async Task SeedProjectsAsync()
    {
        var now = Clock.GetUtcNow().UtcDateTime;
        ProjectP = Guid.NewGuid();
        ProjectQ = Guid.NewGuid();
        await using var db = Db();
        db.Projects.AddRange(
            Project(ProjectP, "P", now),
            Project(ProjectQ, "Q", now));
        db.HostBudgets.Add(new HostBudget
        {
            HostId = "desktop",
            MaxInFlight = 0,
            Reason = "frozen",
            UpdatedAt = now,
            Revision = 3,
        });
        db.RunnerRoutingSettings.Add(new RunnerRoutingSettings
        {
            Id = RunnerRoutingSettings.SingletonKey,
            Revision = 4,
            GlobalRunnerId = "server2",
            UpdatedAt = now,
            LastReason = "frozen",
            LastProvenance = "Human",
        });
        await db.SaveChangesAsync();
    }

    private static Project Project(Guid id, string name, DateTime now) => new()
    {
        Id = id,
        Name = name,
        GitRepositoryUrl = $"https://example.test/{name}.git",
        CreatedAt = now,
        UpdatedAt = now,
    };
}

public sealed class DispatchConcurrencyWireHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private DispatchConcurrencyWireHost(WebApplication app, HttpClient client, DispatchConcurrencyShop shop)
    {
        _app = app;
        Client = client;
        Shop = shop;
    }

    public HttpClient Client { get; }
    public DispatchConcurrencyShop Shop { get; }

    public static async Task<DispatchConcurrencyWireHost> Start(
        DispatchConcurrencyShop shop, params IInterceptor[] interceptors)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(TestDbFixture.CreateDbContextOptions(shop.ConnectionString, interceptors));
        builder.Services.AddScoped(sp => new AppDbContext(sp.GetRequiredService<DbContextOptions<AppDbContext>>()));
        builder.Services.AddSingleton<TimeProvider>(shop.Clock);
        builder.Services.AddSingleton<IEventBus>(shop.Bus);
        builder.Services.AddSingleton(Options.Create(shop.Settings));
        builder.Services.AddScoped<DispatchConcurrencySettingsService>();
        builder.Services.AddScoped(sp => new AgentTaskService(
            sp.GetRequiredService<AppDbContext>(),
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            sp.GetRequiredService<IOptions<DelegationSettings>>(),
            sp.GetRequiredService<IEventBus>(),
            new RecordingSessionStopper(),
            sp.GetRequiredService<TimeProvider>(),
            NullLogger<AgentTaskService>.Instance));
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        });
        var app = builder.Build();
        app.UseMiddleware<ExceptionMiddleware>();
        app.MapDispatchConcurrencyEndpoints();
        await app.StartAsync();
        var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        return new DispatchConcurrencyWireHost(app, client, shop);
    }

    public Task<HttpResponseMessage> GetAsync(string path) => Client.GetAsync(path);

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json = null, string? token = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (token is not null)
            request.Headers.TryAddWithoutValidation(AgentTaskEndpoints.TokenHeader, token);
        return Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>Pauses the first settings save so another session can be observed on the advisory lock.</summary>
public sealed class PauseSettingsSaveInterceptor : SaveChangesInterceptor
{
    private int _paused;

    public TaskCompletionSource AtSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var settings = eventData.Context?.ChangeTracker.Entries<DispatchConcurrencySettings>()
            .Any(entry => entry.State is EntityState.Added or EntityState.Modified) == true;
        if (settings && Interlocked.Exchange(ref _paused, 1) == 0)
        {
            AtSave.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>Throws once from the commit interception, after SaveChanges has sent its inserts.</summary>
public sealed class FailFirstCommitInterceptor : DbTransactionInterceptor
{
    private int _commits;

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        System.Data.Common.DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _commits) == 1)
            throw new InvalidOperationException("interrupted before import commit");
        return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
    }
}

public static class DispatchConcurrencyLockProbe
{
    public static async Task<bool> WaitForUngrantedAdvisoryAsync(string connectionString, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT COUNT(*)
                FROM pg_locks lock
                JOIN pg_stat_activity activity ON activity.pid = lock.pid
                WHERE lock.locktype = 'advisory'
                  AND NOT lock.granted
                  AND activity.query LIKE '%pg_advisory_xact_lock%'
                """,
                connection);
            var waiting = (long)(await command.ExecuteScalarAsync() ?? 0L);
            if (waiting > 0)
                return true;
            await Task.Delay(25);
        }

        return false;
    }
}
