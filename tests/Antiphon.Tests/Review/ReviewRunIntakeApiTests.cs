using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Review.Execution;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Server.Infrastructure.Review;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

[Category("Integration")]
[NotInParallel("review-intake-api")]
[ParallelLimiter<ProcessSpawnLimit>]
[ClassDataSource<ReviewRunIntakeApiTests.Factory>(Shared = SharedType.PerClass)]
public sealed class ReviewRunIntakeApiTests(ReviewRunIntakeApiTests.Factory factory)
{
    private const string Repo = "acme/widgets";

    [Test]
    public async Task Repeated_intake_returns_one_run(CancellationToken ct)
    {
        const int number = 209600;
        using var client = OpenPullRequest(number);
        var created = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
        var existing = await IntakeAsync(client, Repo, number, HttpStatusCode.OK, ct);

        Id(existing).ShouldBe(Id(created));
        created.GetProperty("existing").GetBoolean().ShouldBeFalse();
        existing.GetProperty("existing").GetBoolean().ShouldBeTrue();
        await AssertStoredRunsAsync(number, 1, ct);
    }

    [Test]
    public async Task Every_non_closed_status_keeps_its_run(CancellationToken ct)
    {
        var cases = Enum.GetValues<ReviewRunStatus>().Where(status => status != ReviewRunStatus.Closed)
            .Select(status => (Status: status, Stage: ReviewStage.Intake))
            .Select(item => item.Status == ReviewRunStatus.Failed ? (item.Status, Stage: ReviewStage.Review) : item)
            .Append((Status: ReviewRunStatus.Failed, Stage: ReviewStage.Publish))
            .ToArray();

        for (var i = 0; i < cases.Length; i++)
        {
            var number = 209610 + i;
            var (status, stage) = cases[i];
            using var client = OpenPullRequest(number);
            var created = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var run = await db.ReviewRuns.SingleAsync(r => r.Id == Id(created), ct);
                run.Status = status;
                run.Stage = stage;
                await db.SaveChangesAsync(ct);
            }

            var existing = await IntakeAsync(client, Repo, number, HttpStatusCode.OK, ct);
            Id(existing).ShouldBe(Id(created), $"{status} at {stage}");
            existing.GetProperty("status").GetString().ShouldBe(status.ToString());
            existing.GetProperty("stage").GetString().ShouldBe(stage.ToString());

            if (status == ReviewRunStatus.Failed)
            {
                using var refreshed = await client.PostAsync($"/api/review-runs/{Id(created)}/refresh", null, ct);
                refreshed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
                var retried = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{Id(created)}", ct);
                Id(retried).ShouldBe(Id(created));
                retried.GetProperty("round").GetInt32().ShouldBe(1);
                retried.GetProperty("iterations").EnumerateArray().Last().GetProperty("trigger").GetString().ShouldBe("recheck");
            }

            await AssertStoredRunsAsync(number, 1, ct);
        }
    }

    [Test]
    public async Task Close_checks_token_preserves_audit_and_allows_a_new_run(CancellationToken ct)
    {
        const int number = 209630;
        using var client = OpenPullRequest(number);
        client.DefaultRequestHeaders.Add("X-Review-Viewer", "operator-alex");
        var created = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
        var id = Id(created);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = await db.ReviewRuns.SingleAsync(r => r.Id == id, ct);
            run.Status = ReviewRunStatus.AtGate;
            run.Stage = ReviewStage.HumanGate;
            run.WaitingOn = ReviewWaitingOn.Humans;
            db.ReviewGates.Add(new ReviewGate
            {
                Id = Guid.NewGuid(), RunId = id, Kind = ReviewGateKind.ReviewGate, NodeId = "human-review",
                Iteration = 1, Status = ReviewQueueItemStatus.Unassigned, OpenedAt = DateTime.UtcNow,
            });
            db.ReviewNodeExecutions.Add(new ReviewNodeExecution
            {
                Id = Guid.NewGuid(), RunId = id, NodeId = "human-review", Iteration = 1, Sequence = 2,
                Kind = ReviewNodeKind.Gate, Status = ReviewNodeStatus.Waiting,
            });
            await db.SaveChangesAsync(ct);
        }

        var closeUri = $"/api/review-runs/{id}/close";
        var previousToken = created.TryGetProperty("concurrencyToken", out var token) ? token.GetUInt32() : uint.MaxValue;
        using var stale = await client.PostAsJsonAsync(closeUri, new { concurrencyToken = previousToken, reason = "stale" }, ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict, await stale.Content.ReadAsStringAsync(ct));
        var atGate = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}", ct);
        var concurrencyToken = atGate.GetProperty("concurrencyToken").GetUInt32();
        var request = new { concurrencyToken, reason = "  superseded by the author  " };
        using var closed = await client.PostAsJsonAsync(closeUri, request, ct);
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(ct));
        var detail = await closed.Content.ReadFromJsonAsync<JsonElement>(ct);
        detail.GetProperty("status").GetString().ShouldBe("Closed");
        detail.GetProperty("stage").GetString().ShouldBe("Done");
        var closure = detail.GetProperty("closure");
        closure.GetProperty("actor").GetString().ShouldBe("operator-alex");
        closure.GetProperty("reason").GetString().ShouldBe("superseded by the author");
        var gate = detail.GetProperty("gates").EnumerateArray().Single();
        gate.GetProperty("status").GetString().ShouldBe("Done");
        gate.GetProperty("reasons").EnumerateArray().Single().GetString().ShouldBe("closed: superseded by the author");
        var gateNode = detail.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("nodeId").GetString() == "human-review");
        gateNode.GetProperty("status").GetString().ShouldBe("Skipped");

        using var repeated = await client.PostAsJsonAsync(closeUri, request with { reason = "another reason" }, ct);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var repeatedDetail = await repeated.Content.ReadFromJsonAsync<JsonElement>(ct);
        repeatedDetail.GetProperty("closure").ToString().ShouldBe(closure.ToString());
        repeatedDetail.GetProperty("concurrencyToken").GetUInt32().ShouldBe(detail.GetProperty("concurrencyToken").GetUInt32());

        var replacement = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
        Id(replacement).ShouldNotBe(Id(created));
        await AssertStoredRunsAsync(number, 2, ct);
    }

    [Test]
    public async Task Parallel_intakes_save_one_run_and_return_one_created_response(CancellationToken ct)
    {
        const int number = 209640;
        factory.Race.Arm(number);
        using var client = OpenPullRequest(number);

        var first = client.PostAsJsonAsync("/api/review-runs", new { host = "GitHub", repo = Repo, prNumber = number }, ct);
        var second = client.PostAsJsonAsync("/api/review-runs", new { host = "GitHub", repo = Repo, prNumber = number }, ct);
        var responses = await Task.WhenAll(first, second);
        using var left = responses[0];
        using var right = responses[1];

        responses.Select(r => r.StatusCode).Order().ShouldBe(new[] { HttpStatusCode.OK, HttpStatusCode.Created });
        Id(await left.Content.ReadFromJsonAsync<JsonElement>(ct)).ShouldBe(Id(await right.Content.ReadFromJsonAsync<JsonElement>(ct)));
        await AssertStoredRunsAsync(number, 1, ct);
    }

    [Test]
    public async Task Repo_case_and_pull_request_URL_resolve_to_the_same_run(CancellationToken ct)
    {
        const int number = 209650;
        using var client = OpenPullRequest(number);
        var created = await IntakeAsync(client, " Acme/Widgets ", number, HttpStatusCode.Created, ct);
        var byRepo = await IntakeAsync(client, Repo, number, HttpStatusCode.OK, ct);
        Id(byRepo).ShouldBe(Id(created));
        var byUrl = await IntakeAsync(client, $"https://GitHub.com/ACME/widgets/pull/{number}?tab=files", null, HttpStatusCode.OK, ct);
        Id(byUrl).ShouldBe(Id(created));
        var byPath = await IntakeAsync(client, $"ACME/widgets/{number}", null, HttpStatusCode.OK, ct);
        Id(byPath).ShouldBe(Id(created));

        using var mismatch = await client.PostAsJsonAsync("/api/review-runs",
            new { host = "GitHub", repo = $"https://github.com/acme/widgets/pull/{number}", prNumber = number + 1 }, ct);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        await AssertStoredRunsAsync(number, 1, ct);
    }

    [Test]
    public async Task Repository_rename_preserves_the_open_run(CancellationToken ct)
    {
        const int number = 209660;
        using var client = OpenPullRequest(number);
        var original = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);

        var renamed = await IntakeAsync(client, "transferred/widgets", number, HttpStatusCode.OK, ct);

        Id(renamed).ShouldBe(Id(original));
        await AssertStoredRunsAsync(number, 1, ct);
    }

    [Test]
    public async Task Reused_slug_creates_a_run_for_the_new_repository(CancellationToken ct)
    {
        const int number = 209661;
        using var client = OpenPullRequest(number);
        var original = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
        factory.Host.Pr(number).RepositoryId = 9876543210;

        var replacement = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);

        Id(replacement).ShouldNotBe(Id(original));
    }

    [Test]
    public async Task Rebinding_a_slug_to_another_GitHub_authority_keeps_runs_separate(CancellationToken ct)
    {
        const int number = 209662;
        const string repo = "acme/enterprise";
        using var client = OpenPullRequest(number);
        var original = await IntakeAsync(client, repo, number, HttpStatusCode.Created, ct);
        var options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ReviewCodeHostSettings>>().Value;
        options.Hosts["intake-test-enterprise"].BaseUrl = "https://second.example/api/v3/";

        var replacement = await IntakeAsync(client, repo, number, HttpStatusCode.Created, ct);

        Id(replacement).ShouldNotBe(Id(original));
    }

    [Test]
    public async Task Demo_does_not_reserve_a_real_PR_slot(CancellationToken ct)
    {
        const int number = 812;
        using var client = OpenPullRequest(number);
        using var demo = await client.PostAsync("/api/review-runs/demo", null, ct);
        demo.StatusCode.ShouldBe(HttpStatusCode.OK);
        var examples = (await demo.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items");
        var sample = examples.EnumerateArray().Single(example =>
            example.GetProperty("subject").GetProperty("repo").GetString() == "acme/orders-service"
            && example.GetProperty("subject").GetProperty("prNumber").GetInt32() == number);

        var real = await IntakeAsync(client, "acme/orders-service", number, HttpStatusCode.Created, ct);

        Id(real).ShouldNotBe(Id(sample));
    }

    [Test]
    public async Task Legacy_slug_without_an_id_does_not_identify_a_live_repository(CancellationToken ct)
    {
        const int number = 209667;
        using var client = OpenPullRequest(number);
        var legacy = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var changed = await db.ReviewRuns.Where(r => r.Id == Id(legacy)).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.GitHubRepositoryId, (long?)null)
            .SetProperty(r => r.GitHubRepo, Repo), ct);
        changed.ShouldBe(1);

        var verified = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);

        Id(verified).ShouldNotBe(Id(legacy));
        (await db.ReviewRuns.CountAsync(r => r.GitHubPrNumber == number && r.Status != ReviewRunStatus.Closed, ct)).ShouldBe(2);
    }

    [Test]
    public async Task Close_during_merge_retry_refuses_the_second_write(CancellationToken ct)
    {
        const int number = 209663;
        using var client = OpenPullRequest(number);
        var created = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
        var id = Id(created);

        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var run = await db.ReviewRuns.Include(r => r.Subject).Include(r => r.Iterations).SingleAsync(r => r.Id == id, ct);
        var walk = new ReviewWalk(services, db, run, run.Iterations.Single(),
            services.GetRequiredService<ReviewWorkflowService>().Default, TimeProvider.System);
        var merge = services.GetRequiredService<GitHubCodeHost>().MergeAsync(walk.Binding.Target, run.Subject.HeadSha, "squash", ct);
        await factory.MergeHandler.FirstAttempt.Task.WaitAsync(ct);
        using var closed = await client.PostAsJsonAsync($"/api/review-runs/{id}/close",
            new { concurrencyToken = created.GetProperty("concurrencyToken").GetUInt32(), reason = "author withdrew approval" }, ct);
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(ct));

        // The first HTTP response was 405. Any second PUT would succeed on this code host.
        (await Should.ThrowAsync<CodeHostException>(async () => await merge)).Kind.ShouldBe(CodeHostFailureKind.Rejected);
        factory.MergeHandler.Writes.ShouldBe(1);
    }

    [Test]
    [Arguments(209664, "busy")]
    [Arguments(209665, "eligible")]
    [Arguments(209666, "enqueue-failed")]
    public async Task Close_before_executor_records_task_handle_reconciles_the_real_queue(int number, string recipient,
        CancellationToken ct)
    {
        using var client = OpenPullRequest(number);
        var created = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
        var runId = Id(created);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nodeId = Guid.NewGuid();
        db.ReviewNodeExecutions.Add(new ReviewNodeExecution
        {
            Id = nodeId, RunId = runId, NodeId = "reviewer", Kind = ReviewNodeKind.Agent,
            ItemKey = "concurrency", Iteration = 1, Sequence = 2, Status = ReviewNodeStatus.Running,
        });
        await db.SaveChangesAsync(ct);

        var connection = db.Database.GetConnectionString()!;
        var queueOptions = new BridgeQueueHarness.HarnessOptions
        {
            ClaudeDialogObservation = true, ConnectionString = connection, PreserveDatabaseOnDispose = true,
        };
        Guid taskId;
        DispatchBriefEnsureRequest request;
        Guid sessionId;
        Guid agentId;
        await using (var harness = await BridgeQueueHarness.CreateAsync(queueOptions))
        {
            sessionId = harness.SessionId;
            agentId = harness.AgentId;
            var tasks = scope.ServiceProvider.GetRequiredService<IReviewTaskGateway>();
            var goal = $"Review acme/widgets PR #{number} for the Antiphon review engine (run {runId:D}, iteration 1, round 1, reviewer group concurrency, attempt 1).";
            var create = new CreateAgentTaskRequest(Goal: goal, Title: "Close race reviewer",
                Kind: AgentTaskKind.Worker, Role: AgentTaskRole.Review, Workspace: WorkspaceMode.ReadOnly,
                WorkingDirectory: harness.TempRoot, CommitOnSettle: "Never", AgentKind: AgentKind.ClaudeCode,
                ModelLevel: AgentModelLevel.High);
            taskId = await tasks.CreateAsync(create, null, harness.TempRoot, ct);
            var when = DateTime.UtcNow;
            var sessionStarted = await db.AgentSessions.Where(s => s.Id == sessionId).Select(s => s.StartedAt).SingleAsync(ct);
            var claimed = await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, AgentTaskStatus.Dispatched).SetProperty(t => t.DispatchedAt, when)
                .SetProperty(t => t.AgentId, agentId).SetProperty(t => t.AgentSessionId, sessionId), ct);
            claimed.ShouldBe(1);
            var attempt = await db.AgentTasks.Where(t => t.Id == taskId).Select(t => t.Attempt).SingleAsync(ct);
            request = new DispatchBriefEnsureRequest(taskId, attempt, sessionId, when, sessionStarted);

            if (recipient == "busy")
                await harness.MarkWorkingAsync();

            if (recipient == "enqueue-failed")
            {
                harness.Queue.BeforeDispatchBriefRowLock = _ => throw new IOException("injected before queue insert");
                await Should.ThrowAsync<IOException>(() => harness.Queue.EnsureDispatchBriefAsync(request, ct));
            }
            else
            {
                (await harness.Queue.EnsureDispatchBriefAsync(request, ct)).Inserted.ShouldBeTrue();
            }
            if (recipient == "eligible")
            {
                await harness.Queue.FlushSessionAsync(sessionId, ct);
                var delivered = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.ExecutionTaskId == taskId, ct);
                delivered.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
                var prompt = await db.TranscriptEntries.AsNoTracking()
                    .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt).Select(t => t.Text).SingleAsync(ct);
                PromptSubmissionMatch.IsCompleteIn(delivered.Body, prompt!).ShouldBeTrue();
                await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Blocked), ct);
            }
            else
                harness.Adapter.SubmittedBodies.ShouldBeEmpty();

            // The dispatch has a task id. The executor has not copied that id into OutputJson.
            (await db.ReviewNodeExecutions.AsNoTracking().SingleAsync(n => n.Id == nodeId, ct)).OutputJson.ShouldBeNull();
            using var closed = await client.PostAsJsonAsync($"/api/review-runs/{runId}/close",
                new { concurrencyToken = created.GetProperty("concurrencyToken").GetUInt32(), reason = "stop this reviewer" }, ct);
            closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(ct));
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct)).Status.ShouldBe(AgentTaskStatus.Canceled);
            factory.ReviewerStopper.Killed.ShouldContain(sessionId);
        }

        // A fresh queue process can try the same lost enqueue; the terminal task is durable.
        await using var restarted = await BridgeQueueHarness.CreateAsync(queueOptions with { AttachSessionId = sessionId, AttachAgentId = agentId });
        (await restarted.Queue.EnsureDispatchBriefAsync(request, ct)).Inserted.ShouldBeFalse();
        await restarted.Queue.FlushSessionAsync(sessionId, ct);
        restarted.Adapter.SubmittedBodies.ShouldBeEmpty();
        var rows = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.ExecutionTaskId == taskId).ToListAsync(ct);
        if (recipient == "enqueue-failed")
            rows.ShouldBeEmpty();
        else
            rows.ShouldHaveSingleItem().Status.ShouldBe(recipient == "busy" ? QueuedMessageStatus.Canceled : QueuedMessageStatus.Sent);
    }

    private HttpClient OpenPullRequest(int number)
    {
        factory.Host.Open(number, "Fix the lease", "author", new string('a', 40), new string('b', 40), [], "");
        return factory.CreateClient();
    }

    private async Task AssertStoredRunsAsync(int number, int total, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var runs = db.ReviewRuns.Where(r => r.Subject.PrNumber == number);
        (await runs.CountAsync(ct)).ShouldBe(total);
        (await runs.CountAsync(r => r.Status != ReviewRunStatus.Closed, ct)).ShouldBe(1);
        (await db.ReviewSubjects.CountAsync(s => s.PrNumber == number, ct)).ShouldBe(total);
    }

    private static Guid Id(JsonElement run) => run.GetProperty("id").GetGuid();

    private static async Task<JsonElement> IntakeAsync(HttpClient client, string repo, int? prNumber, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("/api/review-runs", new { host = "GitHub", repo, prNumber }, ct);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public sealed class Factory : AntiphonWebAppFactory
    {
        internal ScriptedCodeHost Host { get; } = new();
        internal IntakeRace Race { get; } = new();
        internal MergeRetryHandler MergeHandler { get; } = new();
        internal RecordingSessionStopper ReviewerStopper { get; } = new();

        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            var dispatcher = services.SingleOrDefault(s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType == typeof(AgentTaskDispatcherHostedService));
            if (dispatcher is not null)
                services.Remove(dispatcher);
            services.RemoveAll<ICodeHost>();
            services.AddSingleton<ICodeHost>(Host);
            services.RemoveAll<IDelegateSessionStopper>();
            services.AddSingleton<IDelegateSessionStopper>(ReviewerStopper);
            services.AddHttpClient<GitHubCodeHost>().ConfigurePrimaryHttpMessageHandler(() => MergeHandler);
            services.PostConfigure<ReviewExecutorSettings>(options => options.Enabled = false);
            services.PostConfigure<ReviewSettings>(options =>
            {
                options.AllowViewerHeader = true;
                options.DemoSeedEnabled = true;
            });
            services.PostConfigure<ReviewCodeHostSettings>(options =>
            {
                options.Hosts["intake-test-enterprise"] = new ReviewCodeHost { BaseUrl = "https://first.example/api/v3" };
                options.Repos.Add(new ReviewCodeHostRepo { Repo = "acme/enterprise", Host = "intake-test-enterprise" });
            });
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(Race));
        }
    }

    internal sealed class MergeRetryHandler : HttpMessageHandler
    {
        private int _writes;
        public int Writes => Volatile.Read(ref _writes);
        public TaskCompletionSource FirstAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/repos/acme/widgets")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":1234}""") });
            request.Method.ShouldBe(HttpMethod.Put);
            request.RequestUri!.AbsolutePath.ShouldBe("/repos/acme/widgets/pulls/209663/merge");
            if (Interlocked.Increment(ref _writes) == 1)
            {
                FirstAttempt.TrySetResult();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
                    { Content = new StringContent("""{"message":"not mergeable yet"}""") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"merged":true}""") });
        }
    }

    // Both requests have passed the existing-run query and are ready to insert in independent DbContexts.
    internal sealed class IntakeRace : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _number;
        private int _arrived;

        public void Arm(int number) => Volatile.Write(ref _number, number);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ReviewRun>().Any(entry =>
                    entry.State == EntityState.Added && entry.Entity.Subject.PrNumber == Volatile.Read(ref _number)))
            {
                if (Interlocked.Increment(ref _arrived) == 2)
                    _bothReady.TrySetResult();
                await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }

            return result;
        }
    }
}
