using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0179 R3 — the build stamp is on <c>GET /api/version</c>, not /health. Only a deployed
/// build passes -p:SourceRevisionId=<HEAD>; a test build reports "unknown".</summary>
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
    public async Task Version_is_the_in_process_build_stamp()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/version");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AntiphonVersionDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        body.ShouldNotBeNull();
        body.Version.ShouldNotBeNullOrWhiteSpace();
        body.Version.ShouldMatch("^([0-9a-f]{40}|unknown)$");
        body.InformationalVersion.ShouldBe(AntiphonVersion.Informational);
        body.InformationalVersion.ShouldEndWith("+" + body.Version);

        // The in-process stamp agrees with the HTTP surface — the endpoint is not a second source.
        AntiphonVersion.Sha.ShouldBe(body.Version);
    }

    [Test]
    public async Task C495_VersionAdvertisesLandV2()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/version");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AntiphonVersionDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        body.ShouldNotBeNull();
        body.Version.ShouldNotBeNullOrWhiteSpace();
        body.Version.ShouldMatch("^([0-9a-f]{40}|unknown)$");
        body.InformationalVersion.ShouldBe(AntiphonVersion.Informational);
        body.InformationalVersion.ShouldEndWith("+" + body.Version);
        AntiphonVersion.Sha.ShouldBe(body.Version);
        body.Capabilities.ShouldNotBeNull();
        body.Capabilities.ShouldBe([AntiphonCapabilities.LandV2, AntiphonCapabilities.OperatorShutdownV1]);

        var health = await client.GetAsync("/health");
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await health.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Test]
    [Arguments("1.0.0+0123456789abcdef0123456789abcdef01234567", "0123456789abcdef0123456789abcdef01234567")]
    [Arguments("1.0.0+unknown", "unknown")]
    [Arguments("1.0.0", "unknown")]
    public void Deployed_stamp_parses_to_the_sha(string informational, string sha) =>
        AntiphonVersion.ResolveSha(informational).ShouldBe(sha);
}
