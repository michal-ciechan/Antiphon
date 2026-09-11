using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DelegateScriptLandApprovalTests
{
    [Test]
    public async Task C488_PostsExactApprovalJson()
    {
        var owner = Guid.NewGuid();
        var evidence = Guid.NewGuid();
        var sha = new string('b', 40);
        await using var server = new CapturingStub(JsonSerializer.Serialize(new { requestId = owner, status = "queued", notification = "tracked" }));
        var result = await DelegateScriptRunner.RunAsync(server.Url, "-Land", owner.ToString(),
            "-ExpectedSourceSha", sha, "-ReviewEvidenceId", evidence.ToString(), "-Verify", "/*/*/FreshnessProbeTests/ApprovedFixIsPresent");
        result.ExitCode.ShouldBe(0, result.Output);
        server.Posted.ShouldNotBeNull();
        using var json = JsonDocument.Parse(server.Posted!);
        json.RootElement.GetProperty("expectedSourceSha").GetString().ShouldBe(sha);
        json.RootElement.GetProperty("reviewEvidenceId").GetGuid().ShouldBe(evidence);
        json.RootElement.GetProperty("verify").GetString().ShouldBe("/*/*/FreshnessProbeTests/ApprovedFixIsPresent");
        result.Output.ShouldContain(owner.ToString("D"));
    }

    [Test]
    public async Task C488_CliPostsExactApproval() => await C488_PostsExactApprovalJson();

    [Test]
    public async Task C488_StatusShowsDistinctSourceFacts()
    {
        var id = Guid.NewGuid();
        var approved = new string('a', 40);
        var verified = new string('c', 40);
        var remote = new string('d', 40);
        var body = JsonSerializer.Serialize(new
        {
            summary = new { status = "Succeeded", title = "owned", kind = "Worker", role = "Code", modelLevel = "High" },
            landRequest = new
            {
                id, state = "Completed", requestedAt = "2026-09-11T00:00:00Z", attempt = 1, noProgressSeconds = 1,
                expectedSourceSha = approved, localBeforeSha = approved, remoteSourceSha = remote, resolvedSourceSha = approved,
                notifications = Array.Empty<object>(),
            },
            landing = new
            {
                publication = "Landed", operationId = id, reviewedSha = approved, verifiedSha = verified,
                remoteSha = remote, remoteConfirmedAt = "2026-09-11T00:10:00Z", cleanup = "Complete",
            },
        });
        await using var server = new CapturingStub(JsonSerializer.Serialize(new { requestId = id, status = "queued" }), body);
        var status = await DelegateScriptRunner.RunAsync(server.Url, "-Status", id.ToString());
        status.ExitCode.ShouldBe(0, status.Output);
        status.Output.ShouldContain("Approved original: " + approved);
        status.Output.ShouldContain("approved " + approved);
        status.Output.ShouldContain("verified " + verified);
        status.Output.ShouldNotContain("Approved original: (legacy");
    }

    [Test]
    public async Task C488_StatusApprovalIsNotVerifiedSha() => await C488_StatusShowsDistinctSourceFacts();

    private sealed class CapturingStub : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _pump;
        private readonly string _post;
        private readonly string _get;
        public string Url { get; }
        public string? Posted { get; private set; }
        public CapturingStub(string post, string? get = null)
        {
            _post = post;
            _get = get ?? "{}";
            Url = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await _listener.GetContextAsync(); }
                    catch (HttpListenerException) { break; }
                    catch (ObjectDisposedException) { break; }
                    var post = context.Request.HttpMethod == "POST";
                    if (post)
                    {
                        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                        Posted = await reader.ReadToEndAsync();
                    }
                    context.Response.StatusCode = post ? 202 : 200;
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(post ? _post : _get));
                    context.Response.Close();
                }
            });
        }
        public async ValueTask DisposeAsync() { _listener.Close(); await _pump; }
    }
}
