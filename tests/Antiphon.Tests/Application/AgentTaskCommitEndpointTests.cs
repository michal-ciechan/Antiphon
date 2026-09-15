using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[NotInParallel]
[ClassDataSource<CommitEndpointWebAppFactory>(Shared = SharedType.PerClass)]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public sealed partial class AgentTaskCommitEndpointTests
{
    private readonly CommitEndpointWebAppFactory _factory;

    public AgentTaskCommitEndpointTests(CommitEndpointWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync()
    {
        _factory.Spy.Verbs.Clear();
        _factory.Spy.OverrideRun = null;
        _factory.Spy.BeforeRun = null;
        return _factory.ResetAsync();
    }

    [Test]
    public async Task Commit_with_the_task_token_returns_200_and_the_sha()
    {
        using var repo = await SeedRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign staged");
        await repo.GitAsync("add", "foreign.md");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign unstaged");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);

        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "x.md" }, message = "task 1234abcd: x" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sha = body.GetProperty("sha").GetString();
        sha.ShouldBe((await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "HEAD")).StdOut.Trim());
        body.GetProperty("files")[0].GetString().ShouldBe("x.md");
        body.GetProperty("files").GetArrayLength().ShouldBe(1);
        (await repo.GitReadAsync("show", ":foreign.md")).ShouldBe("foreign staged");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "foreign.md"))).ShouldBe("foreign unstaged");
        (await repo.GitReadAsync("ls-tree", "--name-only", "HEAD")).ShouldNotContain("foreign.md");
        (await ScratchGitRepo.GitInAsync(repo.Path, "log", "-1", "--format=%B")).StdOut.ShouldContain("antiphon-commit: gated");
        _factory.Spy.Verbs.ShouldNotContain("push");
    }

    [Test]
    [Arguments("omitted")]
    [Arguments("null")]
    [Arguments("empty")]
    [Arguments("null-entry")]
    [Arguments("blank-entry")]
    [Arguments("empty-entry")]
    [Arguments("parent")]
    [Arguments("rooted")]
    [Arguments("pathspec")]
    [Arguments("directory")]
    [Arguments("nul")]
    public async Task Commit_requires_explicit_paths_before_lease_or_mutation(string selection)
    {
        using var repo = await SeedRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "assigned.md"), "assigned");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign staged");
        await repo.GitAsync("add", "foreign.md");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign unstaged");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var index = await File.ReadAllBytesAsync(Path.Combine(repo.Path, ".git", "index"));
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var body = new Dictionary<string, object?> { ["message"] = "selected commit" };
        if (selection != "omitted")
            body["paths"] = selection switch
            {
                "null" => null,
                "empty" => Array.Empty<string>(),
                "null-entry" => new string?[] { "assigned.md", null },
                "blank-entry" => new[] { "assigned.md", " " },
                "empty-entry" => new[] { "assigned.md", "" },
                "parent" => new[] { "assigned.md", "../foreign.md" },
                "rooted" => new[] { "assigned.md", Path.Combine(repo.Path, "foreign.md") },
                "pathspec" => new[] { "assigned.md", ":(glob)*" },
                "directory" => new[] { "assigned.md", "." },
                "nul" => new[] { "assigned.md", "bad\0path" },
                _ => throw new ArgumentOutOfRangeException(nameof(selection)),
            };
        var leases = _factory.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.IRepositoryMutationLease>();
        // Invalid input must remain a validation error even when mutation admission is busy.
        await using (var held = await leases.TryAcquireAsync(repo.Path, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            await AssertRefusalAsync();
        }
        await AssertRefusalAsync();
        _factory.Spy.Verbs.ShouldBeEmpty();
        (await File.ReadAllBytesAsync(Path.Combine(repo.Path, ".git", "index"))).ShouldBe(index);
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "assigned.md"))).ShouldBe("assigned");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "foreign.md"))).ShouldBe("foreign unstaged");
        (await repo.GitReadAsync("ls-tree", "--name-only", "HEAD")).ShouldNotContain("foreign.md");

        async Task AssertRefusalAsync()
        {
            using var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit", body);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }
    }

    [Test]
    public async Task Commit_of_a_force_added_ignored_path_is_409_ignored_path_staged()
    {
        using var repo = await SeedRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.secret"), "s");
        await ScratchGitRepo.GitInAsync(repo.Path, "add", "-f", "a.secret");
        await ScratchGitRepo.GitInAsync(repo.Path, "commit", "-m", "force");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.secret"), "changed");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "a.secret" }, message = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("ignored_path_staged");
        problem.GetProperty("refusals")[0].GetProperty("path").GetString().ShouldBe("a.secret");
        problem.GetProperty("refusals")[0].GetProperty("rule").GetString()!.ShouldEndWith("*.secret");
    }

    [Test]
    public async Task Commit_with_a_dirty_gitignore_is_409_ignore_rules_changed()
    {
        using var repo = await SeedRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, ".gitignore"), "*.secret\nextra\n");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        var head = (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "HEAD")).StdOut.Trim();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { ".gitignore" }, message = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("ignore_rules_changed");
        (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(head);
    }

    [Test]
    public async Task Commit_of_a_clean_tree_is_409_nothing_to_commit()
    {
        using var repo = await SeedRepoAsync();
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "tracked.txt" }, message = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("nothing_to_commit");
    }

    [Test]
    public async Task Commit_with_a_failing_hook_is_409_commit_failed()
    {
        using var repo = await SeedRepoAsync();
        await repo.InstallFailingPreCommitHookAsync("gate says no");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "x.md" }, message = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("commit_failed");
        problem.GetProperty("stderr").GetString().ShouldContain("gate says no");
    }

    [Test]
    public async Task Commit_under_a_held_lease_is_409_repository_busy()
    {
        using var repo = await SeedRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        var leases = _factory.Services.GetRequiredService<Antiphon.Server.Application.Interfaces.IRepositoryMutationLease>();
        await using var held = await leases.TryAcquireAsync(repo.Path, CancellationToken.None);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "x.md" }, message = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("repository_busy");
    }

    [Test]
    public async Task Commit_without_a_token_is_403()
    {
        using var repo = await SeedRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        var head = (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "HEAD")).StdOut.Trim();
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "x.md" }, message = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("delegation_token_required");
        (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(head);
    }

    [Test]
    public async Task Commit_by_a_Never_task_that_is_not_a_commit_child_is_403()
    {
        using var repo = await SeedRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Docs, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "x.md" }, message = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .ShouldBe("commit_on_settle_never");
    }

    [Test]
    public async Task Commit_by_a_commit_child_with_Never_is_allowed()
    {
        using var repo = await SeedRepoAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "x.md" }, message = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Commit_never_pushes()
    {
        using var repo = await SeedRepoAsync();
        await repo.AddBareOriginAsync();
        var origin = (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "origin/master")).StdOut.Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var token = "tok-" + Guid.NewGuid().ToString("N");
        var task = await SeedTaskAsync(repo.Path, token, AgentTaskRole.Commit, CommitOnSettlePolicy.Never);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
        var response = await client.PostAsJsonAsync($"/api/agent-tasks/{task.Id}/commit",
            new { paths = new[] { "x.md" }, message = "x" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _factory.Spy.Verbs.ShouldNotContain("push");
        (await ScratchGitRepo.GitInAsync(repo.Path, "rev-parse", "origin/master")).StdOut.Trim().ShouldBe(origin);
    }

    private static async Task<ScratchGitRepo> SeedRepoAsync()
    {
        var repo = new ScratchGitRepo("c527-ep");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, ".gitignore"), "*.secret\n");
        await repo.CommitFileAsync("tracked.txt", "t\n");
        return repo;
    }

    private async Task<AgentTask> SeedTaskAsync(
        string repoPath, string token, AgentTaskRole role, CommitOnSettlePolicy? policy)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "commit endpoint",
            Goal = "commit",
            Role = role,
            Kind = AgentTaskKind.Worker,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = repoPath,
            RepoPath = repoPath,
            Status = AgentTaskStatus.Working,
            CommitOnSettle = policy,
            TokenHash = AgentTaskService.HashToken(token),
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }
}

public sealed class CommitEndpointWebAppFactory : AntiphonWebAppFactory
{
    public RecordingGitWorkspaceService Spy { get; } = new();
    public CommitChainBoundary Boundary { get; } = new();

    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        var hosted = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
        foreach (var descriptor in hosted)
            services.Remove(descriptor);
        services.RemoveAll<GitWorkspaceService>();
        services.AddGitWorkspaceService(Spy);
        services.AddSingleton<LandDeliveryBoundary>(Boundary);
        services.RemoveAll<PtyDeliveryProfile>();
        services.AddSingleton(sp => new PtyDeliveryProfile(sp.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PtyDeliveryProfile>.Instance,
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Antiphon.Server.Application.Settings.DelegationSettings>>(),
            TimeProvider.System, "modern"));
        services.RemoveAll<Antiphon.Server.Application.Interfaces.IDelegateSessionStopper>();
        services.AddSingleton<Antiphon.Server.Application.Interfaces.IDelegateSessionStopper, RecordingSessionStopper>();
    }
}

public sealed class CommitChainBoundary : LandDeliveryBoundary
{
    public Guid? InterruptedTask { get; set; }
    public bool Interrupted { get; private set; }
    public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
    {
        if (boundary == "settlement-saved" && taskId == InterruptedTask)
        {
            InterruptedTask = null;
            Interrupted = true;
            throw new InvalidOperationException("C527 producer interrupted after durable settlement");
        }
        return Task.CompletedTask;
    }
}
