using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>
/// The real <see cref="DelegationReviewAgentRunner"/> under the real executor and Postgres, with the
/// delegation seam (<see cref="IReviewTaskGateway"/>) scripted so no agent launches: the reviewer node
/// dispatches one read-only Review task in a worktree at the PR head, parks Waiting with the task id,
/// and completes from the task's report on a later pass (or fails with the task's reason).
/// </summary>
[Category("Integration")]
[NotInParallel("ReviewDelegationRunner")]
[ClassDataSource<ReviewDelegationRunnerApiTests.Factory>(Shared = SharedType.PerClass)]
public class ReviewDelegationRunnerApiTests(ReviewDelegationRunnerApiTests.Factory factory)
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(60);
    private static readonly string BaseSha = new('a', 40);
    private const string Repo = "acme/delegated";
    private const string RepoPath = @"C:\repos\acme-delegated";
    private const string Anchor = "finally { _lock.Release(); }";

    private const string Diff = """
        diff --git a/src/Lease.cs b/src/Lease.cs
        --- a/src/Lease.cs
        +++ b/src/Lease.cs
        @@ -10,4 +10,6 @@ public sealed class Lease
             public async Task SaveAsync(CancellationToken ct)
             {
        -        await _db.SaveChangesAsync(ct);
        +        try { await _db.SaveChangesAsync(ct); }
        +        finally { _lock.Release(); }
        +        _released = true;
             }
        """;

    private static readonly IReadOnlyList<CodeHostChangedFile> Files = [new("src/Lease.cs", CodeHostFileStatus.Modified, 3, 1)];

    [Before(Test)]
    public async Task ResetAsync()
    {
        await factory.ResetAsync();
        factory.Decisions.Reset();
        factory.Tasks.Reset();
        await factory.EnsureProjectAsync();
    }

    [Test]
    public async Task The_reviewer_is_a_read_only_review_task_at_the_head_and_its_report_completes_the_node()
    {
        const int number = 301;
        var head = new string('b', 40);
        factory.Host.Open(number, "Release the lease after the save", "dana", BaseSha, head, Files, Diff);
        Script();
        using var client = factory.CreateClient();
        var id = await CreateRunAsync(client, number);

        // 1. One task is dispatched and the reviewer node parks with its id; the executor waits on nothing.
        var (request, projectId, callerDirectory, taskId) = await WaitForTaskAsync();
        request.Kind.ShouldBe(AgentTaskKind.Worker);
        request.Role.ShouldBe(AgentTaskRole.Review);
        request.Workspace.ShouldBe(WorkspaceMode.Worktree);
        request.WorktreeBaseRequestedRef.ShouldBe(head);
        request.CommitOnSettle.ShouldBe("Never");
        request.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        request.ModelLevel.ShouldBe(AgentModelLevel.High, "SEED-0001 is major, so its group is the deep tier");
        request.RunnerId.ShouldBeNull();
        request.Goal.ShouldContain("read-only code review");
        request.Goal.ShouldContain("@abf 1 review-findings/1.0");
        request.Goal.ShouldContain("Assigned checks (each gets one row in the checks record): SEED-0001");
        request.Goal.ShouldContain("--- context pack", Case.Sensitive, "a small pack is inlined");
        request.Goal.ShouldContain(Anchor);
        request.Goal.Length.ShouldBeLessThanOrEqualTo(DelegationReviewAgentRunner.MaxGoalChars);
        projectId.ShouldBe(factory.ProjectId);
        callerDirectory.ShouldBe(RepoPath);
        factory.Tasks.Heads.ShouldHaveSingleItem().ShouldBe((RepoPath, ReviewHost.GitHub, (int?)number, head));

        var parked = await WaitForRunAsync(client, id, r => Reviewer(r)?.GetProperty("status").GetString() == "Waiting"
            && Node(r, "review").GetProperty("status").GetString() == "Waiting");
        Reviewer(parked)!.Value.GetProperty("summary").GetString()!.ShouldContain(taskId.ToString("D"));
        parked.GetProperty("waitingOn").GetString().ShouldBe("Bot");
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        factory.Tasks.Created.Count.ShouldBe(1, "a pass that finds the task still running dispatches nothing");

        // 2. The task settles with a review-findings document; a later pass completes the node and the walk goes on.
        factory.Tasks.Finish(taskId, AgentTaskStatus.Succeeded,
            "Reviewed the lease change.\n\n" + ScriptedReviewer.Document(["SEED-0001"],
            [
                new ScriptedFinding("SEED-0001", "major", 0.9, "src/Lease.cs", Anchor, "The release runs before the save is flushed",
                    "The lock is released while the flush is pending."),
            ]) + "\n--- next stage ---\nnext: done\n");
        var run = await WaitForRunAsync(client, id, r => r.GetProperty("status").GetString() == "AwaitingFixes");
        var reviewer = Reviewer(run)!.Value;
        reviewer.GetProperty("status").GetString().ShouldBe("Succeeded");
        reviewer.GetProperty("summary").GetString()!.ShouldStartWith("1 findings");
        reviewer.GetProperty("provider").GetString().ShouldBe("delegate-claudecode");
        reviewer.GetProperty("transport").GetString().ShouldBe("Seat");
        reviewer.GetProperty("inputTokens").GetInt64().ShouldBe(4000);
        reviewer.GetProperty("costUsd").GetDecimal().ShouldBe(0.37m);
        Node(run, "review").GetProperty("status").GetString().ShouldBe("Succeeded");
        factory.Tasks.Created.Count.ShouldBe(1, "polling never dispatches a second task");

        var findings = (await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}/findings")).GetProperty("items").EnumerateArray().ToList();
        var finding = findings.ShouldHaveSingleItem();
        finding.GetProperty("checkId").GetString().ShouldBe("SEED-0001");
        finding.GetProperty("severity").GetString().ShouldBe("Major");

        // 3. The pack a too-large brief points at is served as text.
        using var pack = await client.GetAsync($"/api/review-runs/{id}/packs/1/{Uri.EscapeDataString(Reviewer(run)!.Value.GetProperty("itemKey").GetString()!)}");
        pack.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await pack.Content.ReadAsStringAsync()).ShouldContain("# Output contract");
    }

    [Test]
    public async Task A_failed_reviewer_task_fails_the_node_with_its_reason()
    {
        const int number = 302;
        factory.Host.Open(number, "Release the lease after the save", "dana", BaseSha, new string('c', 40), Files, Diff);
        Script();
        using var client = factory.CreateClient();
        var id = await CreateRunAsync(client, number);
        var (_, _, _, taskId) = await WaitForTaskAsync();

        factory.Tasks.Finish(taskId, AgentTaskStatus.Failed, null, "the worktree could not be provisioned");
        var run = await WaitForRunAsync(client, id, r => r.GetProperty("status").GetString() == "Failed");
        var reviewer = Reviewer(run)!.Value;
        reviewer.GetProperty("status").GetString().ShouldBe("Failed");
        reviewer.GetProperty("summary").GetString()!.ShouldContain("failed: the worktree could not be provisioned");
        Node(run, "review").GetProperty("summary").GetString()!.ShouldContain("the worktree could not be provisioned");
    }

    [Test]
    [Arguments("https://github.com/acme/widgets", true)]
    [Arguments("https://github.com/acme/widgets.git/", true)]
    [Arguments("git@github.com:Acme/Widgets.git", true)]
    [Arguments("ssh://git@ghe.example.com/acme/widgets.git", true)]
    [Arguments("https://github.com/acme/widgets-two", false)]
    [Arguments("", false)]
    public async Task A_project_url_names_the_repo_in_any_git_url_form(string url, bool names)
    {
        DelegationReviewAgentRunner.RepoUrlNames(url, "acme/widgets").ShouldBe(names);
        await Task.CompletedTask;
    }

    private void Script()
    {
        factory.Decisions.Categorise(externalContract: 0.1);
        factory.Decisions.Yes("applies_seed_0001", 0.9).Yes("applies_seed_0002", 0.1).Yes("applies_seed_0003", 0.05)
            .Yes("applies_seed_0004", 0.1).Yes("applies_seed_0005", 0.2).Yes("applies_seed_0006", 0.2).Yes("applies_seed_0007", 0.1);
    }

    private static async Task<string> CreateRunAsync(HttpClient client, int number)
    {
        using var created = await client.PostAsJsonAsync("/api/review-runs", new { host = "GitHub", repo = Repo, prNumber = number });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private async Task<(CreateAgentTaskRequest Request, Guid? ProjectId, string CallerDirectory, Guid Id)> WaitForTaskAsync()
    {
        var deadline = DateTime.UtcNow + StepTimeout;
        while (factory.Tasks.Created.IsEmpty)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"No reviewer task was dispatched within {StepTimeout}.");
            await Task.Delay(100);
        }

        return factory.Tasks.Created.ShouldHaveSingleItem();
    }

    private static async Task<JsonElement> WaitForRunAsync(HttpClient client, string id, Func<JsonElement, bool> done)
    {
        var deadline = DateTime.UtcNow + StepTimeout;
        while (true)
        {
            var run = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}");
            if (done(run))
                return run;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Run {id} did not settle within {StepTimeout}: {run}");
            await Task.Delay(100);
        }
    }

    private static JsonElement? Reviewer(JsonElement run) =>
        run.GetProperty("nodes").EnumerateArray().Where(n => n.GetProperty("nodeId").GetString() == "reviewer")
            .Select(n => (JsonElement?)n).LastOrDefault();

    private static JsonElement Node(JsonElement run, string nodeId) =>
        run.GetProperty("nodes").EnumerateArray().Last(n => n.GetProperty("nodeId").GetString() == nodeId && n.GetProperty("itemKey").ValueKind == JsonValueKind.Null);

    /// <summary>The real host and reviewer runner, with the code host, decisions, verifier and delegation seam scripted.</summary>
    public sealed class Factory : AntiphonWebAppFactory
    {
        public ScriptedCodeHost Host { get; } = new();
        public ScriptedDecisions Decisions { get; } = new();
        public ScriptedCompletions Completions { get; } = new();
        public ScriptedTaskGateway Tasks { get; } = new();
        public Guid ProjectId { get; } = Guid.NewGuid();

        /// <summary>The project whose URL names the repo, so the runner has a checkout to dispatch into.</summary>
        public async Task EnsureProjectAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.Projects.FindAsync(ProjectId) is not null)
                return;
            db.Projects.Add(new Project
            {
                Id = ProjectId,
                Name = "acme-delegated-" + ProjectId.ToString("N")[..6],
                GitRepositoryUrl = "git@github.com:acme/delegated.git",
                LocalRepositoryPath = RepoPath,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            services.RemoveAll<ICodeHost>();
            services.AddSingleton<ICodeHost>(Host);
            services.RemoveAll<IDecisionModel>();
            services.AddSingleton<IDecisionModel>(Decisions);
            services.RemoveAll<ICompletionModel>();
            services.AddSingleton<ICompletionModel>(Completions);
            services.RemoveAll<IReviewTaskGateway>();
            services.AddSingleton<IReviewTaskGateway>(Tasks);
            services.PostConfigure<ReviewCompletionSettings>(s =>
            {
                s.Providers["scripted-verifier"] = new ReviewCompletionProvider { Dialect = "scripted", Model = ScriptedCompletions.Model };
                s.Chains["completions-cheap"] = [new ReviewDecisionChainEntry { Provider = "scripted-verifier" }];
            });
            services.PostConfigure<ReviewExecutorSettings>(s => s.PollSeconds = 1);
        }
    }

    /// <summary>The delegation seam: records every head and task, and answers reads with the state the test set.</summary>
    public sealed class ScriptedTaskGateway : IReviewTaskGateway
    {
        private readonly ConcurrentDictionary<Guid, ReviewTaskState> _states = new();

        public ConcurrentQueue<(CreateAgentTaskRequest Request, Guid? ProjectId, string CallerDirectory, Guid Id)> Created { get; } = new();
        public ConcurrentQueue<(string RepoPath, ReviewHost Host, int? PrNumber, string HeadSha)> Heads { get; } = new();
        public ConcurrentQueue<(Guid Id, string Reason)> Cancelled { get; } = new();

        public void Reset()
        {
            _states.Clear();
            Created.Clear();
            Heads.Clear();
            Cancelled.Clear();
        }

        public void Finish(Guid id, AgentTaskStatus status, string? result, string? failure = null) =>
            _states[id] = _states[id] with
            {
                Status = status, Result = result, FailureReason = failure, InputTokens = 4000, OutputTokens = 600, CostUsd = 0.37m,
                CompletedAt = DateTime.UtcNow,
            };

        public Task EnsureHeadAsync(string repoPath, ReviewHost host, int? prNumber, string headSha, CancellationToken ct)
        {
            Heads.Enqueue((repoPath, host, prNumber, headSha));
            return Task.CompletedTask;
        }

        public Task<Guid> CreateAsync(CreateAgentTaskRequest request, Guid? projectId, string callerDirectory, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            _states[id] = new ReviewTaskState(id, AgentTaskStatus.Queued, null, null, null, request.AgentKind ?? AgentKind.ClaudeCode,
                request.ModelLevel ?? AgentModelLevel.High, null, 0, 0, 0m, DateTime.UtcNow, null);
            Created.Enqueue((request, projectId, callerDirectory, id));
            return Task.FromResult(id);
        }

        public Task<ReviewTaskState?> ReadAsync(Guid taskId, CancellationToken ct) => Task.FromResult(_states.GetValueOrDefault(taskId));

        public Task CancelAsync(Guid taskId, string reason, CancellationToken ct)
        {
            Cancelled.Enqueue((taskId, reason));
            return Task.CompletedTask;
        }
    }
}
