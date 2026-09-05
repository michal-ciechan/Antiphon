using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0179 R3 — the build-time git SHA is on <c>GET /api/version</c>, not /health.</summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class HealthEndpointTests
{
    private readonly AntiphonWebAppFactory _factory;

    public HealthEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task Version_is_present_and_not_unknown_in_a_git_checkout()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/version");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AntiphonVersionDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        body.ShouldNotBeNull();
        body.Version.ShouldNotBeNullOrWhiteSpace();
        body.Version.ShouldNotBe("unknown");
        body.Version.ShouldMatch("^[0-9a-f]{40}$");
        body.InformationalVersion.ShouldContain(body.Version);

        // The in-process stamp agrees with the HTTP surface — the endpoint is not a second source.
        AntiphonVersion.Sha.ShouldBe(body.Version);
    }
}
