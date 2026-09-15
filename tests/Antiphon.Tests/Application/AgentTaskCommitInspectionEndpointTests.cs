using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class AgentTaskCommitEndpointTests
{
    [Test]
    [Arguments("sha", false)]
    [Arguments("paths", false)]
    [Arguments("sha", true)]
    [Arguments("paths", true)]
    public async Task Post_commit_inspection_failure_returns_recoverable_success_without_recommitting(string inspection, bool unborn)
    {
        using var repo = unborn ? new ScratchGitRepo("c527-ep-unborn") : await SeedRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var initialFailures = _factory.Spy.PostCommitInspectionFailures;
        _factory.Spy.FailPostCommitInspection(inspection);
        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "x.md" }, message = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("commit_inspection_pending");
        problem.GetProperty("committed").GetBoolean().ShouldBeTrue();
        var operation = problem.GetProperty("operationId").GetGuid();
        operation.ShouldNotBe(Guid.Empty);
        problem.TryGetProperty("files", out _).ShouldBeFalse();
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        if (inspection == "paths") problem.GetProperty("sha").GetString().ShouldBe(committed);
        else problem.GetProperty("sha").ValueKind.ShouldBe(JsonValueKind.Null);
        _factory.Spy.PostCommitInspectionFailures.ShouldBe(initialFailures + 1);
        (await repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain($"antiphon-operation: {operation:D}");
        _factory.Spy.OverrideRun = null;
        // HEAD can move before recovery: the receipt must still identify this operation.
        await repo.CommitFileAsync("later.md", "later");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "later edit");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var recovered = await client.GetAsync($"/api/agent-tasks/{task.Id}/commit/{operation}");
            recovered.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await recovered.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("sha").GetString().ShouldBe(committed);
            body.GetProperty("files").EnumerateArray().Select(f => f.GetString()).ShouldBe(new[] { "x.md" });
        }
        _factory.Spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        _factory.Spy.Verbs.ShouldNotContain("push");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "x.md"))).ShouldBe("later edit");
    }

    [Test]
    public async Task Commit_recovery_requires_the_same_authenticated_task_and_operation()
    {
        using var repo = await SeedRepoAsync();
        var ownerToken = "tok-" + Guid.NewGuid().ToString("N");
        var owner = await SeedTaskAsync(repo.Path, ownerToken, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        var otherToken = "tok-" + Guid.NewGuid().ToString("N");
        var other = await SeedTaskAsync(repo.Path, otherToken, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        var operation = Guid.NewGuid();
        await repo.GitAsync("commit", "--allow-empty", "-m", $"fixture\n\nantiphon: true\nantiphon-task: {owner.Id:D}\nantiphon-commit: gated\nantiphon-operation: {operation:D}");
        using var client = _factory.CreateClient();
        (await client.GetAsync($"/api/agent-tasks/{owner.Id}/commit/{operation}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", otherToken);
        (await client.GetAsync($"/api/agent-tasks/{owner.Id}/commit/{operation}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/agent-tasks/{other.Id}/commit/{operation}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/agent-tasks/{other.Id}/commit/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        _factory.Spy.Verbs.ShouldNotContain("commit");
        _factory.Spy.Verbs.ShouldNotContain("add");
    }
}
