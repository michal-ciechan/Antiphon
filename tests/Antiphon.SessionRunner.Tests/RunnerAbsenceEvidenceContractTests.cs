using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1153 S3: the production HTTP route extension on a random-port in-process Kestrel host
/// (the <c>TerminalSeatReleaseTests.SeatWire</c> shape), real JSON contracts, a synthetic key,
/// the real evidence service over a temp root. Design: V-9, V-10. No LocalHttpRunner.exe.
/// </summary>
[Category("Integration")]
public class RunnerAbsenceEvidenceContractTests
{
    /// <summary>
    /// V-9. Prepare then certify returns the full documented shape with Cache-Control: no-store;
    /// GET /sessions/{id} and /sessions/{id}/transcript stay 404 for that prepared id; an
    /// arbitrary id answers 404 on certify with no certificate body.
    /// </summary>
    [Test]
    public async Task C1153_Real_unknown_transcript_has_a_separate_certificate()
    {
        await using var wire = await EvidenceWire.StartAsync(withKey: true);
        var id = Guid.NewGuid();

        var prepared = await wire.PostAsync(AbsenceEvidenceAuthentication.PrepareOperation, id);
        prepared.Status.ShouldBe(200);
        prepared.Authenticated.ShouldBeTrue();
        (await wire.GetAsync($"/sessions/{id:D}")).ShouldBe(HttpStatusCode.NotFound, "a prepared id is not a session");
        (await wire.GetAsync($"/sessions/{id:D}/transcript")).ShouldBe(HttpStatusCode.NotFound,
            "the ordinary transcript stays 404; it is never an empty transcript");

        var certified = await wire.PostAsync(AbsenceEvidenceAuthentication.CertifyOperation, id);

        certified.Status.ShouldBe(200);
        certified.CacheControl.ShouldBe("no-store");
        certified.Authenticated.ShouldBeTrue("the response MAC covers the certificate and the request nonce");
        var body = JsonNode.Parse(certified.Body)!.AsObject();
        body.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(RunnerAbsenceEvidence.CertificateMembers.OrderBy(k => k, StringComparer.Ordinal));
        body["version"]!.GetValue<int>().ShouldBe(1);
        body["outcome"]!.GetValue<string>().ShouldBe(RunnerAbsenceEvidence.NeverCreated);
        body["sessionId"]!.GetValue<string>().ShouldBe(id.ToString("D"));
        SessionGeneration.Equal(body["acceptedStartedAt"]!.GetValue<DateTime>(), RuntimeWorld.Generation).ShouldBeTrue();
        body["runnerStoreId"]!.GetValue<string>().ShouldBe(wire.World.StoreId.ToString("D"));
        Guid.Parse(body["runtimeEpoch"]!.GetValue<string>()).ShouldBe(wire.World.Runtime.AbsenceEvidence!.Epoch);
        body["requestNonce"]!.GetValue<string>().ShouldBe(certified.Nonce);
        body["complete"]!.GetValue<bool>().ShouldBeTrue();
        body["identityClosed"]!.GetValue<bool>().ShouldBeTrue();
        foreach (var presence in new[] { "creationObserved", "nativeTranscriptPresent", "sidecarTranscriptPresent", "processPresent" })
            body[presence]!.GetValue<bool>().ShouldBeFalse(presence);
        (await wire.GetAsync($"/sessions/{id:D}")).ShouldBe(HttpStatusCode.NotFound);
        (await wire.GetAsync($"/sessions/{id:D}/transcript")).ShouldBe(HttpStatusCode.NotFound);

        var arbitrary = Guid.NewGuid();
        var unknown = await wire.PostAsync(AbsenceEvidenceAuthentication.CertifyOperation, arbitrary);
        unknown.Status.ShouldBe(404);
        Encoding.UTF8.GetString(unknown.Body).ShouldContain(RunnerAbsenceRefusalCodes.NotPrepared);
        Encoding.UTF8.GetString(unknown.Body).ShouldNotContain(RunnerAbsenceEvidence.NeverCreated);
        File.Exists(wire.World.RecordPath(arbitrary)).ShouldBeFalse("certify of an arbitrary id writes nothing");
    }

    /// <summary>
    /// V-10. No accepted certificate and no state change for: no key configured, a wrong request
    /// MAC, a request field changed after signing (route id versus body id), and one altered
    /// response bit. Canary: no key bytes or key path in any response or captured log line.
    /// </summary>
    [Test]
    [Arguments("missing-key")]
    [Arguments("wrong-request-MAC")]
    [Arguments("changed-request-field")]
    [Arguments("altered-response-bit")]
    public async Task C1153_Http_authentication_covers_request_and_response(string condition)
    {
        await using var wire = await EvidenceWire.StartAsync(withKey: condition != "missing-key");
        // Positive control on the same host shape: a keyed host prepares and certifies.
        await using (var control = await EvidenceWire.StartAsync(withKey: true))
        {
            var controlId = Guid.NewGuid();
            (await control.PostAsync(AbsenceEvidenceAuthentication.PrepareOperation, controlId)).Status.ShouldBe(200);
            var ok = await control.PostAsync(AbsenceEvidenceAuthentication.CertifyOperation, controlId);
            ok.Status.ShouldBe(200);
            ok.Authenticated.ShouldBeTrue();
            control.AssertNoSecretLeak(ok);
        }

        var id = Guid.NewGuid();
        switch (condition)
        {
            case "missing-key":
            {
                var refused = await wire.PostAsync(AbsenceEvidenceAuthentication.PrepareOperation, id, signingKey: EvidenceWire.ForeignKey());
                refused.Status.ShouldBe(401);
                refused.CacheControl.ShouldBe("no-store");
                (await wire.Capabilities()).ShouldNotContain(RunnerAbsenceEvidence.Feature, "a key-less runner cannot advertise HTTP certification");
                File.Exists(wire.World.RecordPath(id)).ShouldBeFalse("no record without authentication");
                break;
            }
            case "wrong-request-MAC":
            {
                var refused = await wire.PostAsync(AbsenceEvidenceAuthentication.PrepareOperation, id, signingKey: EvidenceWire.ForeignKey(),
                    keyIdOverride: wire.Key!.KeyId);
                refused.Status.ShouldBe(401);
                File.Exists(wire.World.RecordPath(id)).ShouldBeFalse("no record for a wrong MAC");
                (await wire.PostAsync(AbsenceEvidenceAuthentication.PrepareOperation, id)).Status.ShouldBe(200);
                var bad = await wire.PostAsync(AbsenceEvidenceAuthentication.CertifyOperation, id, signingKey: EvidenceWire.ForeignKey(),
                    keyIdOverride: wire.Key.KeyId);
                bad.Status.ShouldBe(401);
                wire.World.ReadState(id).ShouldBe(RunnerAbsenceRecordState.Prepared, "an unauthenticated certify closes nothing");
                break;
            }
            case "changed-request-field":
            {
                // Signed for one store, sent with another: the body digest no longer matches.
                var tampered = await wire.PostAsync(AbsenceEvidenceAuthentication.PrepareOperation, id,
                    mutateBodyAfterSigning: body => body.Replace(wire.World.StoreId.ToString("D"), Guid.NewGuid().ToString("D")));
                tampered.Status.ShouldBe(401);
                File.Exists(wire.World.RecordPath(id)).ShouldBeFalse();
                // Route, body and signed id must be one value (A-1): correctly signed for the route, body names another id.
                var other = Guid.NewGuid();
                var disagreeing = await wire.PostAsync(AbsenceEvidenceAuthentication.PrepareOperation, id, bodySessionId: other);
                disagreeing.Status.ShouldBe(400);
                File.Exists(wire.World.RecordPath(id)).ShouldBeFalse();
                File.Exists(wire.World.RecordPath(other)).ShouldBeFalse();
                break;
            }
            case "altered-response-bit":
            {
                (await wire.PostAsync(AbsenceEvidenceAuthentication.PrepareOperation, id)).Status.ShouldBe(200);
                var certified = await wire.PostAsync(AbsenceEvidenceAuthentication.CertifyOperation, id);
                certified.Authenticated.ShouldBeTrue();
                var text = Encoding.UTF8.GetString(certified.Body);
                text.ShouldContain("\"processPresent\":false");
                var altered = Encoding.UTF8.GetBytes(text.Replace("\"processPresent\":false", "\"processPresent\":true"));
                AbsenceEvidenceAuthentication.Verify(wire.Key!, AbsenceEvidenceAuthentication.ResponseCanonical(
                    AbsenceEvidenceAuthentication.CertifyOperation, certified.Nonce, 200, altered), certified.Mac)
                    .ShouldBeFalse("one changed evidence bit invalidates the response MAC");
                AbsenceEvidenceAuthentication.Verify(wire.Key!, AbsenceEvidenceAuthentication.ResponseCanonical(
                    AbsenceEvidenceAuthentication.CertifyOperation, RunnerAbsenceEvidence.NewNonce(), 200, certified.Body), certified.Mac)
                    .ShouldBeFalse("the MAC binds the request nonce: a replay under another nonce fails");
                break;
            }
            default: throw new ArgumentOutOfRangeException(nameof(condition), condition, null);
        }

        wire.AssertNoSecretLeak(null);
    }
}

/// <summary>A random-port Kestrel host mapping the production routes over a real runtime.</summary>
internal sealed class EvidenceWire : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _http;
    private readonly List<string> _logs;
    private readonly List<string> _responses = new();
    private readonly string? _keyPath;
    private readonly string? _keyText;

    public RuntimeWorld World { get; }
    public AbsenceEvidenceKey? Key { get; }

    private EvidenceWire(WebApplication app, HttpClient http, List<string> logs, RuntimeWorld world,
        AbsenceEvidenceKey? key, string? keyPath, string? keyText)
    {
        _app = app;
        _http = http;
        _logs = logs;
        World = world;
        Key = key;
        _keyPath = keyPath;
        _keyText = keyText;
    }

    public static AbsenceEvidenceKey ForeignKey() =>
        AbsenceEvidenceKey.FromMaterial(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public static async Task<EvidenceWire> StartAsync(bool withKey)
    {
        var world = await RuntimeWorld.CreateAsync();
        string? keyPath = null;
        string? keyText = null;
        if (withKey)
        {
            keyPath = Path.Combine(world.Root, "secret-absence-key-" + Guid.NewGuid().ToString("N") + ".key");
            keyText = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            Directory.CreateDirectory(world.Root);
            File.WriteAllText(keyPath, keyText);
        }

        var logs = new List<string>();
        var settings = new SessionRunnerSettings { SessionLogPath = world.Root };
        settings.AbsenceEvidence.KeyPath = keyPath;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Test" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new ListLoggerProvider(logs));
        builder.Services.AddSingleton(world.Runtime);
        builder.Services.AddSingleton(Options.Create(settings));
        builder.Services.AddSingleton<AbsenceEvidenceKeyProvider>(sp => new AbsenceEvidenceKeyProvider(
            sp.GetRequiredService<IOptions<SessionRunnerSettings>>(), sp.GetRequiredService<ILogger<AbsenceEvidenceKeyProvider>>()));
        builder.Services.Configure<HerdrSettings>(_ => { });
        builder.Services.Configure<HostStatsSettings>(_ => { });
        var app = builder.Build();
        app.UseRunnerExceptionMapping();
        app.MapRunnerCapabilitiesRoute(new RunnerBuildDto("test", null, DateTime.UtcNow, DateTime.UtcNow));
        app.MapSessionGetRoute();
        app.MapSessionTranscriptRoute();
        app.MapAbsenceEvidenceRoutes();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        new Uri(address).Port.ShouldNotBe(17204);
        var key = app.Services.GetRequiredService<AbsenceEvidenceKeyProvider>().Key;
        (key is not null).ShouldBe(withKey);
        return new EvidenceWire(app, new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(20) },
            logs, world, key, keyPath, keyText);
    }

    public sealed record Answer(int Status, byte[] Body, string Nonce, string? Mac, string? CacheControl, bool Authenticated);

    public async Task<Answer> PostAsync(
        string operation, Guid routeId, AbsenceEvidenceKey? signingKey = null, string? keyIdOverride = null,
        Func<string, string>? mutateBodyAfterSigning = null, Guid? bodySessionId = null)
    {
        var request = new RunnerAbsenceRequest(RunnerAbsenceEvidence.Version, bodySessionId ?? routeId,
            RuntimeWorld.Generation, World.StoreId, RunnerAbsenceEvidence.NewNonce());
        var body = RunnerAbsenceEvidence.RequestBody(request);
        var signer = signingKey ?? Key ?? ForeignKey();
        var mac = AbsenceEvidenceAuthentication.Sign(signer, AbsenceEvidenceAuthentication.RequestCanonical(
            operation, routeId, request.AcceptedStartedAt, request.RunnerStoreId, request.RequestNonce, body));
        if (mutateBodyAfterSigning is not null)
            body = Encoding.UTF8.GetBytes(mutateBodyAfterSigning(Encoding.UTF8.GetString(body)));
        var path = operation == AbsenceEvidenceAuthentication.PrepareOperation
            ? $"/sessions/{routeId:D}/absence-evidence/prepare" : $"/sessions/{routeId:D}/absence-evidence";
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        message.Headers.Add(AbsenceEvidenceAuthentication.KeyIdHeader, keyIdOverride ?? signer.KeyId);
        message.Headers.Add(AbsenceEvidenceAuthentication.MacHeader, mac);
        using var response = await _http.SendAsync(message);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var responseMac = response.Headers.TryGetValues(AbsenceEvidenceAuthentication.MacHeader, out var macs) ? macs.Single() : null;
        _responses.Add(Encoding.UTF8.GetString(bytes));
        _responses.Add(string.Join(";", response.Headers.Select(h => h.Key + "=" + string.Join(",", h.Value))));
        var authenticated = Key is not null && AbsenceEvidenceAuthentication.Verify(Key,
            AbsenceEvidenceAuthentication.ResponseCanonical(operation, request.RequestNonce, (int)response.StatusCode, bytes), responseMac);
        return new((int)response.StatusCode, bytes, request.RequestNonce, responseMac,
            response.Headers.CacheControl?.ToString(), authenticated);
    }

    public async Task<HttpStatusCode> GetAsync(string path)
    {
        using var response = await _http.GetAsync(path);
        _responses.Add(await response.Content.ReadAsStringAsync());
        return response.StatusCode;
    }

    public async Task<IReadOnlyList<string>> Capabilities()
    {
        var text = await _http.GetStringAsync("/capabilities");
        _responses.Add(text);
        return JsonNode.Parse(text)!["features"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
    }

    /// <summary>V-10 canary: no key bytes, key text or key path in any response or log line.</summary>
    public void AssertNoSecretLeak(Answer? extra)
    {
        var haystack = new List<string>(_responses);
        lock (_logs) haystack.AddRange(_logs);
        if (extra is not null) haystack.Add(Encoding.UTF8.GetString(extra.Body));
        foreach (var line in haystack)
        {
            if (_keyText is not null) line.ShouldNotContain(_keyText);
            if (_keyPath is not null)
            {
                line.ShouldNotContain(_keyPath);
                line.ShouldNotContain(Path.GetFileName(_keyPath));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await World.DisposeAsync();
    }
}

internal sealed class ListLoggerProvider(List<string> sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ListLogger<object>(sink);

    public void Dispose()
    {
    }
}
