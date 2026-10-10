using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

/// <summary>CARD-0822. GET serves the last good body. POST refresh is operator-token gated.</summary>
[Category("Integration")]
[NotInParallel("OrchestratorInstructionsEndpoints")]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class OrchestratorInstructionsEndpointTests
{
    private readonly AntiphonWebAppFactory _factory;

    public OrchestratorInstructionsEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task Get_returns_the_body_with_etag_and_version_header()
    {
        await _factory.Services.GetRequiredService<OrchestratorInstructionsService>()
            .ReconcileNowAsync("startup");
        await using var scope = _factory.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OrchestratorInstructionsStates
            .AsNoTracking()
            .SingleAsync(state => state.Id == OrchestratorInstructionsState.FleetId);
        var text = OrchestratorInstructionsService.FileText(row.Version, row.Revision, row.Body);

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/orchestrator-instructions");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag?.Tag.Trim('"').ShouldBe(row.Version);
        response.Headers.GetValues("X-Antiphon-Instructions-Version").Single().ShouldBe(row.Version);
        (await response.Content.ReadAsStringAsync()).ShouldBe(text);
    }

    [Test]
    public async Task Post_refresh_requires_the_operator_token_and_returns_the_version()
    {
        using var client = _factory.CreateClient();
        using var denied = await client.PostAsync("/api/orchestrator-instructions/refresh", null);
        ((int)denied.StatusCode).ShouldBe(403);
        (await denied.Content.ReadAsStringAsync()).ShouldContain("operator_token_required");

        var token = OperatorTokenFile.ReadOrCreate(_factory.OperatorTokenPath);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orchestrator-instructions/refresh");
        request.Headers.TryAddWithoutValidation(OperatorTokenFile.Header, token);
        using var allowed = await client.SendAsync(request);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await allowed.Content.ReadFromJsonAsync<JsonElement>();
        await using var scope = _factory.Services.CreateAsyncScope();
        var version = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OrchestratorInstructionsStates
            .AsNoTracking()
            .Where(state => state.Id == OrchestratorInstructionsState.FleetId)
            .Select(state => state.Version)
            .SingleAsync();
        body.GetProperty("version").GetString().ShouldBe(version);
    }
}
