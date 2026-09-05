using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.ApiKeys;

/// <summary>
/// CARD-0106 S1 — the HTTP contract. The one property worth the cost of a real host here is the
/// NEGATIVE one: no route anywhere in this group hands a value back, so the JSON a client can see
/// is checked as JSON rather than as a DTO that might be projected differently by the serializer.
/// </summary>
[NotInParallel]
[ClassDataSource<ApiKeyApiWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public sealed class ApiKeyApiTests
{
    private const string Canary = "sk-canary-http-0106";

    private readonly ApiKeyApiWebAppFactory _factory;

    public ApiKeyApiTests(ApiKeyApiWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task a_global_key_round_trips_through_put_list_and_delete_without_a_value_in_sight()
    {
        using var client = _factory.CreateClient();
        var name = NewName();

        var put = await client.PutAsJsonAsync($"/api/api-keys/{name}", new { value = Canary });
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await ReadJsonAsync(put);
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("name").GetString().ShouldBe(name);
        created.GetProperty("projectId").ValueKind.ShouldBe(JsonValueKind.Null);
        created.GetRawText().ShouldNotContain(Canary);

        var list = await client.GetAsync("/api/api-keys");
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listBody = await list.Content.ReadAsStringAsync();
        listBody.ShouldContain(name);
        listBody.ShouldNotContain(Canary, Case.Sensitive,
            "there is no endpoint that returns a stored value, by design");

        var globals = await client.GetAsync("/api/api-keys/global");
        (await globals.Content.ReadAsStringAsync()).ShouldContain(name);

        var delete = await client.DeleteAsync($"/api/api-keys/{id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await (await client.GetAsync("/api/api-keys")).Content.ReadAsStringAsync())
            .ShouldNotContain(name);
    }

    [Test]
    public async Task a_project_scoped_key_is_written_through_the_project_route_and_listed_there()
    {
        using var client = _factory.CreateClient();
        var projectId = await AddProjectAsync();
        var name = NewName();

        var put = await client.PutAsJsonAsync(
            $"/api/projects/{projectId:D}/api-keys/{name}",
            new { value = Canary });
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(put)).GetProperty("projectId").GetGuid().ShouldBe(projectId);

        var list = await client.GetAsync($"/api/projects/{projectId:D}/api-keys");
        var body = await list.Content.ReadAsStringAsync();
        body.ShouldContain(name);
        body.ShouldNotContain(Canary);
    }

    [Test]
    public async Task the_same_name_at_both_scopes_is_two_keys_over_http()
    {
        using var client = _factory.CreateClient();
        var projectId = await AddProjectAsync();
        var name = NewName();

        var global = await client.PutAsJsonAsync($"/api/api-keys/{name}", new { value = "global" });
        var scoped = await client.PutAsJsonAsync(
            $"/api/projects/{projectId:D}/api-keys/{name}", new { value = "scoped" });

        (await ReadJsonAsync(global)).GetProperty("id").GetGuid()
            .ShouldNotBe((await ReadJsonAsync(scoped)).GetProperty("id").GetGuid());
    }

    [Test]
    public async Task a_name_a_placeholder_cannot_spell_is_422_and_an_unknown_project_is_404()
    {
        using var client = _factory.CreateClient();

        var badName = await client.PutAsJsonAsync("/api/api-keys/bad%20name", new { value = "v" });
        badName.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var unknownProject = await client.PutAsJsonAsync(
            $"/api/projects/{Guid.NewGuid():D}/api-keys/{NewName()}", new { value = "v" });
        unknownProject.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var unknownDelete = await client.DeleteAsync($"/api/api-keys/{Guid.NewGuid():D}");
        unknownDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task an_oversize_value_is_422_and_the_response_does_not_echo_it()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/api-keys/{NewName()}",
            new { value = new string('x', 4001) });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(new string('x', 100));
    }

    [Test]
    public async Task a_query_string_value_is_explicitly_rejected_and_never_written()
    {
        using var client = _factory.CreateClient();
        var name = NewName();
        var canary = "sk-canary-query-0114";

        var response = await client.PutAsJsonAsync(
            $"/api/api-keys/{name}?value={canary}", new { value = "body-value" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(canary);
        (await (await client.GetAsync("/api/api-keys")).Content.ReadAsStringAsync()).ShouldNotContain(name);
    }

    private static string NewName() => $"http-{Guid.NewGuid():N}";

    private async Task<Guid> AddProjectAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"ApiKey HTTP {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}

/// <summary>
/// Own schema (the API keys table must not be shared with every other service-level test) and a
/// reversible fake protector: the real DataProtection key ring is not ready under a test host, and
/// the crypto has its own tests in <see cref="ApiKeyProtectorTests"/>.
/// </summary>
public sealed class ApiKeyApiWebAppFactory : AntiphonWebAppFactory
{
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private IsolatedTestSchema? _isolatedSchema;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = IsolatedConnectionString,
                ["Serilog:ConsoleMinimumLevel"] = "Warning",
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
                IsolatedConnectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                }));
        });
    }

    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        services.RemoveAll<IApiKeyProtector>();
        services.AddSingleton<IApiKeyProtector, ApiKeyStoreTests.FakeApiKeyProtector>();
    }

    public override async Task ResetAsync()
    {
        await EnsureIsolatedSchemaAsync();
        await base.ResetAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_isolatedSchema is not null)
        {
            await _isolatedSchema.DisposeAsync();
            _isolatedSchema = null;
        }

        _schemaGate.Dispose();
    }

    private string IsolatedConnectionString => _isolatedSchema?.ConnectionString
        ?? throw new InvalidOperationException("The API test schema must be created before the host starts.");

    /// <summary>This factory owns its schema already; do not let the base create a second one.</summary>
    protected override string ConnectionString => IsolatedConnectionString;

    private async Task EnsureIsolatedSchemaAsync()
    {
        if (_isolatedSchema is not null)
            return;

        await _schemaGate.WaitAsync();
        try
        {
            _isolatedSchema ??= await TestDbFixture.CreateIsolatedSchemaAsync();
        }
        finally
        {
            _schemaGate.Release();
        }
    }
}
