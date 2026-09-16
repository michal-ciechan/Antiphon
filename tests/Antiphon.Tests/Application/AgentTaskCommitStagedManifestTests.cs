using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class AgentTaskCommitEndpointTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Selected_revert_returns_exact_HTTP_receipt_and_recovery(bool recover)
    {
        using var repo = await SeedRepoAsync();
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "selected.md"), "selected");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "tracked.txt"), "staged change");
        await repo.GitAsync("add", "tracked.txt");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign staged");
        await repo.GitAsync("add", "foreign.md");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign work");
        _factory.Spy.BeforeRun = async args =>
        {
            if (args[0] == "add") await File.WriteAllTextAsync(Path.Combine(repo.Path, "tracked.txt"), "t\n");
        };
        if (recover) _factory.Spy.FailPostCommitInspection("paths");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        using var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "selected.md", "tracked.txt" }, message = "Selected revert" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        committed.ShouldNotBe(baseline);
        if (recover)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            body.GetProperty("code").GetString().ShouldBe("commit_inspection_pending");
            var operation = body.GetProperty("operationId").GetGuid();
            _factory.Spy.OverrideRun = null;
            // Recover from a different branch, with the original attempt only in the reflog.
            await repo.GitAsync("switch", "-c", "other", baseline);
            await repo.GitAsync("branch", "-D", "master");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var recovered = await client.GetAsync($"/api/agent-tasks/{task.Id}/commit/{operation}");
                recovered.StatusCode.ShouldBe(HttpStatusCode.OK);
                body = await recovered.Content.ReadFromJsonAsync<JsonElement>();
                body.GetProperty("sha").GetString().ShouldBe(committed);
                body.GetProperty("files").EnumerateArray().Select(p => p.GetString()).ShouldBe(new[] { "selected.md" });
            }
            (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(baseline);
        }
        else response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("sha").GetString().ShouldBe(committed);
        body.GetProperty("files").EnumerateArray().Select(p => p.GetString()).ShouldBe(new[] { "selected.md" });
        (await repo.GitReadAsync("show", $"{committed}:tracked.txt")).ShouldBe("t\n");
        (await repo.GitReadAsync("show", ":foreign.md")).ShouldBe("foreign staged");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "foreign.md"))).ShouldBe("foreign work");
        _factory.Spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        _factory.Spy.Verbs.ShouldNotContain("push");
    }
}
