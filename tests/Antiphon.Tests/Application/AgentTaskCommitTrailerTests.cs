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
    public async Task Equals_separator_returns_exact_HTTP_receipt_and_recovery(bool recover)
    {
        using var repo = await SeedRepoAsync();
        await repo.GitAsync("config", "trailer.separators", "=");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "selected.md"), "selected");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign staged");
        await repo.GitAsync("add", "foreign.md");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign work");
        if (recover) _factory.Spy.FailPostCommitInspection("paths");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        using var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "selected.md" }, message = "Equals separator" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var operation = Guid.Parse((await repo.GitReadAsync("log", "-1",
            "--format=%(trailers:key=antiphon-operation,valueonly)")).Trim());
        if (recover)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            body.GetProperty("code").GetString().ShouldBe("commit_inspection_pending");
            body.GetProperty("operationId").GetGuid().ShouldBe(operation);
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            body.GetProperty("sha").GetString().ShouldBe(committed);
            body.GetProperty("files").EnumerateArray().Select(p => p.GetString()).ShouldBe(new[] { "selected.md" });
        }
        _factory.Spy.OverrideRun = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var recovered = await client.GetAsync($"/api/agent-tasks/{task.Id}/commit/{operation}");
            recovered.StatusCode.ShouldBe(HttpStatusCode.OK);
            body = await recovered.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("sha").GetString().ShouldBe(committed);
            body.GetProperty("files").EnumerateArray().Select(p => p.GetString()).ShouldBe(new[] { "selected.md" });
        }
        using var absent = await client.GetAsync($"/api/agent-tasks/{task.Id}/commit/{Guid.NewGuid()}");
        absent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        _factory.Spy.OverrideRun = args => args.Contains("--all") ? (128, "", "history unavailable") : null;
        using var unavailable = await client.GetAsync($"/api/agent-tasks/{task.Id}/commit/{operation}");
        unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(committed);
        (await repo.GitReadAsync("show", ":foreign.md")).ShouldBe("foreign staged");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "foreign.md"))).ShouldBe("foreign work");
        _factory.Spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        _factory.Spy.Verbs.ShouldNotContain("push");
    }
}
