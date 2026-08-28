using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0217 S2 — API JSON is compressed when requested, while the SignalR route stays safe for
/// long-lived streaming transports.
/// </summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ResponseCompressionIntegrationTests
{
    private readonly AntiphonWebAppFactory _factory;

    public ResponseCompressionIntegrationTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task Boards_json_is_brotli_compressed_and_round_trips_byte_for_byte()
    {
        using var client = _factory.CreateClient();

        var baselineResponse = await client.GetAsync("/api/boards");
        baselineResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var baseline = await baselineResponse.Content.ReadAsByteArrayAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/boards");
        request.Headers.AcceptEncoding.ParseAdd("br");
        var compressedResponse = await client.SendAsync(request);

        compressedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        compressedResponse.Content.Headers.ContentEncoding.ShouldContain("br");
        var compressed = await compressedResponse.Content.ReadAsByteArrayAsync();
        var roundTripped = await DecompressBrotliAsync(compressed);

        roundTripped.ShouldBe(baseline);
        using var document = JsonDocument.Parse(roundTripped);
        document.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Test]
    public async Task Signalr_negotiate_is_not_compressed_even_when_brotli_is_requested()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/hubs/antiphon/negotiate?negotiateVersion=1");
        request.Headers.AcceptEncoding.ParseAdd("br");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentEncoding.ShouldBeEmpty();
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        document.RootElement.GetProperty("connectionId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    private static async Task<byte[]> DecompressBrotliAsync(byte[] compressed)
    {
        await using var input = new MemoryStream(compressed);
        await using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        await using var output = new MemoryStream();
        await brotli.CopyToAsync(output);
        return output.ToArray();
    }
}
