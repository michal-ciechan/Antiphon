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
    [Arguments("allowed")]
    [Arguments("ignored-selected")]
    [Arguments("ignored-neighbor")]
    public async Task Commit_explicit_bracketed_filename_preserves_matching_neighbor(string variant)
    {
        using var repo = await SeedRepoAsync();
        const string selected = "item[ab].md";
        const string neighbor = "itema.md";
        await repo.CommitFileAsync(selected, "selected base");
        await repo.CommitFileAsync(neighbor, "neighbor base");
        var rules = variant switch
        {
            "ignored-selected" => "*.secret\nitem\\[ab\\].md\n",
            "ignored-neighbor" => "*.secret\n*.md\n!item\\[ab\\].md\n",
            _ => "*.secret\n",
        };
        if (variant != "allowed") await repo.CommitFileAsync(".gitignore", rules);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, selected), "selected work");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, neighbor), "neighbor staged");
        await repo.GitAsync("add", "-f", "--", neighbor);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, neighbor), "neighbor work");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var index = await File.ReadAllBytesAsync(Path.Combine(repo.Path, ".git", "index"));
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        using var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { selected }, message = "Explicit filename" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (variant == "ignored-selected")
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            body.GetProperty("code").GetString().ShouldBe("ignored_path_staged");
            body.GetProperty("refusals")[0].GetProperty("path").GetString().ShouldBe(selected);
            _factory.Spy.Verbs.ShouldNotContain("add");
            (await File.ReadAllBytesAsync(Path.Combine(repo.Path, ".git", "index"))).ShouldBe(index);
            (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            body.GetProperty("files").EnumerateArray().Select(p => p.GetString()).ShouldBe(new[] { selected });
            body.GetProperty("sha").GetString().ShouldBe((await repo.GitReadAsync("rev-parse", "HEAD")).Trim());
            (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Trim().ShouldBe(selected);
        }
        (await repo.GitReadAsync("show", $"HEAD:{neighbor}")).ShouldBe("neighbor base");
        (await repo.GitReadAsync("show", $":{neighbor}")).ShouldBe("neighbor staged");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, neighbor))).ShouldBe("neighbor work");
        _factory.Spy.Verbs.ShouldNotContain("push");
    }
}
