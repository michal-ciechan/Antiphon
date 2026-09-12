using System.Diagnostics;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Slow")]
public sealed class DelegateScriptLandCompatibilityTests
{
    private static readonly string VersionSha = new('a', 40);
    private static readonly string ApprovalSha = new('b', 40);

    [Test]
    public async Task C495_OldServerTwoFieldVersionRefuses()
    {
        await using var stub = LandApiStub.Old(VersionSha);
        await AssertRefusedAsync(stub, LandArgs());
    }

    [Test]
    public async Task C495_MissingCapabilitiesRefuses()
    {
        await using var stub = LandApiStub.WithVersion(200, LandApiStub.TwoFieldVersion(VersionSha));
        await AssertRefusedAsync(stub, LandArgs());
    }

    [Test]
    public async Task C495_EmptyCapabilitiesRefuses()
    {
        await using var stub = LandApiStub.Compatible(VersionSha, capabilities: []);
        await AssertRefusedAsync(stub, LandArgs());
    }

    [Test]
    public async Task C495_WrongCapabilityRefuses()
    {
        await using var stub = LandApiStub.Compatible(VersionSha, capabilities: ["land-v3"]);
        await AssertRefusedAsync(stub, LandArgs());
    }

    [Test]
    public async Task C495_UppercaseCapabilityRefuses()
    {
        await using var stub = LandApiStub.Compatible(VersionSha, capabilities: ["LAND-V2"]);
        await AssertRefusedAsync(stub, LandArgs());
    }

    [Test]
    public async Task C495_UnknownShaRefuses()
    {
        await using var stub = LandApiStub.WithVersion(200,
            LandApiStub.CompatibleVersion("unknown", ["land-v2"], extraJsonFields: null));
        await AssertRefusedAsync(stub, LandArgs(), observed: "unknown");
    }

    [Test]
    public async Task C495_ShortShaRefuses()
    {
        var shortSha = new string('a', 39);
        await using var stub = LandApiStub.WithVersion(200,
            LandApiStub.CompatibleVersion(shortSha, ["land-v2"], extraJsonFields: null));
        await AssertRefusedAsync(stub, LandArgs(), observed: shortSha);
    }

    [Test]
    public async Task C495_MissingShaRefuses()
    {
        await using var stub = LandApiStub.WithVersion(200,
            """{"informationalVersion":"x","capabilities":["land-v2"]}""");
        await AssertRefusedAsync(stub, LandArgs(), observed: "unavailable");
    }

    [Test]
    public async Task C495_Version404Refuses()
    {
        await using var stub = LandApiStub.WithVersion(404, """{"title":"Not Found"}""");
        await AssertRefusedAsync(stub, LandArgs(), observed: "unavailable");
    }

    [Test]
    public async Task C495_Version401Refuses()
    {
        await using var stub = LandApiStub.WithVersion(401, """{"title":"Unauthorized"}""");
        await AssertRefusedAsync(stub, LandArgs(), observed: "unavailable");
    }

    [Test]
    public async Task C495_Version500Refuses()
    {
        await using var stub = LandApiStub.WithVersion(500, """{"title":"Error"}""");
        await AssertRefusedAsync(stub, LandArgs(), observed: "unavailable");
    }

    [Test]
    public async Task C495_VersionNonJsonRefuses()
    {
        await using var stub = new LandApiStub(new LandApiStubOptions
        {
            VersionBody = "not-json",
            VersionIsJson = false,
        });
        await AssertRefusedAsync(stub, LandArgs(), observed: "unavailable");
    }

    [Test]
    public async Task C495_VersionProbeTimeoutRefuses()
    {
        await using var stub = LandApiStub.WithVersion(200, LandApiStub.CompatibleVersion(VersionSha, ["land-v2"], null),
            delay: TimeSpan.FromSeconds(4));
        var sw = Stopwatch.StartNew();
        var result = await DelegateScriptRunner.RunAsync(
            stub.Url,
            new Dictionary<string, string?> { ["ANTIPHON_VERSION_PROBE_TIMEOUT_SEC"] = "1" },
            LandArgs());
        sw.Stop();
        result.ExitCode.ShouldBe(1, result.Output);
        stub.Requests.Count.ShouldBe(1);
        stub.Requests[0].Path.ShouldBe("/api/version");
        stub.LegacyLandPosts.ShouldBe(0);
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(4));
        result.Output.ShouldNotContain("Queued land");
    }

    [Test]
    public async Task C495_VersionProbeDefaultTimeoutIsFiveSeconds()
    {
        await using var stub = LandApiStub.WithVersion(200, LandApiStub.CompatibleVersion(VersionSha, ["land-v2"], null),
            delay: TimeSpan.FromSeconds(8));
        var sw = Stopwatch.StartNew();
        var result = await DelegateScriptRunner.RunAsync(stub.Url, LandArgs());
        sw.Stop();
        result.ExitCode.ShouldBe(1, result.Output);
        stub.LegacyLandPosts.ShouldBe(0);
        stub.Requests.ShouldNotContain(r => r.Method == "POST");
        sw.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5));
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(8));
    }

    [Test]
    public async Task C495_CompatibleServerPostsExactBodyToV2()
    {
        var id = Guid.NewGuid();
        var evidence = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await using var stub = LandApiStub.Compatible(
            VersionSha,
            v2Body: LandApiStub.DefaultQueuedBody(requestId, "queued", "tracked"));
        var result = await DelegateScriptRunner.RunAsync(stub.Url, "-Land", id.ToString(),
            "-ExpectedSourceSha", ApprovalSha, "-ReviewEvidenceId", evidence.ToString(),
            "-Verify", "/*/*/FreshnessProbeTests/ApprovedFixIsPresent");
        result.ExitCode.ShouldBe(0, result.Output);
        stub.Requests.Count.ShouldBe(2);
        stub.Requests[0].Method.ShouldBe("GET");
        stub.Requests[0].Path.ShouldBe("/api/version");
        stub.Requests[1].Method.ShouldBe("POST");
        stub.Requests[1].Path.ShouldEndWith("/land/v2");
        stub.LegacyLandPosts.ShouldBe(0);
        using var json = JsonDocument.Parse(stub.Requests[1].Body);
        json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ShouldBe(
            ["expectedSourceSha", "reviewEvidenceId", "verify"]);
        json.RootElement.GetProperty("expectedSourceSha").GetString().ShouldBe(ApprovalSha);
        json.RootElement.GetProperty("reviewEvidenceId").GetGuid().ShouldBe(evidence);
        json.RootElement.GetProperty("verify").GetString().ShouldBe("/*/*/FreshnessProbeTests/ApprovedFixIsPresent");
        result.Output.ShouldContain($"Queued land request {requestId}.");
        result.Output.ShouldContain("Publication pending");
    }

    [Test]
    public async Task C495_ExtraCapabilitiesAndFieldsStillCompatible()
    {
        var id = Guid.NewGuid();
        var sha64 = new string('c', 64);
        await using var stub = LandApiStub.Compatible(
            sha64,
            capabilities: ["x", "land-v2"],
            extraJsonFields: "\"extra\":true,\"build\":1");
        var result = await DelegateScriptRunner.RunAsync(stub.Url, "-Land", id.ToString(),
            "-ExpectedSourceSha", ApprovalSha);
        result.ExitCode.ShouldBe(0, result.Output);
        stub.Requests[1].Path.ShouldEndWith("/land/v2");
        stub.LegacyLandPosts.ShouldBe(0);
    }

    [Test]
    public async Task C495_BodylessResumeProbesThenPostsV2()
    {
        var id = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        await using var stub = LandApiStub.Compatible(
            VersionSha,
            v2Body: LandApiStub.DefaultQueuedBody(requestId, "requeued", "tracked"));
        var result = await DelegateScriptRunner.RunAsync(stub.Url, "-Land", id.ToString());
        result.ExitCode.ShouldBe(0, result.Output);
        stub.Requests.Count.ShouldBe(2);
        stub.Requests[0].Method.ShouldBe("GET");
        stub.Requests[0].Path.ShouldBe("/api/version");
        stub.Requests[1].Method.ShouldBe("POST");
        stub.Requests[1].Path.ShouldEndWith("/land/v2");
        stub.Requests[1].Body.ShouldBe("{}");
        result.Output.ShouldContain("Requeued land");
    }

    [Test]
    public async Task C495_ProcessSwapAfterProbeFailsWithoutLegacyPost()
    {
        await using var stub = LandApiStub.ProcessSwap(VersionSha, v2Status: 404);
        var result = await DelegateScriptRunner.RunAsync(stub.Url, LandArgs());
        result.ExitCode.ShouldBe(1, result.Output);
        stub.Requests.Count.ShouldBe(2);
        stub.Requests[0].Path.ShouldBe("/api/version");
        stub.Requests[1].Path.ShouldEndWith("/land/v2");
        stub.LegacyLandPosts.ShouldBe(0);
        result.Output.ShouldContain("land-v2");
        result.Output.ShouldContain("404");
        result.Output.ShouldContain("restart-apphost.ps1");
        result.Output.ShouldNotContain("Queued land");
    }

    [Test]
    public async Task C495_ProcessSwap405FailsWithoutLegacyPost()
    {
        await using var stub = LandApiStub.ProcessSwap(VersionSha, v2Status: 405);
        var result = await DelegateScriptRunner.RunAsync(stub.Url, LandArgs());
        result.ExitCode.ShouldBe(1, result.Output);
        stub.LegacyLandPosts.ShouldBe(0);
        result.Output.ShouldContain("405");
        result.Output.ShouldNotContain("Queued land");
    }

    [Test]
    public async Task C495_V2ServiceErrorKeepsDiagnostics()
    {
        await using var stub = new LandApiStub(new LandApiStubOptions
        {
            VersionBody = LandApiStub.CompatibleVersion(VersionSha, ["land-v2"], null),
            V2Status = 422,
            V2Body = """{"title":"Unprocessable Entity","detail":"expectedSourceSha is required for a fresh land request.","code":"expected_source_sha_required"}""",
        });
        var result = await DelegateScriptRunner.RunAsync(stub.Url, LandArgs());
        result.ExitCode.ShouldBe(1, result.Output);
        stub.LegacyLandPosts.ShouldBe(0);
        result.Output.ShouldContain("expectedSourceSha is required for a fresh land request.");
    }

    [Test]
    public async Task C495_TransportFailureAfterPostIsUncertain()
    {
        var id = Guid.NewGuid();
        await using var stub = new LandApiStub(new LandApiStubOptions
        {
            VersionBody = LandApiStub.CompatibleVersion(VersionSha, ["land-v2"], null),
            V2AbortAfterRead = true,
        });
        var result = await DelegateScriptRunner.RunAsync(stub.Url, "-Land", id.ToString(),
            "-ExpectedSourceSha", ApprovalSha);
        result.ExitCode.ShouldBe(1, result.Output);
        result.Output.ShouldContain($"-Status {id}");
        result.Output.ShouldNotContain("no land request was sent");
        result.Output.ShouldNotContain("zero requests");
        result.Output.ShouldNotContain("publication failed");
        result.Output.ShouldNotContain("failed publication");
    }

    [Test]
    public async Task C495_RefusalRedactsCredentials()
    {
        var nonce = "c495-token-" + Guid.NewGuid().ToString("N");
        await using var stub = LandApiStub.Old(VersionSha);
        var result = await DelegateScriptRunner.RunAsync(
            stub.Url,
            new Dictionary<string, string?> { ["ANTIPHON_TASK_TOKEN"] = nonce },
            LandArgs());
        result.ExitCode.ShouldBe(1, result.Output);
        result.Output.ShouldNotContain(nonce);
    }

    [Test]
    public async Task C495_StatusDoesNotProbeVersion()
    {
        var id = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            summary = new { status = "Succeeded", title = "owned", kind = "Worker", role = "Code", modelLevel = "High" },
        });
        await using var stub = LandApiStub.Compatible(VersionSha, taskStatusBody: body);
        var result = await DelegateScriptRunner.RunAsync(stub.Url, "-Status", id.ToString());
        result.ExitCode.ShouldBe(0, result.Output);
        stub.Requests.Count.ShouldBe(1);
        stub.Requests[0].Method.ShouldBe("GET");
        stub.Requests[0].Path.ShouldBe($"/api/agent-tasks/{id}");
        stub.Requests.ShouldNotContain(r => r.Path == "/api/version");
    }

    [Test]
    public async Task C495_ReplyDoesNotProbeVersion()
    {
        var id = Guid.NewGuid();
        await using var stub = LandApiStub.Compatible(VersionSha);
        var result = await DelegateScriptRunner.RunAsync(stub.Url, "-Reply", id.ToString(), "the answer");
        result.ExitCode.ShouldBe(0, result.Output);
        stub.Requests.Count.ShouldBe(1);
        stub.Requests[0].Method.ShouldBe("POST");
        stub.Requests[0].Path.ShouldEndWith("/reply");
        stub.Requests.ShouldNotContain(r => r.Path == "/api/version");
    }

    private static string[] LandArgs() =>
        ["-Land", Guid.NewGuid().ToString(), "-ExpectedSourceSha", ApprovalSha];

    private static async Task AssertRefusedAsync(LandApiStub stub, string[] args, string? observed = null)
    {
        var result = await DelegateScriptRunner.RunAsync(stub.Url, args);
        result.ExitCode.ShouldBe(1, result.Output);
        stub.Requests.Count.ShouldBe(1);
        stub.Requests[0].Method.ShouldBe("GET");
        stub.Requests[0].Path.ShouldBe("/api/version");
        stub.LegacyLandPosts.ShouldBe(0);
        stub.Requests.ShouldNotContain(r => r.Method == "POST");
        var baseUrl = stub.Url.TrimEnd('/');
        result.Output.ShouldContain(baseUrl);
        result.Output.ShouldContain("land-v2");
        result.Output.ShouldContain("/api/version");
        result.Output.ShouldContain("restart-apphost.ps1");
        result.Output.ShouldContain(observed ?? VersionSha);
        result.Output.ShouldNotContain("Queued land");
        result.Output.ShouldNotContain("Publication pending");
    }
}
