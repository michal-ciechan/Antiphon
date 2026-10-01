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
        await using var server = LandApiStub.Compatible(
            new string('a', 40),
            v2Body: JsonSerializer.Serialize(new { requestId = owner, status = "queued", notification = "tracked" }));
        var result = await DelegateScriptRunner.RunAsync(server.Url, "-Land", owner.ToString(),
            "-ExpectedSourceSha", sha, "-ReviewEvidenceId", evidence.ToString(), "-Verify", "/*/*/FreshnessProbeTests/ApprovedFixIsPresent");
        result.ExitCode.ShouldBe(0, result.Output);
        server.LegacyLandPosts.ShouldBe(0);
        server.Requests.Count.ShouldBe(2);
        server.Requests[1].Path.ShouldEndWith("/land/v2");
        using var json = JsonDocument.Parse(server.Requests[1].Body);
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
        await using var server = LandApiStub.Compatible(
            new string('e', 40),
            v2Body: JsonSerializer.Serialize(new { requestId = id, status = "queued" }),
            taskStatusBody: body);
        var status = await DelegateScriptRunner.RunAsync(server.Url, "-Status", id.ToString());
        status.ExitCode.ShouldBe(0, status.Output);
        status.Output.ShouldContain("Approved original: " + approved);
        status.Output.ShouldContain("approved " + approved);
        status.Output.ShouldContain("verified " + verified);
        status.Output.ShouldNotContain("Approved original: (legacy");
    }

    [Test]
    public async Task C488_StatusApprovalIsNotVerifiedSha() => await C488_StatusShowsDistinctSourceFacts();

    [Test]
    [Category("C835")]
    public async Task C835_FindingSourceCleanIsExplicit()
    {
        var task = Guid.NewGuid();
        var sha = new string('a', 40);
        await using var server = LandApiStub.Compatible(sha,
            taskStatusBody: JsonSerializer.Serialize(new
            {
                summary = new { status = "Succeeded", title = "review", kind = "Worker", role = "Review", modelLevel = "High" },
                reviewEvidence = new { id = Guid.NewGuid(), subjectTaskId = task, reviewedSourceSha = sha,
                    reviewedSourceClean = (bool?)false, reviewedSourceRef = "refs/heads/review", outcome = "Clean" },
            }));

        foreach (var (label, option, expected) in new[]
        {
            ("omitted", (string?)null, (bool?)null),
            ("false", "False", (bool?)false),
            ("true", "True", (bool?)true),
        })
        {
            var args = new List<string> { "-Finding", task.ToString("D"), "-Stage", "Review", "-Clean",
                "-ReviewedSourceSha", sha };
            if (option is not null) args.Add("-ReviewedSourceClean:$" + option.ToLowerInvariant());
            var result = await DelegateScriptRunner.RunAsync(server.Url, args.ToArray());
            result.ExitCode.ShouldBe(0, label + ": " + result.Output);
            var call = server.Requests.Last(request => request.Path.EndsWith("/finding", StringComparison.Ordinal));
            using var body = JsonDocument.Parse(call.Body);
            body.RootElement.GetProperty("reviewedSourceSha").GetString().ShouldBe(sha, label);
            var present = body.RootElement.TryGetProperty("reviewedSourceClean", out var clean);
            present.ShouldBe(expected.HasValue, "finding-json-preserves-explicitness " + label);
            if (expected.HasValue) clean.GetBoolean().ShouldBe(expected.Value, label + "-value");
        }

        var status = await DelegateScriptRunner.RunAsync(server.Url, "-Status", task.ToString("D"));
        status.ExitCode.ShouldBe(0, status.Output);
        status.Output.ShouldContain("clean False", Case.Sensitive, "status-false-visible");
        server.LegacyLandPosts.ShouldBe(0, "no-fallback-land-post");
        server.Requests.Any(request => request.Path.EndsWith("/land/v2", StringComparison.Ordinal))
            .ShouldBeFalse("no-fallback-land-post");
    }
}
