using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DelegateScriptAdoptTests
{
    [Test]
    public async Task C753_FromTaskResolvesSourceAndPostsReviewedRecoveryToV2()
    {
        var owner = Guid.NewGuid();
        var source = Guid.NewGuid();
        var review = Guid.NewGuid();
        var sha = new string('b', 40);
        await using var server = LandApiStub.Compatible(new string('a', 40),
            taskStatusBody: JsonSerializer.Serialize(new { summary = new { id = source } }));

        var result = await DelegateScriptRunner.RunAsync(server.Url, "-Land", owner.ToString(),
            "-FromTask", source.ToString()[..8], "-ExpectedSourceSha", sha,
            "-ReviewEvidenceId", review.ToString());

        result.ExitCode.ShouldBe(0, result.Output);
        server.Requests.Count.ShouldBe(3);
        server.Requests[0].Path.ShouldStartWith("/api/agent-tasks/");
        server.Requests[1].Path.ShouldBe("/api/version");
        server.Requests[2].Path.ShouldEndWith("/land/v2");
        using var json = JsonDocument.Parse(server.Requests[2].Body);
        json.RootElement.GetProperty("adoptFromTaskId").GetGuid().ShouldBe(source);
        json.RootElement.GetProperty("expectedSourceSha").GetString().ShouldBe(sha);
        json.RootElement.GetProperty("reviewEvidenceId").GetGuid().ShouldBe(review);
        server.LegacyLandPosts.ShouldBe(0);
    }

    [Test]
    public async Task C753_InvalidRecoveryArgsSendNoPost()
    {
        await using var server = LandApiStub.Compatible(new string('a', 40));
        var result = await DelegateScriptRunner.RunAsync(server.Url, "-Land", Guid.NewGuid().ToString(),
            "-RecoverReviewedSource", "-ExpectedSourceSha", "abcd", "-ReviewEvidenceId", Guid.NewGuid().ToString());
        result.ExitCode.ShouldBe(1);
        server.Requests.ShouldNotContain(r => r.Method == "POST");
    }

    [Test]
    public async Task C753_StatusPrintsAdoptedSourceAndReviewedSha()
    {
        var owner = Guid.NewGuid();
        var source = Guid.NewGuid();
        var sha = new string('c', 40);
        await using var server = LandApiStub.Compatible(new string('a', 40),
            taskStatusBody: JsonSerializer.Serialize(new
            {
                summary = new { id = owner, status = "Failed", title = "owner", kind = "Worker", role = "Code", modelLevel = "High" },
                landRequest = new { id = Guid.NewGuid(), state = "Completed", requestedAt = DateTime.UtcNow,
                    attempt = 1, noProgressSeconds = 0, expectedSourceSha = sha,
                    recoveryMode = "AdoptReviewedSource", recoveryOwnerStatus = "Failed",
                    recoverySourceTaskId = source, recoverySourceFullRef = "refs/heads/repair",
                    recoveryOwnerRemoteAfterSha = sha, notifications = Array.Empty<object>() },
                landing = new { publication = "Landed", operationId = Guid.NewGuid(), reviewedSha = sha,
                    verifiedSha = sha, remoteSha = sha, cleanup = "Complete", recoveryMode = "AdoptReviewedSource",
                    recoverySourceTaskId = source, recoverySourceFullRef = "refs/heads/repair",
                    recoveryOwnerRemoteBeforeSha = new string('b', 40), recoveryOwnerRemoteAfterSha = sha },
            }));

        var result = await DelegateScriptRunner.RunAsync(server.Url, "-Status", owner.ToString());

        result.ExitCode.ShouldBe(0, result.Output);
        result.Output.ShouldContain($"Recovery: AdoptReviewedSource; source task {source}");
        result.Output.ShouldContain($"reviewed {sha}");
        result.Output.ShouldContain("Recovery receipt: AdoptReviewedSource");
    }
}
