using System.Net;
using System.Net.Http.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0179 R1 — HTTP surface of <c>POST /api/diagnostics/bundle</c>.</summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class DiagnosticsBundleEndpointTests
{
    private readonly AntiphonWebAppFactory _factory;

    public DiagnosticsBundleEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task Oversized_screenshot_is_400()
    {
        using var client = _factory.CreateClient();
        var oversized = Convert.ToBase64String(new byte[8 * 1024 * 1024 + 1]);
        var response = await client.PostAsJsonAsync("/api/diagnostics/bundle", new BugReportRequest(
            ScreenshotPngBase64: oversized));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Screenshot exceeds");
    }

    [Test]
    public async Task Bundle_returns_zip_with_filename()
    {
        using var client = _factory.CreateClient();
        const string png =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/diagnostics/bundle")
        {
            Content = JsonContent.Create(new BugReportRequest(
                Route: "/",
                ScreenshotPngBase64: png,
                Note: "endpoint"))
        };
        request.Headers.Add("X-Antiphon-Client-Sha", "clientsha");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/zip");
        var disposition = response.Content.Headers.ContentDisposition;
        disposition.ShouldNotBeNull();
        disposition.FileName.ShouldNotBeNull();
        disposition.FileName.ShouldStartWith("antiphon-bug-");
        disposition.FileName.ShouldEndWith(".zip");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(4);
        bytes[0].ShouldBe((byte)'P');
        bytes[1].ShouldBe((byte)'K');
    }
}
